using System.Collections.Concurrent;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using ErrorOr;
using JasperFx.CodeGeneration;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Travel.Modules.Trips.Api.Composition;
using Travel.Modules.Trips.Api.Contracts;
using Travel.Modules.Trips.Api.Endpoints;
using Travel.Modules.Trips.Api.Middleware;
using Travel.Modules.Trips.Application;
using Travel.Modules.Trips.Application.BriefExtraction;
using Travel.Modules.Trips.Application.Itineraries;
using Travel.Modules.Trips.Core;
using Travel.Modules.Trips.Infrastructure.Readiness;
using Travel.Modules.Trips.Infrastructure.Serialization;
using Wolverine;
using Wolverine.Http;

namespace Travel.Modules.Trips.Tests.Integration;

internal sealed class TripsHttpFixture : IAsyncDisposable
{
    internal const string Issuer = "https://fictional.trips.test/realms/travel";
    internal static readonly Guid Owner = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    internal WebApplication App { get; private set; } = null!;
    internal HttpClient Client { get; private set; } = null!;
    internal MemoryTripStore Store { get; } = new();
    internal TestProtection Protection { get; } = new();
    internal TripsTestLogs Logs { get; } = new();
    internal FictionalTripsAuthenticationProbe Authentication { get; } = new();

    internal static async Task<TripsHttpFixture> Start(
        bool enabled = true,
        bool ready = true,
        bool itinerary = false,
        Action<IServiceCollection>? configure = null,
        string environment = "Development",
        bool defaultMap = false,
        bool briefExtraction = false
    )
    {
        var fixture = new TripsHttpFixture();
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions
            {
                ApplicationName = typeof(TripsHttpFixture).Assembly.FullName,
                EnvironmentName = environment,
            }
        );
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(fixture.Logs);
        // Match the accepted Host threshold: framework request-starting Information logs include raw query strings.
        builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                { "Trips:Enabled", enabled.ToString() },
                { "Trips:Itinerary:Enabled", itinerary.ToString() },
                { "Trips:BriefExtraction:Enabled", briefExtraction.ToString() },
                { "Trips:BriefExtraction:ProcessorId", "anthropic" },
                { "Trips:BriefExtraction:ModelId", "fictional-model" },
                { "Trips:BriefExtraction:PrivacyNoticeVersion", "trips-brief-ru-v1" },
            }
        );
        builder.Services.AddSingleton(new TripsFeature(enabled));
        builder.Services.AddSingleton(
            TripsBriefExtractionFeature.Read(builder.Configuration, builder.Environment)
        );
        if (enabled && briefExtraction && environment != Environments.Production)
        {
            builder.Services.AddSingleton<TripBriefOwnerAdmission>();
            builder.Services.AddScoped<ITripBriefExtractionClient>(_ =>
                throw new InvalidOperationException(
                    "Fictional extraction transport must be supplied explicitly for POST."
                )
            );
            builder.Services.AddScoped<TripBriefExtractionService>();
        }
        builder.Services.AddSingleton(
            new TripsItineraryFeature(
                enabled && itinerary && environment != Environments.Production
            )
        );
        builder.Services.AddProblemDetails();
        builder.Services.AddSingleton(fixture.Authentication);
        if (enabled)
        {
            builder.Services.AddSingleton<ITripPlanStore>(fixture.Store);
            builder.Services.AddSingleton<ITripPlanProtector>(fixture.Protection);
            builder.Services.AddSingleton<ITripPlanCursor>(new FixtureCursor());
            builder.Services.AddSingleton<ITripPlanCodec, TripPlanJsonCodec>();
            builder.Services.AddSingleton(TimeProvider.System);
            builder.Services.AddScoped<TripPlanService>();
            builder.Services.AddSingleton<ITripsReadiness>(new Ready(ready));
        }
        if (enabled && itinerary && environment != Environments.Production)
        {
            builder.Services.AddSingleton<IItineraryFactSource>(new FictionalItinerarySource());
            builder.Services.AddSingleton<
                IItineraryCandidateResponseEncoder,
                ItineraryCandidateResponseEncoder
            >();
            builder.Services.AddSingleton<ItineraryAdmissionGate>();
            builder.Services.AddScoped<ItineraryCandidateService>();
            builder.Services.AddSingleton<TimeProvider>(new ItineraryHttpClock());
        }
        configure?.Invoke(builder.Services);
        builder
            .Services.AddAuthentication("FictionalTrips")
            .AddScheme<AuthenticationSchemeOptions, FictionalTripsAuth>("FictionalTrips", _ => { });
        builder.Services.AddAuthorization(options =>
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build()
        );
        builder.Services.AddWolverine(options =>
        {
            options.ApplicationAssembly = typeof(TripsHttpFixture).Assembly;
            options.Discovery.DisableConventionalDiscovery();
            TripsModule.ConfigureWolverine(options, builder.Configuration);
            options.Durability.Mode = DurabilityMode.MediatorOnly;
            options.CodeGeneration.TypeLoadMode = TypeLoadMode.Auto;
        });
        builder.Services.AddWolverineHttp();
        fixture.App = builder.Build();
        fixture.App.UseTripsResponsePolicy();
        fixture.App.UseAuthentication();
        fixture.App.UseAuthorization();
        fixture.App.UseTripsModule();
        if (defaultMap)
            fixture.App.MapWolverineEndpoints();
        else
            fixture.App.MapWolverineEndpoints(options => options.WarmUpRoutes = RouteWarmup.Lazy);
        await fixture.App.StartAsync();
        fixture.Client = fixture.App.GetTestClient();
        fixture.Client.DefaultRequestHeaders.Add("X-Trips-Owner", Owner.ToString());
        return fixture;
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await App.DisposeAsync();
    }

    private sealed class Ready(bool ready) : ITripsReadiness
    {
        public Task<bool> IsReadyAsync(CancellationToken ct) => Task.FromResult(ready);
    }

    private sealed class FixtureCursor : ITripPlanCursor
    {
        private readonly Dictionary<
            string,
            (OwnerIdentity Owner, TripPlanPosition Position)
        > values = [];

        public ErrorOr<string> Encode(OwnerIdentity owner, TripPlanPosition position)
        {
            var key = Guid.NewGuid().ToString("N");
            values[key] = (owner, position);
            return key;
        }

        public ErrorOr<TripPlanPosition> Decode(OwnerIdentity owner, string value) =>
            values.TryGetValue(value, out var item) && item.Owner == owner
                ? item.Position
                : TripErrors.InvalidCursor;
    }
}

