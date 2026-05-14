using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Shouldly;
using StackExchange.Redis;
using Travel.Modules.Flights.Application.Idempotency;
using Travel.Modules.Flights.Application.Notifications;
using Travel.Modules.Flights.Application.Observability;
using Travel.Modules.Flights.Application.Queries;
using Travel.Modules.Flights.Application.Search;
using Travel.Modules.Flights.Application.Webhooks;
using Travel.Modules.Flights.Core.Providers;
using Travel.Modules.Flights.Infrastructure;
using Travel.Modules.Flights.Infrastructure.ExternalServices;
using Travel.Modules.Flights.Infrastructure.Notifications.Keycloak;
using Travel.Modules.Flights.Infrastructure.Observability;
using Travel.Modules.Flights.Infrastructure.Providers.Duffel;
using Travel.Modules.Flights.Infrastructure.Providers.Travelpayouts;
using Xunit;
using IOrderReadModelProjector = Travel.Modules.Flights.Application.Handlers.Booking.IOrderReadModelProjector;

namespace Travel.Modules.Flights.Tests.Unit.Composition;

/// <summary>
/// Guards the Flights module composition root: every interface a Wolverine handler or
/// endpoint depends on must have an implementation registered by <c>AddFlightsModule</c>,
/// otherwise the booking/search pipeline 500s at runtime with an unresolved-service error.
/// Asserts registration presence (no instantiation — so no Postgres/Redis needed).
/// </summary>
public sealed class FlightsModuleRegistrationTests
{
    private static IServiceCollection BuildModuleServices()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:travel"] =
                        "Host=localhost;Database=travel;Username=x;Password=x",
                    ["ConnectionStrings:redis"] = "localhost:6379",
                    ["Flights:Duffel:ApiKey"] = "test",
                    ["Flights:Travelpayouts:ApiToken"] = "test",
                }
            )
            .Build();

        var services = new ServiceCollection();
        services.AddFlightsModule(config, new FakeHostEnvironment());
        return services;
    }

    public static TheoryData<Type> RequiredServiceTypes =>
        new()
        {
            typeof(IFlightSearchProvider),
            typeof(IFlightBookingProvider),
            typeof(IPaymentGateway),
            typeof(IOrderReadModelProjector),
            typeof(IOrderReadModelQueries),
            typeof(IDeeplinkOfferCache),
            typeof(IFxRates),
            typeof(ISearchCache),
            typeof(IWebhookInboxStore),
            typeof(IIdempotencyStore),
            typeof(IUserDirectory),
            typeof(IEmailSender),
            typeof(IEmailRenderer),
            typeof(IOrderSseRegistry),
            typeof(IFlightsMetrics),
            typeof(ISearchMetrics),
            typeof(IKeycloakAdminTokenProvider),
            typeof(IConnectionMultiplexer),
            typeof(DuffelClient),
            typeof(TravelpayoutsClient),
            typeof(FrankfurterClient),
            typeof(DuffelWebhookVerifier),
            typeof(TravelpayoutsDeeplinkBuilder),
            typeof(FlightsMetrics),
            typeof(KeycloakAdminAuthHandler),
        };

    [Theory]
    [MemberData(nameof(RequiredServiceTypes))]
    public void AddFlightsModule_registers(Type serviceType)
    {
        var services = BuildModuleServices();

        services
            .Any(d => d.ServiceType == serviceType)
            .ShouldBeTrue($"{serviceType.Name} is not registered by AddFlightsModule.");
    }

    [Fact]
    public void AddFlightsModule_registers_both_search_providers()
    {
        var services = BuildModuleServices();

        // Search aggregates Duffel (bookable) + Travelpayouts (deeplink) — handlers inject
        // IEnumerable<IFlightSearchProvider>, so both must be registered.
        services.Count(d => d.ServiceType == typeof(IFlightSearchProvider)).ShouldBe(2);
    }

    [Fact]
    public void AddFlightsModule_skips_TestOnly_payment_gateway_in_production()
    {
        var config = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();

        services.AddFlightsModule(
            config,
            new FakeHostEnvironment { EnvironmentName = Environments.Production }
        );

        // The Duffel test wallet is [TestOnly] — it must not be registered in Production
        // (TestOnlyGuard would otherwise throw at host start-up).
        services.Any(d => d.ServiceType == typeof(IPaymentGateway)).ShouldBeFalse();
    }
}

/// <summary>Minimal <see cref="IHostEnvironment"/> for composition-root unit tests.</summary>
file sealed class FakeHostEnvironment : IHostEnvironment
{
    public string EnvironmentName { get; set; } = Environments.Development;
    public string ApplicationName { get; set; } = "Travel.Modules.Flights.Tests.Unit";
    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
