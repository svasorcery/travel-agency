using System.Collections.Concurrent;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Travel.Modules.Flights.Api.Composition;
using Travel.Modules.Flights.Api.Endpoints;
using Travel.Modules.Flights.Api.Middleware;
using Travel.Modules.Flights.Application;
using Travel.Modules.Flights.Application.Idempotency;
using Travel.Modules.Flights.Application.Notifications;
using Travel.Modules.Flights.Application.Privacy;
using Travel.Modules.Identity.Infrastructure.Authentication;
using Travel.ServiceDefaults.Web;
using Travel.Shared.TestInfrastructure;
using Travel.Tests.Fixtures;
using Wolverine;
using Xunit;

namespace Travel.Host.Tests.Integration.Flights;

/// <summary>
/// xUnit class fixture that hosts the Flights HTTP endpoints in a lean in-memory
/// <see cref="TestServer"/>. It reproduces the real <c>Travel.Host</c> request pipeline —
/// authentication, the <c>RequireAuthenticatedUser</c> fallback policy, the per-endpoint
/// <c>[Authorize]</c> / <c>[AllowAnonymous]</c> intent, the idempotency middleware, routing
/// and model binding — but stubs the message bus (<see cref="FakeMessageBus"/>) and the
/// idempotency store, so no Postgres / NATS / Wolverine runtime is required. The host boots
/// once per test class.
/// </summary>
public sealed class FlightsApiFixture : IAsyncLifetime
{
    private WebApplication _app = default!;

    public HttpClient Client { get; private set; } = default!;

    public FakeMessageBus Bus { get; } = new();
    public FakeSavedTravelerService SavedTravelers { get; } = new();
    public TestPassengerProtector PassengerProtector { get; } = new();
    public TestPassengerPartyProtector PassengerPartyProtector { get; } = new();
    public RecordingFlightsLogs Logs { get; } = new();

    public FakeIdempotencyStore IdempotencyStore { get; } = new();

    public FakeOrderSseRegistry SseRegistry { get; } = new();

    public async ValueTask InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.AddProvider(Logs);

        builder.Services.AddSingleton<IMessageBus>(Bus);
        builder.Services.AddSingleton<Travel.Modules.Flights.Application.SavedTravelers.ISavedTravelerService>(
            SavedTravelers
        );
        builder.Services.AddSingleton<IBookingPassengerProtector>(PassengerProtector);
        builder.Services.AddSingleton<IBookingPassengerPartyProtector>(PassengerPartyProtector);
        builder.Services.AddSingleton<IIdempotencyStore>(IdempotencyStore);
        builder.Services.AddSingleton<IOrderSseRegistry>(SseRegistry);
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddProblemDetails(PlatformProblemDetails.Configure);

        // Feature flags — default all enabled; individual tests can override via Bus.On.
        builder.Services.AddSingleton<IOptions<FlightsFeatureFlags>>(
            Options.Create(new FlightsFeatureFlags())
        );
        builder.Services.AddSingleton<IOptionsMonitor<FlightsFeatureFlags>>(
            new StubOptionsMonitor<FlightsFeatureFlags>(new FlightsFeatureFlags())
        );

