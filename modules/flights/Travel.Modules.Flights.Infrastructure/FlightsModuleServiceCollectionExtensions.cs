using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StackExchange.Redis;
using Travel.Modules.Flights.Application.Handlers.Booking;
using Travel.Modules.Flights.Application.Idempotency;
using Travel.Modules.Flights.Application.Notifications;
using Travel.Modules.Flights.Application.Observability;
using Travel.Modules.Flights.Application.Queries;
using Travel.Modules.Flights.Application.Search;
using Travel.Modules.Flights.Application.Webhooks;
using Travel.Modules.Flights.Core.Providers;
using Travel.Modules.Flights.Infrastructure.Cache;
using Travel.Modules.Flights.Infrastructure.ExternalServices;
using Travel.Modules.Flights.Infrastructure.Notifications;
using Travel.Modules.Flights.Infrastructure.Notifications.Email;
using Travel.Modules.Flights.Infrastructure.Notifications.Keycloak;
using Travel.Modules.Flights.Infrastructure.Notifications.Sse;
using Travel.Modules.Flights.Infrastructure.Observability;
using Travel.Modules.Flights.Infrastructure.Payments;
using Travel.Modules.Flights.Infrastructure.Persistence;
using Travel.Modules.Flights.Infrastructure.Persistence.Repositories;
using Travel.Modules.Flights.Infrastructure.Providers.Duffel;
using Travel.Modules.Flights.Infrastructure.Providers.Travelpayouts;

namespace Travel.Modules.Flights.Infrastructure;

/// <summary>
/// Composition root for the Flights module: registers every service the Wolverine handlers
/// and HTTP endpoints depend on — provider adapters, caches, persistence-backed stores,
/// notifications and the Keycloak admin integration.
/// <para>
/// The host remains responsible for the Aspire-integrated bits that need the
/// <c>IHostApplicationBuilder</c> — the <c>FlightsDbContext</c>, Marten, the
/// <c>TimeProvider</c> singleton and Wolverine handler/endpoint assembly discovery.
/// </para>
/// </summary>
public static class FlightsModuleServiceCollectionExtensions
{
    public static IServiceCollection AddFlightsModule(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment
    )
    {
        // ── Options ──────────────────────────────────────────────────────────────
        services.Configure<DuffelOptions>(configuration.GetSection(DuffelOptions.SectionName));
        services.Configure<TravelpayoutsOptions>(
            configuration.GetSection(TravelpayoutsOptions.SectionName)
        );
        services.Configure<KeycloakAdminOptions>(
            configuration.GetSection(KeycloakAdminOptions.SectionName)
        );

        // ── Observability ────────────────────────────────────────────────────────
        // Single FlightsMetrics instance behind both metric interfaces.
        services.AddSingleton<FlightsMetrics>();
        services.AddSingleton<ISearchMetrics>(sp => sp.GetRequiredService<FlightsMetrics>());
        services.AddSingleton<IFlightsMetrics>(sp => sp.GetRequiredService<FlightsMetrics>());

        // ── Redis (search + deeplink caches) ─────────────────────────────────────
        // AbortOnConnectFail=false keeps host start-up resilient — Redis is a cache, not a
        // boot-critical dependency; cache calls degrade gracefully if it is unavailable.
        var redisConnString = configuration.GetConnectionString("redis") ?? "localhost:6379";
        services.AddSingleton<IConnectionMultiplexer>(_ =>
        {
            var options = ConfigurationOptions.Parse(redisConnString);
            options.AbortOnConnectFail = false;
            return ConnectionMultiplexer.Connect(options);
        });

        // ── External HTTP clients ────────────────────────────────────────────────
        services.AddHttpClient<DuffelClient>();
        services.AddHttpClient<TravelpayoutsClient>();
        services.AddHttpClient<FrankfurterClient>(c =>
            c.BaseAddress = new Uri("https://api.frankfurter.app/")
        );

        // ── Provider adapters (capability-segregated) ────────────────────────────
        services.AddSingleton<TravelpayoutsDeeplinkBuilder>();
        services.AddSingleton<DuffelWebhookVerifier>();

        services.AddScoped<IFlightSearchProvider, DuffelFlightSearchProvider>();
        services.AddScoped<IFlightSearchProvider, TravelpayoutsSearchProvider>();
        services.AddScoped<IFlightBookingProvider, DuffelFlightBookingProvider>();

        // M1 ships only the [TestOnly] Duffel test wallet — never register it in Production
        // (TestOnlyGuard would throw at start-up). A real gateway is a later subproject.
        if (!environment.IsProduction())
            services.AddSingleton<IPaymentGateway, DuffelTestWalletPaymentGateway>();

        // ── Search caches & FX ───────────────────────────────────────────────────
        services.AddScoped<ISearchCache, SearchCacheRedis>();
        services.AddScoped<IDeeplinkOfferCache, DeeplinkOfferCacheRepository>();
        services.AddScoped<IFxRates, FrankfurterRatesCache>();

        // ── Persistence-backed stores & projections ──────────────────────────────
        services.AddScoped<IIdempotencyStore, IdempotencyStore>();
        services.AddScoped<IWebhookInboxStore, WebhookInboxStore>();
        services.AddScoped<IOrderReadModelQueries, OrderReadModelQueries>();
        services.AddScoped<IOrderReadModelProjector, OrderReadModelProjectorImpl>();

        // ── Notifications ────────────────────────────────────────────────────────
        services.AddSingleton<IEmailRenderer, HtmlTemplateEmailRenderer>();
        services.AddScoped<IEmailSender, MailKitEmailSender>();
        services.AddSingleton<IOrderSseRegistry, OrderSseConnectionRegistry>();

        // ── Keycloak admin user directory ────────────────────────────────────────
        // Degrades gracefully to synthetic profiles when Flights:Keycloak is not configured.
        services.AddSingleton<IKeycloakAdminTokenProvider, KeycloakAdminTokenProvider>();
        services.AddTransient<KeycloakAdminAuthHandler>();
        services.AddHttpClient(KeycloakAdminTokenProvider.HttpClientName);
        services
            .AddHttpClient(KeycloakAdminAuthHandler.HttpClientName)
            .AddHttpMessageHandler<KeycloakAdminAuthHandler>();
        services.AddScoped<IUserDirectory, KeycloakUserDirectory>();

        return services;
    }
}
