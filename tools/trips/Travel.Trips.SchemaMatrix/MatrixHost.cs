extern alias TravelHostApp;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Travel.Modules.Trips.Infrastructure.Privacy;
using Wolverine;
using Wolverine.Runtime;

namespace Travel.Trips.SchemaMatrix;

internal sealed record FixtureConfig(string Connection, TripsProtectionOptions Protection)
{
    public override string ToString() => nameof(FixtureConfig);
}

internal sealed class MatrixHost(FixtureConfig config, bool enableSaving = false)
    : WebApplicationFactory<TravelHostApp::Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var connection = new NpgsqlConnectionStringBuilder(config.Connection);
        if (
            connection.Database != "trips_schema_matrix"
            || !IPAddress.IsLoopback(
                Dns.GetHostAddresses(connection.Host!)
                    .Single(ip => ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            )
        )
            throw new InvalidOperationException(
                "Only the disposable loopback matrix database is supported."
            );
        var hostRoot = Path.Combine(Directory.GetCurrentDirectory(), "apps", "Travel.Host");
        builder.UseContentRoot(hostRoot);
        builder.UseEnvironment("Testing");
        var settings = new Dictionary<string, string?>
        {
            ["environment"] = "Testing",
            ["ConnectionStrings:travel"] = config.Connection,
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
            ["Trips:SavedOptions:Enabled"] = enableSaving.ToString(),
            ["Trips:Itinerary:Enabled"] = enableSaving.ToString(),
            ["Trips:OptionPreview:Enabled"] = enableSaving.ToString(),
            ["Trips:OptionPreview:Profile"] = "spb-option",
            ["Trips:OptionPreview:Catalogue"] = "B",
            ["Trips:Protection:KeyRingPath"] = config.Protection.KeyRingPath,
            ["Trips:Protection:ActiveCertificatePath"] = config.Protection.ActiveCertificatePath,
            ["Trips:Protection:ActiveCertificatePassword"] = config
                .Protection
                .ActiveCertificatePassword,
        };
        foreach (var (key, value) in settings)
            builder.UseSetting(key, value);
        builder.ConfigureAppConfiguration(
            (_, configuration) =>
            {
                configuration.Sources.Clear();
                configuration.AddInMemoryCollection(settings);
            }
        );
        builder.ConfigureLogging(logging => logging.ClearProviders());
        builder.ConfigureServices(services =>
        {
            services.DisableAllExternalWolverineTransports();
            services
                .AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = "Matrix";
                    options.DefaultChallengeScheme = "Matrix";
                })
                .AddScheme<AuthenticationSchemeOptions, MatrixIdentity>("Matrix", _ => { });
        });
    }

    internal static object Brief =>
        new
        {
            destinationId = "saint-petersburg",
            originId = "moscow",
            startDate = "2026-10-28",
            endDate = "2026-10-30",
            adults = 2,
            currency = "RUB",
            totalBudget = (string?)null,
            interests = new[] { "architecture" },
            pace = "relaxed",
        };

    internal static async Task<Guid> CreateManual(HttpClient client, CancellationToken ct)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/trips",
            new { title = "Fictional manual plan", brief = Brief },
            ct
        );
        Require(response, HttpStatusCode.Created);
        using var json = await Read(response, ct);
        return json.RootElement.GetProperty("tripId").GetGuid();
    }

    internal static async Task OpenManual(HttpClient client, Guid id, CancellationToken ct)
    {
        using var response = await client.GetAsync($"/api/trips/{id}", ct);
        Require(response, HttpStatusCode.OK);
        using var json = await Read(response, ct);
        if (
            json.RootElement.GetProperty("tripId").GetGuid() != id
            || json.RootElement.GetProperty("title").GetString() != "Fictional manual plan"
        )
            throw new InvalidOperationException("Manual plan changed across schema versions.");
    }

    internal static async Task<JsonDocument> Read(
        HttpResponseMessage response,
        CancellationToken ct
    ) => JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(ct));

    internal static void Require(HttpResponseMessage response, HttpStatusCode expected)
    {
        if (response.StatusCode != expected)
            throw new InvalidOperationException(
                $"Expected {(int)expected}, received {(int)response.StatusCode}."
            );
    }

    public override async ValueTask DisposeAsync()
    {
        if (Services.GetService<IWolverineRuntime>() is WolverineRuntime runtime)
            runtime.StopMode = StopMode.Quick;
        await base.DisposeAsync();
    }
}

internal sealed class MatrixIdentity(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder
) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        const string issuer = "https://fictional.schema-matrix.test";
        Claim[] claims =
        [
            new("iss", issuer, ClaimValueTypes.String, issuer),
            new("sub", "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa", ClaimValueTypes.String, issuer),
        ];
        return Task.FromResult(
            AuthenticateResult.Success(
                new(new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name)), Scheme.Name)
            )
        );
    }
}
