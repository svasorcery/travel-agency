using Alba;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Travel.Modules.Flights.Application.Idempotency;
using Travel.Modules.Flights.Application.Notifications;
using Travel.Modules.Flights.Application.Observability;
using Travel.Modules.Flights.Application.Queries;
using Travel.Modules.Flights.Application.Search;
using Travel.Modules.Flights.Application.Webhooks;
using Travel.Modules.Flights.Core.Providers;
using Travel.Modules.Flights.Infrastructure.Providers.Duffel;
using Travel.Shared.TestInfrastructure;
using Wolverine;
using Xunit;

namespace Travel.Host.Tests.Integration.Flights;

/// <summary>
/// Verifies the Flights module is fully wired into <c>Travel.Host</c>: every service the
/// Wolverine handlers / HTTP endpoints depend on must resolve from the host container, the
/// search/booking providers must all be registered, and the Wolverine.Http endpoints must be
/// discovered and mapped. Without this the booking + search pipeline would 500 (or 404) at
/// runtime even though the host boots.
/// </summary>
[Trait("Category", "Integration")]
public sealed class FlightsModuleWiringTests : IntegrationTestBase
{
    private IAlbaHost _host = default!;

    protected override async ValueTask OnInitializedAsync()
    {
        _host = await AlbaHost.For<Program>(builder =>
        {
            builder.UseSetting("ConnectionStrings:travel", ConnectionString);
            builder.UseSetting("OTEL_EXPORTER_OTLP_ENDPOINT", "");
            // No NATS broker in this test — stub Wolverine's external transports so the
            // host boots; handler/endpoint discovery and DI are what we're verifying.
            builder.ConfigureServices(s => s.DisableAllExternalWolverineTransports());
        });
    }

    protected override async ValueTask OnDisposingAsync() => await _host.DisposeAsync();

    [Theory]
    [InlineData(typeof(IFlightsMetrics))]
    [InlineData(typeof(ISearchMetrics))]
    [InlineData(typeof(ISearchCache))]
    [InlineData(typeof(IDeeplinkOfferCache))]
    [InlineData(typeof(IFxRates))]
    [InlineData(typeof(IIdempotencyStore))]
    [InlineData(typeof(IWebhookInboxStore))]
    [InlineData(typeof(IOrderReadModelQueries))]
    [InlineData(
        typeof(Travel.Modules.Flights.Application.Handlers.Booking.IOrderReadModelProjector)
    )]
    [InlineData(typeof(IOrderSseRegistry))]
    [InlineData(typeof(IEmailSender))]
    [InlineData(typeof(IEmailRenderer))]
    [InlineData(typeof(IUserDirectory))]
    [InlineData(typeof(IPaymentGateway))]
    [InlineData(typeof(DuffelWebhookVerifier))]
    public void Flights_service_resolves_from_host_container(Type serviceType)
    {
        using var scope = _host.Services.CreateScope();

        var resolved = scope.ServiceProvider.GetService(serviceType);

        resolved.ShouldNotBeNull($"{serviceType.Name} is not DI-registered in Travel.Host");
    }

    [Fact]
    public void Both_flight_search_providers_are_registered()
    {
        using var scope = _host.Services.CreateScope();

        var providers = scope.ServiceProvider.GetServices<IFlightSearchProvider>().ToList();

        // Duffel (bookable) + Travelpayouts (deeplink).
        providers.Count.ShouldBe(2);
    }

    [Fact]
    public void Flight_booking_provider_is_registered()
    {
        using var scope = _host.Services.CreateScope();

        var providers = scope.ServiceProvider.GetServices<IFlightBookingProvider>().ToList();

        providers.ShouldNotBeEmpty();
    }

    [Fact]
    public void Flights_http_endpoints_are_discovered_and_mapped()
    {
        var routes = _host
            .Services.GetRequiredService<EndpointDataSource>()
            .Endpoints.OfType<RouteEndpoint>()
            .Select(e => e.RoutePattern.RawText)
            .ToList();

        routes.ShouldContain("/api/flights/search");
        routes.ShouldContain("/api/flights/orders/hold");
        routes.ShouldContain("/api/flights/orders");
        routes.ShouldContain("/webhooks/duffel");
    }

    /// <summary>
    /// Drift-detection tripwire: the route paths declared by <see cref="FlightsApiFixture"/>
    /// (its hand-maintained convenience list) must be a subset of the real route paths
    /// discovered by Wolverine.Http from the actual endpoint attributes. If this test fails,
    /// a route has been added to or removed from the real endpoints without updating
    /// <c>FlightsApiFixture.FixtureRoutePaths</c>.
    /// </summary>
    [Fact]
    public void Real_endpoint_routes_match_fixture_declarations()
    {
        var realRoutes = _host
            .Services.GetRequiredService<EndpointDataSource>()
            .Endpoints.OfType<RouteEndpoint>()
            .Select(e => e.RoutePattern.RawText)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var fixtureRoutes = FlightsApiFixture.FixtureRoutePaths;

        var missing = fixtureRoutes.Where(r => !realRoutes.Contains(r)).ToList();

        missing.ShouldBeEmpty(
            $"Fixture declares routes that are not present in the real EndpointDataSource. "
                + $"Either remove them from FlightsApiFixture.FixtureRoutePaths or restore the "
                + $"real endpoint: {string.Join(", ", missing)}"
        );
    }

    /// <summary>
    /// Verifies that the booking endpoints discovered by the real Wolverine HTTP pipeline
    /// carry <c>[Authorize("flights:book")]</c> metadata. This ensures that adding a new
    /// booking endpoint without the correct policy attribute is caught before runtime.
    /// </summary>
    [Fact]
    public void Discovered_booking_endpoints_require_flights_book_scope()
    {
        var allRoutes = _host
            .Services.GetRequiredService<EndpointDataSource>()
            .Endpoints.OfType<RouteEndpoint>()
            .ToList();

        // These are the write-path endpoints that must require the flights:book scope.
        var bookingPatterns = new[]
        {
            "/api/flights/orders/hold",
            "/api/flights/orders/confirm",
            "/api/flights/orders/{aggregateId:guid}/cancel",
        };

        foreach (var pattern in bookingPatterns)
        {
            var endpoint = allRoutes.FirstOrDefault(r =>
                string.Equals(r.RoutePattern.RawText, pattern, StringComparison.OrdinalIgnoreCase)
            );

            endpoint.ShouldNotBeNull($"Route '{pattern}' was not discovered by Wolverine.Http");

            var authorizeData = endpoint!.Metadata.OfType<IAuthorizeData>().ToList();
            authorizeData.ShouldNotBeEmpty(
                $"Route '{pattern}' has no [Authorize] metadata — add [Authorize(\"flights:book\")]"
            );
            authorizeData.ShouldContain(
                a => a.Policy == "flights:book",
                $"Route '{pattern}' does not require the flights:book policy"
            );
        }
    }
}
