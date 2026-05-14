using Alba;
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
}