        // Test auth scheme + the same fallback policy Program.cs applies.
        builder
            .Services.AddAuthentication("ProfileBoundary")
            .AddPolicyScheme(
                "ProfileBoundary",
                null,
                options =>
                    options.ForwardDefaultSelector = context =>
                        context.Request.Path.StartsWithSegments(
                            "/api/flights/travelers",
                            StringComparison.OrdinalIgnoreCase
                        )
                        && context
                            .Request.Headers.Authorization.ToString()
                            .StartsWith("Bearer ", StringComparison.Ordinal)
                            ? "ProfileJwt"
                            : TestAuthHandler.SchemeName
            )
            .AddJwtBearer(
                "ProfileJwt",
                options =>
                {
                    options.MapInboundClaims = false;
                    options.Authority = null;
                    options.MetadataAddress = string.Empty;
                    options.IncludeErrorDetails = false;
                    options.TokenValidationParameters =
                        new Microsoft.IdentityModel.Tokens.TokenValidationParameters
                        {
                            ValidateIssuer = true,
                            ValidIssuer = ProfileJwtTestTokens.Issuer,
                            ValidateAudience = true,
                            ValidAudience = ProfileJwtTestTokens.Audience,
                            ValidateLifetime = true,
                            ValidateIssuerSigningKey = true,
                            IssuerSigningKey = ProfileJwtTestTokens.ValidationKey(),
                        };
                }
            )
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                TestAuthHandler.SchemeName,
                _ => { }
            );
        builder.Services.AddTransient<
            IClaimsTransformation,
            NormalizedIdentityClaimsTransformation
        >();
        builder.Services.AddAuthorization(options =>
        {
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();
            options.AddPolicy(
                "flights:book",
                policy => policy.RequireAuthenticatedUser().RequireClaim("scope", "flights:book")
            );
        });

        _app = builder.Build();

        _app.UseFlightsResponsePolicy();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.UseFlightsModule();

        MapFlightsEndpoints(_app);

        await _app.StartAsync();
        Client = _app.GetTestServer().CreateClient();
    }

    // IMPORTANT: This fixture re-declares the Flights routes manually so that the HTTP-pipeline
    // tests can run without Docker, Testcontainers or a live PostgreSQL / NATS connection.
    // Refactoring to AlbaHost.For<Program>() (the pattern used in FlightsModuleWiringTests)
    // would add a Testcontainers dependency and make every test in FlightsEndpointsHttpTests
    // require Docker — these tests intentionally carry no [Trait("Category","Integration")] and
    // must remain lightweight. This is therefore a deliberate fixture-level convenience.
    //
    // CONSEQUENCE: the authorization metadata here (.RequireAuthorization / .AllowAnonymous)
    // is hand-maintained and could drift from the real [Authorize] / [AllowAnonymous] attributes
    // on the endpoint classes in Travel.Modules.Flights.Api.
    //
    // SOURCE OF TRUTH for the actual [Authorize("flights:book")] metadata is:
    //   FlightsModuleWiringTests.Discovered_booking_endpoints_require_flights_book_scope
    // A drift-detection tripwire (Real_endpoint_routes_match_fixture_declarations) is present in
    // FlightsModuleWiringTests and will fail if this list diverges from the real EndpointDataSource.

    /// <summary>
    /// Maps the Flights endpoints with the same routes and the same authorization intent as
    /// the <c>[WolverinePost]/[WolverineGet]</c> + <c>[Authorize]/[AllowAnonymous]</c>
    /// attributes on the endpoint classes in <c>Travel.Modules.Flights.Api</c>.
    /// </summary>
    private static void MapFlightsEndpoints(WebApplication app)
    {
        app.MapGet("/api/flights/travelers", ListSavedTravelersEndpoint.Get)
            .RequireAuthorization("flights:book");
        app.MapGet("/api/flights/travelers/{travelerId:guid}", GetSavedTravelerEndpoint.Get)
            .RequireAuthorization("flights:book");
        app.MapPut("/api/flights/travelers/{travelerId:guid}", PutSavedTravelerEndpoint.Put)
            .RequireAuthorization("flights:book");
        app.MapDelete(
                "/api/flights/travelers/{travelerId:guid}",
                DeleteSavedTravelerEndpoint.Delete
            )
            .RequireAuthorization("flights:book");
        app.MapPost("/api/flights/search", SearchEndpoint.Post).AllowAnonymous();
        app.MapPost("/api/flights/search/v2", MultiLegSearchEndpoint.Post).AllowAnonymous();
        app.MapPost("/api/flights/search/nl", NlSearchEndpoint.Post).AllowAnonymous();
        app.MapPost("/api/flights/orders/quote", QuoteOfferEndpoint.Post).AllowAnonymous();

        app.MapPost("/api/flights/orders/hold", HoldOfferEndpoint.Post)
            .RequireAuthorization("flights:book");
        app.MapPost("/api/flights/orders/confirm", ConfirmOrderEndpoint.Post)
            .RequireAuthorization("flights:book");
        app.MapPost("/api/flights/orders/{aggregateId:guid}/cancel", CancelOrderEndpoint.Post)
            .RequireAuthorization("flights:book");
        app.MapGet("/api/flights/orders/{aggregateId:guid}", GetOrderEndpoint.Get)
            .RequireAuthorization();
        app.MapGet("/api/flights/orders", ListOrdersEndpoint.Get).RequireAuthorization();
        app.MapGet("/events/flights/orders/{orderId:guid}", OrderEventsSseEndpoint.Stream)
            .RequireAuthorization();
    }

    /// <summary>
    /// Returns the set of route paths this fixture maps, used by
    /// <c>FlightsModuleWiringTests.Real_endpoint_routes_match_fixture_declarations</c>
    /// to detect drift between the fixture's hand-coded list and the real endpoint metadata.
    /// </summary>
    internal static IReadOnlyList<string> FixtureRoutePaths { get; } =
    [
        "/api/flights/travelers",
        "/api/flights/travelers/{travelerId:guid}",
        "/api/flights/search",
        "/api/flights/search/v2",
        "/api/flights/search/nl",
        "/api/flights/orders/quote",
        "/api/flights/orders/hold",
        "/api/flights/orders/confirm",
        "/api/flights/orders/{aggregateId:guid}/cancel",
        "/api/flights/orders/{aggregateId:guid}",
        "/api/flights/orders",
        "/events/flights/orders/{orderId:guid}",
    ];

    /// <summary>
    /// Real endpoint routes that are intentionally absent from this fixture, with documented
    /// reasons. Used by
    /// <c>FlightsModuleWiringTests.All_real_flights_routes_are_covered_by_fixture_or_documented_exclusion</c>
    /// so that a new uncovered route causes a test failure rather than silent omission.
    /// </summary>
    internal static IReadOnlyList<string> FixtureExcludedRoutePaths { get; } =
    [
        // /webhooks/duffel forwards raw bytes and headers to the Application-owned
        // IWebhookIngestionService; the lean fixture intentionally has no signed provider
        // request, PostgreSQL inbox, or Wolverine outbox. DuffelWebhookEndpointTests cover
        // the endpoint-to-service transport mapping, DuffelWebhookIngestionPortTests exercise
        // the real port with PostgreSQL/Wolverine atomicity, and AspireStackSmokeTests covers
        // the signed HTTP delivery plus duplicate processing through the complete stack.
        "/webhooks/duffel",
    ];

    public async ValueTask DisposeAsync()
    {
        Client?.Dispose();
        if (_app is not null)
            await _app.DisposeAsync();
    }
}

