using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;
using StackExchange.Redis;
using Travel.Modules.Flights.Application;
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
using Travel.Modules.Flights.Infrastructure.HealthChecks;
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
        services.Configure<FlightsFeatureFlags>(
            configuration.GetSection(FlightsFeatureFlags.SectionName)
        );
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

        // ── External HTTP clients ─────────────────────────────────────────────────
        //
        // Per spec §19 each client gets its own named resilience pipeline rather than the
        // global AddStandardResilienceHandler so timeouts are tuned per-provider:
        //
        //   DuffelClient       — 10 s timeout (orders are the long path; DuffelFlightSearchProvider
        //                        imposes a separate 4 s per-call CancellationToken for search).
        //   TravelpayoutsClient — 4 s (read-only price feed, should be fast).
        //   FrankfurterClient   — 2 s (simple exchange-rate lookup).
        //
        // Each client calls .ReplaceGlobalResilience() before .AddResilienceHandler — see the
        // private helper at the bottom of this class for the full suppression rationale.

        // Strip global resilience and install the Duffel-tuned pipeline.
        services
            .AddHttpClient<DuffelClient>()
            .ReplaceGlobalResilience()
            .AddResilienceHandler(
                "duffel",
                (pipeline, ctx) =>
                {
                    pipeline.AddRetry(
                        new HttpRetryStrategyOptions
                        {
                            MaxRetryAttempts = 3,
                            UseJitter = true,
                            Delay = TimeSpan.FromMilliseconds(50),
                            MaxDelay = TimeSpan.FromMilliseconds(500),
                            BackoffType = DelayBackoffType.Exponential,
                        }
                    );
                    pipeline.AddCircuitBreaker(
                        new HttpCircuitBreakerStrategyOptions
                        {
                            MinimumThroughput = 5,
                            SamplingDuration = TimeSpan.FromSeconds(30),
                            BreakDuration = TimeSpan.FromSeconds(30),
                        }
                    );
                    pipeline.AddTimeout(
                        TimeSpan.FromSeconds(
                            ctx.ServiceProvider.GetRequiredService<
                                IOptions<DuffelOptions>
                            >().Value.TimeoutSeconds
                        )
                    );
                }
            );

        // Strip global resilience and install the Travelpayouts-tuned pipeline.
        services
            .AddHttpClient<TravelpayoutsClient>()
            .ReplaceGlobalResilience()
            .AddResilienceHandler(
                "travelpayouts",
                (pipeline, ctx) =>
                {
                    pipeline.AddRetry(
                        new HttpRetryStrategyOptions
                        {
                            MaxRetryAttempts = 3,
                            UseJitter = true,
                            Delay = TimeSpan.FromMilliseconds(50),
                            MaxDelay = TimeSpan.FromMilliseconds(200),
                            BackoffType = DelayBackoffType.Exponential,
                        }
                    );
                    pipeline.AddCircuitBreaker(
                        new HttpCircuitBreakerStrategyOptions
                        {
                            MinimumThroughput = 5,
                            SamplingDuration = TimeSpan.FromSeconds(30),
                            BreakDuration = TimeSpan.FromSeconds(30),
                        }
                    );
                    pipeline.AddTimeout(
                        TimeSpan.FromSeconds(
                            ctx.ServiceProvider.GetRequiredService<
                                IOptions<TravelpayoutsOptions>
                            >().Value.TimeoutSeconds
                        )
                    );
                }
            );

        // Strip global resilience and install the Frankfurter-tuned pipeline.
        // Public FX service; 2 s timeout is sufficient; values are cached daily, retries rarely repeat.
        // Circuit breaker intentionally omitted: Frankfurter is a public free FX service with
        // 24 h-cached values; a CB adds no protection worth the complexity.
        services
            .AddHttpClient<FrankfurterClient>(c =>
                c.BaseAddress = new Uri("https://api.frankfurter.app/")
            )
            .ReplaceGlobalResilience()
            .AddResilienceHandler(
                "frankfurter",
                pipeline =>
                {
                    pipeline.AddRetry(
                        new HttpRetryStrategyOptions
                        {
                            MaxRetryAttempts = 2,
                            UseJitter = true,
                            Delay = TimeSpan.FromMilliseconds(50),
                            MaxDelay = TimeSpan.FromMilliseconds(200),
                            BackoffType = DelayBackoffType.Exponential,
                        }
                    );
                    // spec §19: 2 s for the exchange-rate lookup
                    pipeline.AddTimeout(TimeSpan.FromSeconds(2));
                }
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

        // ── Healthchecks ─────────────────────────────────────────────────────────
        services
            .AddHealthChecks()
            .AddCheck<DuffelHealthCheck>("duffel", tags: ["ready"])
            .AddCheck<TravelpayoutsHealthCheck>("travelpayouts", tags: ["ready"]);

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

    // `RemoveAllResilienceHandlers` is marked experimental (EXTEXP0001) in
    // Microsoft.Extensions.Http.Resilience 10.5.0. It IS the documented opt-out
    // for the global standard handler that Travel.ServiceDefaults installs via
    // ConfigureHttpClientDefaults — without this call, Polly STACKS the global
    // + per-client pipelines (3×3 = 9 effective Duffel retries).
#pragma warning disable EXTEXP0001
    private static IHttpClientBuilder ReplaceGlobalResilience(this IHttpClientBuilder b) =>
        b.RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001
}