public sealed class FictionalTripsAuth(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder
) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        Context.RequestServices.GetRequiredService<FictionalTripsAuthenticationProbe>().Calls++;
        if (!Request.Headers.TryGetValue("X-Trips-Owner", out var owner))
            return Task.FromResult(AuthenticateResult.NoResult());
        var claims = new List<Claim>
        {
            new("iss", TripsHttpFixture.Issuer, ClaimValueTypes.String, TripsHttpFixture.Issuer),
            new("sub", owner.ToString(), ClaimValueTypes.String, TripsHttpFixture.Issuer),
        };
        if (Request.Headers.TryGetValue("X-Trips-OtherSubject", out var other))
            claims.Add(
                new("sub", other.ToString(), ClaimValueTypes.String, TripsHttpFixture.Issuer)
            );
        return Task.FromResult(
            AuthenticateResult.Success(
                new AuthenticationTicket(
                    new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name)),
                    Scheme.Name
                )
            )
        );
    }
}

internal sealed class TestProtection : ITripPlanProtector
{
    internal int Reads { get; private set; }

    public Task<ErrorOr<string>> ProtectAsync(
        string text,
        TripPlanBinding binding,
        CancellationToken ct
    ) => Task.FromResult<ErrorOr<string>>(text);

    public Task<ErrorOr<string>> UnprotectAsync(
        string text,
        TripPlanBinding binding,
        CancellationToken ct
    )
    {
        Reads++;
        return Task.FromResult<ErrorOr<string>>(text);
    }
}

internal sealed class MemoryTripStore : ITripPlanStore
{
    internal ConcurrentDictionary<Guid, TripPlanRecord> Rows { get; } = [];
    internal int Loads { get; private set; }
    private readonly object gate = new();

    public Task<ErrorOr<TripPlanRecord?>> LoadAsync(
        OwnerIdentity owner,
        Guid id,
        CancellationToken ct
    )
    {
        Loads++;
        return Task.FromResult<ErrorOr<TripPlanRecord?>>(
            Rows.TryGetValue(id, out var row) && row.Owner == owner ? row : null
        );
    }

    public Task<ErrorOr<TripPlanRecordPage>> ListAsync(
        OwnerIdentity owner,
        TripPlanPosition? after,
        int limit,
        CancellationToken ct
    ) =>
        Task.FromResult<ErrorOr<TripPlanRecordPage>>(
            new TripPlanRecordPage(
                Rows.Values.Where(row =>
                        row.Owner == owner
                        && (
                            after is null
                            || row.UpdatedAt < after.UpdatedAt
                            || row.UpdatedAt == after.UpdatedAt
                                && row.TripId.CompareTo(after.TripId) > 0
                        )
                    )
                    .OrderByDescending(row => row.UpdatedAt)
                    .ThenBy(row => row.TripId)
                    .Take(limit)
                    .ToArray(),
                Rows.Values.Count(row => row.Owner == owner) > limit
            )
        );

    public Task<ErrorOr<Success>> TryCreateAsync(TripPlanRecord row, int cap, CancellationToken ct)
    {
        lock (gate)
        {
            if (Rows.Values.Count(x => x.Owner == row.Owner) >= cap)
                return Task.FromResult<ErrorOr<Success>>(TripErrors.CapacityExceeded);
            Rows[row.TripId] = row;
            return Task.FromResult<ErrorOr<Success>>(Result.Success);
        }
    }

    public Task<ErrorOr<Success>> TryReplaceAsync(
        long expected,
        TripPlanRecord row,
        CancellationToken ct
    )
    {
        lock (gate)
        {
            if (!Rows.TryGetValue(row.TripId, out var old) || old.Owner != row.Owner)
                return Task.FromResult<ErrorOr<Success>>(TripErrors.NotFound);
            if (old.Revision != expected)
                return Task.FromResult<ErrorOr<Success>>(TripErrors.RevisionMismatch);
            Rows[row.TripId] = row;
            return Task.FromResult<ErrorOr<Success>>(Result.Success);
        }
    }

    public Task<ErrorOr<Success>> TryDeleteAsync(
        OwnerIdentity owner,
        Guid id,
        long expected,
        CancellationToken ct
    )
    {
        lock (gate)
        {
            if (!Rows.TryGetValue(id, out var old) || old.Owner != owner)
                return Task.FromResult<ErrorOr<Success>>(TripErrors.NotFound);
            if (old.Revision != expected)
                return Task.FromResult<ErrorOr<Success>>(TripErrors.RevisionMismatch);
            Rows.TryRemove(id, out _);
            return Task.FromResult<ErrorOr<Success>>(Result.Success);
        }
    }
}

public sealed class FictionalTripsAuthenticationProbe
{
    internal int Calls { get; set; }
}