public sealed class TestPassengerPartyProtector : IBookingPassengerPartyProtector
{
    public bool Available { get; set; } = true;
    public int ProtectCount { get; private set; }
    public BookingPassengerPartyProtectionContext? LastContext { get; private set; }

    public void Reset()
    {
        Available = true;
        ProtectCount = 0;
        LastContext = null;
    }

    public ErrorOr.ErrorOr<Travel.Modules.Flights.Core.ValueObjects.ProtectedPassengerPartySnapshot> Protect(
        BookingPassengerPartyProtectionContext context,
        Travel.Shared.Abstractions.EquatableArray<Travel.Modules.Flights.Core.ValueObjects.BookingPassenger> passengers
    )
    {
        ProtectCount++;
        LastContext = context;
        return Available
            ? TestPii.PartyProtector.Protect(context, passengers)
            : PiiProtectionErrors.Unavailable;
    }

    public ErrorOr.ErrorOr<Travel.Shared.Abstractions.EquatableArray<Travel.Modules.Flights.Core.ValueObjects.BookingPassenger>> Unprotect(
        BookingPassengerPartyProtectionContext context,
        Travel.Modules.Flights.Core.ValueObjects.ProtectedPassengerPartySnapshot snapshot
    ) =>
        Available
            ? TestPii.PartyProtector.Unprotect(context, snapshot)
            : PiiProtectionErrors.PayloadUnavailable;
}

public sealed class RecordingFlightsLogs : ILoggerProvider
{
    public ConcurrentQueue<string> Messages { get; } = new();

    public ILogger CreateLogger(string categoryName) => new RecordingLogger(Messages);

    public void Dispose() { }

    private sealed class RecordingLogger(ConcurrentQueue<string> messages) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel level) => true;

        public void Log<TState>(
            LogLevel level,
            EventId id,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        ) => messages.Enqueue(formatter(state, exception));
    }
}

public sealed class TestPassengerProtector : IBookingPassengerProtector
{
    public bool Available { get; set; } = true;

    public ErrorOr.ErrorOr<Travel.Modules.Flights.Core.ValueObjects.ProtectedPassengerSnapshot> Protect(
        Guid aggregateId,
        Guid ownerUserId,
        Travel.Modules.Flights.Core.ValueObjects.PassengerInfo passenger
    ) =>
        Available
            ? TestPii.Protector.Protect(aggregateId, ownerUserId, passenger)
            : PiiProtectionErrors.Unavailable;

    public ErrorOr.ErrorOr<Travel.Modules.Flights.Core.ValueObjects.PassengerInfo> Unprotect(
        Guid aggregateId,
        Guid ownerUserId,
        Travel.Modules.Flights.Core.ValueObjects.ProtectedPassengerSnapshot snapshot
    ) =>
        Available
            ? TestPii.Protector.Unprotect(aggregateId, ownerUserId, snapshot)
            : PiiProtectionErrors.PayloadUnavailable;
}
