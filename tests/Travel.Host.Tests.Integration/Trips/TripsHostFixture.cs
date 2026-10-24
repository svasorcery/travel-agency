using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Testcontainers.PostgreSql;
using Travel.Modules.Trips.Infrastructure.Persistence;
using Travel.Modules.Trips.Tests.Integration;
using Wolverine;
using Wolverine.Runtime;

namespace Travel.Host.Tests.Integration.Trips;

// Actual Program.cs, Wolverine policies, EF store and protection. Only identity and external transports are fictional.
internal sealed class TripsHostFixture : IAsyncDisposable
{
    internal const string Issuer = "https://fictional.trips-host.test/realms/travel";
    internal static readonly Guid Owner = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder(
        "pgvector/pgvector:pg17"
    ).Build();
    private readonly TripsTestRing ring = new();
    private Factory? factory;
    internal HttpClient Client { get; private set; } = null!;

    internal TripsDbContext Db()
    {
        var options = new DbContextOptionsBuilder<TripsDbContext>().UseNpgsql(
            postgres.GetConnectionString()
        );
        TripsDbContextConfiguration.Configure(options);
        return new(options.Options);
    }

    internal async Task StartAsync(CancellationToken ct)
    {
        await postgres.StartAsync(ct);
        // Explicit provisioning only in this owned disposable fixture; ordinary Host never provisions Trips.
        await using (var db = Db())
            await db.Database.MigrateAsync(ct);
        StartHost();
    }

    private void StartHost()
    {
        factory = new Factory(postgres.GetConnectionString(), ring);
        factory.ClientOptions.AllowAutoRedirect = false;
        Client = factory.CreateClient();
        Client.DefaultRequestHeaders.Add("X-Trips-Owner", Owner.ToString());
        var configuration = (IConfigurationRoot)
            factory.Services.GetRequiredService<IConfiguration>();
        if (
            !factory.Services.GetRequiredService<IHostEnvironment>().IsEnvironment("Testing")
            || configuration.Providers.Any(p => p is not MemoryConfigurationProvider)
        )
            throw new InvalidOperationException(
                "Trips Host fixture configuration is not isolated."
            );
    }

    internal async Task RestartAsync()
    {
        await StopHostAsync();
        StartHost();
    }

    private async ValueTask StopHostAsync()
    {
        Client?.Dispose();
        if (factory is null)
            return;
        if (factory.Services.GetService<IWolverineRuntime>() is WolverineRuntime runtime)
            runtime.StopMode = StopMode.Quick;
        await factory.DisposeAsync();
        factory = null;
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await StopHostAsync();
        }
        finally
        {
            try
            {
                await postgres.DisposeAsync();
            }
            finally
            {
                ring.Dispose();
            }
        }
    }

    private sealed class Factory(string connection, TripsTestRing ring)
        : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            var root = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (root is not null && !File.Exists(Path.Combine(root.FullName, "Travel.slnx")))
                root = root.Parent;
            if (root is null)
                throw new InvalidOperationException(
                    "Run the Trips Host fixture inside the repository."
                );
            builder.UseContentRoot(Path.Combine(root.FullName, "apps", "Travel.Host"));
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:travel", connection);
            builder.UseSetting("ConnectionStrings:nats", "");
            builder.UseSetting("ConnectionStrings:redis", "");
            builder.UseSetting("OTEL_EXPORTER_OTLP_ENDPOINT", "");
            builder.UseSetting("Trips:Enabled", "true");
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureAppConfiguration(
                (context, configuration) =>
                {
                    if (
                        !context.HostingEnvironment.IsEnvironment("Testing")
                        || configuration
                            .Sources.OfType<JsonConfigurationSource>()
                            .Any(s =>
                                string.Equals(
                                    Path.GetFileName(s.Path),
                                    "secrets.json",
                                    StringComparison.OrdinalIgnoreCase
                                )
                            )
                    )
                        throw new InvalidOperationException(
                            "Saved-secret configuration cannot enter the Trips fixture."
                        );
                    configuration.Sources.Clear();
                    configuration.AddInMemoryCollection(
                        new Dictionary<string, string?>
                        {
                            ["environment"] = "Testing",
                            ["ConnectionStrings:travel"] = connection,
                            ["ConnectionStrings:nats"] = "",
                            ["ConnectionStrings:redis"] = "",
                            ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "",
                            ["HealthEndpoints:InternalPort"] = "5098",
                            ["Keycloak:Authority"] = "",
                            ["Keycloak:Audience"] = "travel-web",
                            ["Flights:FeatureFlags:Travelpayouts:Enabled"] = "false",
                            ["Flights:FeatureFlags:NlSearch:Enabled"] = "false",
                            ["Flights:Duffel:BaseUrl"] = "http://127.0.0.1:1",
                            ["Flights:Providers:Frankfurter:BaseAddress"] = "http://127.0.0.1:1",
                            ["Trips:Enabled"] = "true",
                            ["Trips:Protection:KeyRingPath"] = ring.Settings.KeyRingPath,
                            ["Trips:Protection:ActiveCertificatePath"] =
                                ring.Settings.ActiveCertificatePath,
                            ["Trips:Protection:ActiveCertificatePassword"] =
                                ring.Settings.ActiveCertificatePassword,
                        }
                    );
                }
            );
            builder.ConfigureServices(services =>
            {
                // Retain real transaction middleware and durable local storage; only external transports are disabled.
                services.DisableAllExternalWolverineTransports();
                services
                    .AddAuthentication(options =>
                    {
                        options.DefaultAuthenticateScheme = "TripsHostFictional";
                        options.DefaultChallengeScheme = "TripsHostFictional";
                    })
                    .AddScheme<AuthenticationSchemeOptions, TripsHostAuthentication>(
                        "TripsHostFictional",
                        _ => { }
                    );
            });
        }
    }
}

public sealed class TripsHostAuthentication(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder
) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Guid.TryParse(Request.Headers["X-Trips-Owner"], out var owner) || owner == Guid.Empty)
            return Task.FromResult(AuthenticateResult.NoResult());
        var identity = new ClaimsIdentity(
            [
                new Claim("sub", owner.ToString(), ClaimValueTypes.String, TripsHostFixture.Issuer),
                new Claim(
                    "iss",
                    TripsHostFixture.Issuer,
                    ClaimValueTypes.String,
                    TripsHostFixture.Issuer
                ),
            ],
            Scheme.Name
        );
        return Task.FromResult(
            AuthenticateResult.Success(new(new ClaimsPrincipal(identity), Scheme.Name))
        );
    }
}
