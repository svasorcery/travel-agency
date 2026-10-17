using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Testcontainers.PostgreSql;
using Travel.Modules.Hotels.Infrastructure.Privacy;
using Wolverine;
using Wolverine.Runtime;

namespace Travel.Host.Tests.Integration.Hotels;

// Real Program.cs and durable stores; only the external supplier wire and identity are fictional.
public sealed class HotelsHostFixture : IAsyncDisposable
{
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder(
        "pgvector/pgvector:pg17"
    ).Build();
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        "travel-hotels-host-" + Guid.NewGuid().ToString("N")
    );
    private Factory? factory;
    private bool started;
    private HotelPiiProtectionOptions privacy = null!;
    public TimeProvider Clock { get; init; } = TimeProvider.System;
    public bool IsolatedConfiguration { get; private set; }
    public HotelsFakeSupplier Supplier { get; } = new();
    public HttpClient Client { get; private set; } = null!;
    public IServiceProvider Services => factory!.Services;
    public string Connection => postgres.GetConnectionString();
    public Action<object>? AssetsReady { get; set; }
    public const string Owner = "785e39a4-b13f-4fd8-835b-1ab619cd0e14";
    public const string Account = "org_fictional_nondefault";
    public const string Secret = "fictional-hotels-webhook-secret";

    public async Task StartAsync(CancellationToken ct, int? port = null)
    {
        await postgres.StartAsync(ct);
        AssetsReady?.Invoke(
            new
            {
                ownerPid = Environment.ProcessId,
                containerId = postgres.Id,
                temporaryDirectory = directory,
                fictional = true,
            }
        );
        Directory.CreateDirectory(directory);
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=fictional-hotels-host",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1
        );
        using var cert = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(3)
        );
        var path = Path.Combine(directory, "fictional.pfx");
        await File.WriteAllBytesAsync(path, cert.Export(X509ContentType.Pfx), ct);
        privacy = new HotelPiiProtectionOptions
        {
            KeyRingPath = Path.Combine(directory, "ring"),
            ActiveCertificatePath = path,
        };
        // Explicit fixture-only initialization, separate from ordinary Host startup.
        using (
            var keys = new HotelPiiProtectionProvider(Options.Create(privacy), TimeProvider.System)
        )
            if (keys.Initialize().IsError)
                throw new InvalidOperationException("Fictional fixture keys unavailable.");
        factory = new Factory(Connection, privacy, Supplier, Clock);
        if (port is not null)
            factory.UseKestrel(port.Value);
        factory.ClientOptions.AllowAutoRedirect = false;
        Client = factory.CreateClient();
        started = true;
        VerifyIsolation();
    }

    public async Task RestartAsync()
    {
        Client.Dispose();
        if (factory!.Services.GetService<IWolverineRuntime>() is WolverineRuntime runtime)
            runtime.StopMode = StopMode.Quick;
        await factory.DisposeAsync();
        started = false;
        factory = new Factory(Connection, privacy, Supplier, Clock);
        factory.ClientOptions.AllowAutoRedirect = false;
        Client = factory.CreateClient();
        started = true;
        VerifyIsolation();
    }

    private void VerifyIsolation()
    {
        var environment = Services.GetRequiredService<IHostEnvironment>();
        var configuration = (IConfigurationRoot)Services.GetRequiredService<IConfiguration>();
        IsolatedConfiguration =
            environment.IsEnvironment("Testing")
            && configuration.Providers.All(p => p is MemoryConfigurationProvider);
        if (!IsolatedConfiguration)
            throw new InvalidOperationException("Fictional fixture configuration is not isolated.");
    }

    public async ValueTask DisposeAsync()
    {
        Client?.Dispose();
        try
        {
            if (factory is not null)
            {
                if (
                    started
                    && factory.Services.GetService<IWolverineRuntime>() is WolverineRuntime runtime
                )
                    runtime.StopMode = StopMode.Quick;
                await factory.DisposeAsync();
            }
        }
        finally
        {
            try
            {
                await postgres.DisposeAsync();
            }
            finally
            {
                var full = Path.GetFullPath(directory);
                if (
                    !full.StartsWith(
                        Path.GetFullPath(Path.GetTempPath()),
                        StringComparison.OrdinalIgnoreCase
                    )
                    || !Path.GetFileName(full)
                        .StartsWith("travel-hotels-host-", StringComparison.Ordinal)
                )
                    throw new InvalidOperationException();
                if (Directory.Exists(full))
                    Directory.Delete(full, true);
            }
        }
    }

    private sealed class Factory(
        string connection,
        HotelPiiProtectionOptions privacy,
        HotelsFakeSupplier supplier,
        TimeProvider clock
    ) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            var root = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (root is not null && !File.Exists(Path.Combine(root.FullName, "Travel.slnx")))
                root = root.Parent;
            if (root is null)
                throw new InvalidOperationException(
                    "Run the fictional Hotels fixture inside the repository."
                );
            builder.UseContentRoot(Path.Combine(root.FullName, "apps", "Travel.Host"));
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:travel", connection);
            builder.UseSetting("ConnectionStrings:nats", "");
            builder.UseSetting("ConnectionStrings:redis", "");
            builder.UseSetting("OTEL_EXPORTER_OTLP_ENDPOINT", "");
            builder.UseSetting("Hotels:Enabled", "true");
            builder.UseSetting("Hotels:BookingEnabled", "true");
            builder.UseSetting("Hotels:ReceiverEnabled", "true");
            builder.UseSetting("Hotels:OrganisationId", Account);
            builder.UseSetting("Hotels:ApiKey", "duffel_test_fictional_only");
            builder.UseSetting("Hotels:WebhookSecret", Secret);
            builder.UseSetting("Flights:Duffel:WebhookSecret", Secret);
            builder.UseSetting("Flights:Duffel:OrganisationId", Account);
            builder.UseSetting("Hotels:PiiProtection:KeyRingPath", privacy.KeyRingPath);
            builder.UseSetting(
                "Hotels:PiiProtection:ActiveCertificatePath",
                privacy.ActiveCertificatePath
            );
            builder.ConfigureLogging(o => o.ClearProviders());
            builder.ConfigureAppConfiguration(
                (context, configuration) =>
                {
                    // Inspect source metadata only; never open or probe a saved secret file.
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
                            "Saved-secret configuration cannot enter the fictional fixture."
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
                            ["Keycloak:Audience"] = "travel-web",
                            ["Keycloak:Authority"] = "",
                            ["Flights:FeatureFlags:Travelpayouts:Enabled"] = "false",
                            ["Flights:FeatureFlags:NlSearch:Enabled"] = "false",
                            ["Flights:Duffel:ApiKey"] = "",
                            ["Flights:Duffel:BaseUrl"] = "http://127.0.0.1:1",
                            ["Flights:Duffel:WebhookSecret"] = Secret,
                            ["Flights:Duffel:OrganisationId"] = Account,
                            ["Flights:Providers:Frankfurter:BaseAddress"] = "http://127.0.0.1:1",
                            ["Flights:Providers:Frankfurter:TimeoutSeconds"] = "2",
                            ["Hotels:Enabled"] = "true",
                            ["Hotels:BookingEnabled"] = "true",
                            ["Hotels:ReceiverEnabled"] = "true",
                            ["Hotels:OrganisationId"] = Account,
                            ["Hotels:ApiKey"] = "duffel_test_fictional_only",
                            ["Hotels:WebhookSecret"] = Secret,
                            ["Hotels:PiiProtection:KeyRingPath"] = privacy.KeyRingPath,
                            ["Hotels:PiiProtection:ActiveCertificatePath"] =
                                privacy.ActiveCertificatePath,
                        }
                    );
                }
            );
            builder.ConfigureServices(services =>
            {
                services.AddSingleton(clock);
                services.DisableAllExternalWolverineTransports();
                services.Configure<HotelPiiProtectionOptions>(o =>
                {
                    o.KeyRingPath = privacy.KeyRingPath;
                    o.ActiveCertificatePath = privacy.ActiveCertificatePath;
                });
                services
                    .AddHttpClient("hotels-duffel")
                    .ConfigurePrimaryHttpMessageHandler(() => supplier);
                services
                    .AddAuthentication(o =>
                    {
                        o.DefaultAuthenticateScheme = "HotelsFictional";
                        o.DefaultChallengeScheme = "HotelsFictional";
                    })
                    .AddScheme<AuthenticationSchemeOptions, HotelsFictionalAuthentication>(
                        "HotelsFictional",
                        _ => { }
                    );
            });
        }
    }
}

public sealed class HotelsFictionalAuthentication(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder
) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (Request.Headers.Authorization != "Bearer fictional-hotels-owner")
            return Task.FromResult(AuthenticateResult.NoResult());
        var identity = new ClaimsIdentity(
            [
                new Claim("sub", HotelsHostFixture.Owner),
                new Claim("aud", "travel-web"),
                new Claim("scope", "hotels:book"),
            ],
            Scheme.Name
        );
        return Task.FromResult(
            AuthenticateResult.Success(new(new ClaimsPrincipal(identity), Scheme.Name))
        );
    }
}
