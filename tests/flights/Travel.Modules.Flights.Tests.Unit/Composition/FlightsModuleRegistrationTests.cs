using JasperFx.Events;
using JasperFx.Events.Aggregation;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Shouldly;
using StackExchange.Redis;
using Travel.Modules.Flights.Api.Composition;
using Travel.Modules.Flights.Application.Idempotency;
using Travel.Modules.Flights.Application.Notifications;
using Travel.Modules.Flights.Application.Observability;
using Travel.Modules.Flights.Application.Queries;
using Travel.Modules.Flights.Application.Search;
using Travel.Modules.Flights.Application.Webhooks;
using Travel.Modules.Flights.Core.Aggregates;
using Travel.Modules.Flights.Core.DomainEvents;
using Travel.Modules.Flights.Core.Providers;
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
    private static readonly Type[] ExpectedBookingEventTypes =
    [
        typeof(OfferQuoted),
        typeof(OfferReQuoted),
        typeof(OfferHeld),
        typeof(PaymentAuthorized),
        typeof(OrderConfirmed),
        typeof(OrderTicketed),
        typeof(OrderCancelled),
        typeof(OrderRefunded),
    ];

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

        return BuildModuleServices(config);
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
            typeof(IWebhookIngestionPort),
            typeof(IWebhookIngestionService),
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
        var services = BuildModuleServices(config, Environments.Production);

        // The Duffel test wallet is [TestOnly] — it must not be registered in Production
        // (TestOnlyGuard would otherwise throw at host start-up).
        services.Any(d => d.ServiceType == typeof(IPaymentGateway)).ShouldBeFalse();
    }

    [Fact]
    public void AddFlightsModule_binds_Frankfurter_BaseAddress_from_config()
    {
        // Arrange: supply a custom base address via config to prove the binding is wired.
        const string customBase = "https://fx-stub.local/";
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:redis"] = "localhost:6379",
                    ["Flights:Providers:Frankfurter:BaseAddress"] = customBase,
                }
            )
            .Build();

        var services = BuildModuleServices(config);

        var sp = services.BuildServiceProvider();

        // The IOptions<FrankfurterOptions> snapshot must reflect the configured value.
        var opts = sp.GetRequiredService<IOptions<FrankfurterOptions>>().Value;
        opts.BaseAddress.ShouldBe(customBase);
    }

    [Fact]
    public void FrankfurterOptions_defaults_to_public_endpoint_when_config_key_absent()
    {
        // Defense-in-depth: if the config key is missing the class default kicks in.
        var config = new ConfigurationBuilder().Build();
        var services = BuildModuleServices(config);

        var sp = services.BuildServiceProvider();

        var opts = sp.GetRequiredService<IOptions<FrankfurterOptions>>().Value;
        opts.BaseAddress.ShouldBe("https://api.frankfurter.app/");
    }

    [Fact]
    public void ConfigureMarten_preserves_Host_stream_identity_and_registers_booking_contract()
    {
        var options = new StoreOptions();
        options.Events.StreamIdentity = StreamIdentity.AsString;

        FlightsModule.ConfigureMarten(options);

        options.Events.StreamIdentity.ShouldBe(StreamIdentity.AsString);
        AssertBookingMartenContract(options);
    }

    [Fact]
    public void Booking_Marten_contract_assertion_rejects_missing_contributions()
    {
        var incomplete = new StoreOptions();
        incomplete.Events.StreamIdentity = StreamIdentity.AsString;
        ConfigureIncompleteMarten(incomplete);

        Should.Throw<ShouldAssertException>(() => AssertBookingMartenContract(incomplete));
    }

    private static void AssertBookingMartenContract(StoreOptions options)
    {
        var eventNamespace = typeof(OfferQuoted).Namespace;
        var registeredBookingEvents = ((EventGraph)options.Events)
            .AllKnownEventTypes()
            .Select(eventType => eventType.EventType)
            .Where(eventType => eventType.Namespace == eventNamespace)
            .OrderBy(eventType => eventType.FullName, StringComparer.Ordinal)
            .ToArray();
        var expectedEvents = ExpectedBookingEventTypes
            .OrderBy(eventType => eventType.FullName, StringComparer.Ordinal)
            .ToArray();

        registeredBookingEvents.ShouldBe(expectedEvents);

        var projection = options
            .Projections.All.OfType<IAggregateProjection>()
            .Where(candidate => candidate.AggregateType == typeof(BookingAggregate))
            .ShouldHaveSingleItem();
        projection.Lifecycle.ShouldBe(ProjectionLifecycle.Live);
        projection.Scope.ShouldBe(AggregationScope.SingleStream);
        projection.AllEventTypes.ShouldBe(ExpectedBookingEventTypes, ignoreOrder: true);
    }

    private static void ConfigureIncompleteMarten(StoreOptions options)
    {
        options.Events.AddEventType<OfferQuoted>();
    }

    private static IServiceCollection BuildModuleServices(
        IConfiguration configuration,
        string? environmentName = null
    )
    {
        var builder = new HostApplicationBuilder(
            new HostApplicationBuilderSettings
            {
                DisableDefaults = true,
                EnvironmentName = environmentName ?? Environments.Development,
                ApplicationName = "Travel.Modules.Flights.Tests.Unit",
            }
        );
        builder.Configuration.AddConfiguration(configuration);
        builder.AddFlightsModule();
        return builder.Services;
    }
}
