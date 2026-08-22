using System.Net;
using System.Net.Mail;
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
using Travel.Modules.Flights.Infrastructure.Persistence.Initialization;
using Travel.Modules.Flights.Infrastructure.Persistence.Repositories;
using Travel.Modules.Flights.Infrastructure.Providers.Duffel;
using Travel.Modules.Flights.Infrastructure.Providers.Travelpayouts;
using Travel.Modules.Flights.Infrastructure.Webhooks;
using Travel.Shared.Infrastructure.Initialization;
using Travel.Shared.Infrastructure.Telemetry;

namespace Travel.Modules.Flights.Infrastructure;

/// <summary>
/// Composition root for the Flights module: registers every service the Wolverine handlers
/// and HTTP endpoints depend on — provider adapters, caches, persistence-backed stores,
/// notifications and the Keycloak admin integration.
/// <para>
/// The public Flights Api facade owns process-facing module composition.
/// </para>
/// </summary>
internal static class FlightsInfrastructureServiceCollectionExtensions
{
    private static readonly Func<
        Polly.Retry.RetryPredicateArguments<HttpResponseMessage>,
        ValueTask<bool>
    > DefaultTransientHttpRetryPredicate = new HttpRetryStrategyOptions().ShouldHandle;

    internal static IServiceCollection AddFlightsInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment
    )
    {
        // ── Options ──────────────────────────────────────────────────────────────
        services
            .AddOptions<FlightsFeatureFlags>()
            .Bind(configuration.GetSection(FlightsFeatureFlags.SectionName))
            .ValidateOnStart();

        services
            .AddOptions<DuffelOptions>()
            .Bind(configuration.GetSection(DuffelOptions.SectionName))
            .Validate(
                options => IsHttpEndpoint(options.BaseUrl),
                "Duffel BaseUrl must be an absolute HTTP or HTTPS URI."
            )
            .Validate(
                options => options.TimeoutSeconds > 0 && options.SearchTimeoutSeconds > 0,
                "Duffel timeouts must be positive."
            )
            .Validate(
                options =>
                    !environment.IsProduction()
                    || (
                        !string.IsNullOrWhiteSpace(options.ApiKey)
                        && !string.IsNullOrWhiteSpace(options.WebhookSecret)
                    ),
                "Production Duffel API key and webhook secret are required."
            )
            .Validate(
                options =>
                    !environment.IsProduction() || IsHttpsNonLoopbackEndpoint(options.BaseUrl),
                "Production Duffel BaseUrl must use HTTPS and must not be loopback."
            )
            .ValidateOnStart();

        var travelpayoutsEnabled = configuration.GetValue(
            $"{FlightsFeatureFlags.SectionName}:Travelpayouts:Enabled",
            true
        );
        if (travelpayoutsEnabled)
        {
            services
                .AddOptions<TravelpayoutsOptions>()
                .Bind(configuration.GetSection(TravelpayoutsOptions.SectionName))
                .Validate(
                    options =>
                        !string.IsNullOrWhiteSpace(options.ApiToken)
                        && !string.IsNullOrWhiteSpace(options.PartnerMarker),
                    "Enabled Travelpayouts token and partner marker are required."
                )
                .Validate(
                    options => IsHttpsNonLoopbackEndpoint(options.BaseUrl),
                    "Enabled Travelpayouts BaseUrl must use HTTPS and must not be loopback."
                )
                .Validate(
                    options => options.TimeoutSeconds > 0,
                    "Travelpayouts timeout must be positive."
                )
                .ValidateOnStart();
        }

        services
            .AddOptions<SmtpOptions>()
            .Bind(configuration.GetSection(SmtpOptions.SectionName))
            .Validate(
                options =>
                    IsSmtpAbsent(options)
                    || (
                        !string.IsNullOrWhiteSpace(options.Host)
                        && options.Port is > 0 and <= 65535
                        && IsValidEmailAddress(options.FromAddress)
                    ),
                "SMTP host, port, and from address must be valid when configured."
            )
            .Validate(
                options =>
                    !environment.IsProduction()
                    || (!IsSmtpAbsent(options) && !IsLoopbackHost(options.Host)),
                "Production SMTP configuration is required and host must not be loopback."
            )
            .ValidateOnStart();

        services
            .AddOptions<KeycloakAdminOptions>()
            .Bind(configuration.GetSection(KeycloakAdminOptions.SectionName))
            .Validate(
                options => IsKeycloakAdminAbsent(options) || IsKeycloakAdminComplete(options),
                "Keycloak admin configuration must be either completely absent or complete."
            )
            .Validate(
                options => !options.IsConfigured || options.TimeoutSeconds > 0,
                "Configured Keycloak admin timeout must be positive."
            )
            .ValidateOnStart();

        services
            .AddOptions<FrankfurterOptions>()
            .Bind(configuration.GetSection(FrankfurterOptions.SectionName))
            .Validate(
                options => IsHttpEndpoint(options.BaseAddress),
                "Frankfurter BaseAddress must be an absolute HTTP or HTTPS URI."
            )
            .Validate(
                options => options.TimeoutSeconds > 0,
                "Frankfurter timeout must be positive."
            )
            .ValidateOnStart();

        // ── Observability ────────────────────────────────────────────────────────
        services.AddSingleton<IHttpUrlRedactionContributor, TravelpayoutsUrlRedactionContributor>();
        // Single FlightsMetrics instance behind both metric interfaces.
        services.AddSingleton<FlightsMetrics>();
        services.AddSingleton<ISearchMetrics>(sp => sp.GetRequiredService<FlightsMetrics>());
        services.AddSingleton<IFlightsMetrics>(sp => sp.GetRequiredService<FlightsMetrics>());

        // ── Redis (search + deeplink caches) ─────────────────────────────────────
        // AbortOnConnectFail=false keeps host start-up resilient — Redis is a cache, not a
        // boot-critical dependency; cache calls degrade gracefully if it is unavailable.
        var redisConnString = configuration.GetConnectionString("redis") ?? string.Empty;
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

        // Install the Duffel-tuned pipeline.
        services
            .AddHttpClient<DuffelClient>()
            .AddResilienceHandler(
                "duffel",
                (pipeline, ctx) =>
                {
                    pipeline.AddTimeout(
                        TimeSpan.FromSeconds(
                            ctx.ServiceProvider.GetRequiredService<
                                IOptions<DuffelOptions>
                            >().Value.TimeoutSeconds
                        )
                    );
                    pipeline.AddRetry(
                        new HttpRetryStrategyOptions
                        {
                            MaxRetryAttempts = 3,
                            UseJitter = true,
                            Delay = TimeSpan.FromMilliseconds(50),
                            MaxDelay = TimeSpan.FromMilliseconds(500),
                            BackoffType = DelayBackoffType.Exponential,
                            ShouldHandle = ShouldRetryProviderRequest,
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
                }
            );

        // Install the Travelpayouts-tuned pipeline.
        if (travelpayoutsEnabled)
        {
            services
                .AddHttpClient<TravelpayoutsClient>()
                .AddResilienceHandler(
                    "travelpayouts",
                    (pipeline, ctx) =>
                    {
                        pipeline.AddTimeout(
                            TimeSpan.FromSeconds(
                                ctx.ServiceProvider.GetRequiredService<
                                    IOptions<TravelpayoutsOptions>
                                >().Value.TimeoutSeconds
                            )
                        );
                        pipeline.AddRetry(
                            new HttpRetryStrategyOptions
                            {
                                MaxRetryAttempts = 3,
                                UseJitter = true,
                                Delay = TimeSpan.FromMilliseconds(50),
                                MaxDelay = TimeSpan.FromMilliseconds(200),
                                BackoffType = DelayBackoffType.Exponential,
                                ShouldHandle = ShouldRetryProviderRequest,
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
                    }
                );
        }

        // Install the Frankfurter-tuned pipeline.
        // Public FX service; 2 s timeout is sufficient; values are cached daily, retries rarely repeat.
        // Circuit breaker intentionally omitted: Frankfurter is a public free FX service with
        // 24 h-cached values; a CB adds no protection worth the complexity.
        //
        // Base address is read from Flights:Providers:Frankfurter:BaseAddress so it can be
        // overridden per-environment or pointed at a WireMock stub in tests.
        // The class-initialiser default ("https://api.frankfurter.app/") is the defense-in-depth
        // fallback when the config key is absent.
        services
            .AddHttpClient<FrankfurterClient>(
                (sp, c) =>
                {
                    var opts = sp.GetRequiredService<IOptions<FrankfurterOptions>>().Value;
                    c.BaseAddress = new Uri(opts.BaseAddress);
                }
            )
            .AddResilienceHandler(
                "frankfurter",
                (pipeline, ctx) =>
                {
                    // The configured timeout is the total exchange-rate call budget.
                    pipeline.AddTimeout(
                        TimeSpan.FromSeconds(
                            ctx.ServiceProvider.GetRequiredService<
                                IOptions<FrankfurterOptions>
                            >().Value.TimeoutSeconds
                        )
                    );
                    pipeline.AddRetry(
                        new HttpRetryStrategyOptions
                        {
                            MaxRetryAttempts = 2,
                            UseJitter = true,
                            Delay = TimeSpan.FromMilliseconds(50),
                            MaxDelay = TimeSpan.FromMilliseconds(200),
                            BackoffType = DelayBackoffType.Exponential,
                            ShouldHandle = ShouldRetryProviderRequest,
                        }
                    );
                }
            );

        // ── Provider adapters (capability-segregated) ────────────────────────────
        services.AddSingleton<DuffelWebhookVerifier>();
        services.AddScoped<IWebhookIngestionPort, DuffelWebhookIngestionPort>();
        services.AddScoped<IWebhookIngestionService, WebhookIngestionService>();

        if (travelpayoutsEnabled)
            services.AddSingleton<TravelpayoutsDeeplinkBuilder>();

        services.AddScoped<IFlightSearchProvider, DuffelFlightSearchProvider>();
        if (travelpayoutsEnabled)
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
        services.AddInitializer<FlightsEfInitializer>();
        services.AddInitializer<FlightsMartenInitializer>();

        // ── Notifications ────────────────────────────────────────────────────────
        services.AddSingleton<IEmailRenderer, HtmlTemplateEmailRenderer>();
        services.AddScoped<IEmailSender, MailKitEmailSender>();
        services.AddSingleton<IOrderSseRegistry, OrderSseConnectionRegistry>();

        // ── Probe-only HTTP clients for healthchecks ──────────────────────────────
        //
        // These named clients deliberately bypass the production resilience pipeline
        // (retries + 30 s circuit breaker installed on the typed DuffelClient /
        // TravelpayoutsClient). Without this isolation a /health/dependencies probe would hang
        // for the full break-duration whenever the breaker is open, and probe traffic
        // would not help the breaker close (MinimumThroughput = 5 is for business calls).
        //
        // These named clients own no retry or circuit-breaker policy. Their explicit
        // HttpClient timeouts keep each dependency probe to one bounded attempt.
        services.AddHttpClient(
            "duffel-health",
            (sp, c) =>
            {
                var opts = sp.GetRequiredService<IOptions<DuffelOptions>>().Value;
                c.BaseAddress = new Uri(opts.BaseUrl);
                c.Timeout = TimeSpan.FromSeconds(5);
            }
        );

        if (travelpayoutsEnabled)
        {
            services.AddHttpClient(
                "travelpayouts-health",
                (sp, c) =>
                {
                    var opts = sp.GetRequiredService<IOptions<TravelpayoutsOptions>>().Value;
                    c.BaseAddress = new Uri(opts.BaseUrl);
                    c.Timeout = TimeSpan.FromSeconds(3);
                }
            );
        }

        // ── Healthchecks ─────────────────────────────────────────────────────────
        services.AddHealthChecks().AddCheck<DuffelHealthCheck>("duffel", tags: ["dependency"]);
        if (travelpayoutsEnabled)
        {
            services
                .AddHealthChecks()
                .AddCheck<TravelpayoutsHealthCheck>("travelpayouts", tags: ["dependency"]);
        }

        // ── Keycloak admin user directory ────────────────────────────────────────
        // Degrades gracefully to synthetic profiles when Flights:Keycloak is not configured.
        services.AddSingleton<IKeycloakAdminTokenProvider, KeycloakAdminTokenProvider>();
        services.AddTransient<KeycloakAdminAuthHandler>();
        AddKeycloakAdminResilience(
            services.AddHttpClient(KeycloakAdminTokenProvider.HttpClientName),
            "keycloak-admin-token"
        );
        AddKeycloakAdminResilience(
            services
                .AddHttpClient(KeycloakAdminAuthHandler.HttpClientName)
                .AddHttpMessageHandler<KeycloakAdminAuthHandler>(),
            "keycloak-admin"
        );
        services.AddScoped<IUserDirectory, KeycloakUserDirectory>();

        return services;
    }

    private static void AddKeycloakAdminResilience(IHttpClientBuilder client, string pipelineName)
    {
        client.AddResilienceHandler(
            pipelineName,
            (pipeline, ctx) =>
            {
                pipeline.AddTimeout(
                    TimeSpan.FromSeconds(
                        ctx.ServiceProvider.GetRequiredService<
                            IOptions<KeycloakAdminOptions>
                        >().Value.TimeoutSeconds
                    )
                );
                pipeline.AddRetry(
                    new HttpRetryStrategyOptions
                    {
                        MaxRetryAttempts = 3,
                        UseJitter = true,
                        Delay = TimeSpan.FromMilliseconds(50),
                        MaxDelay = TimeSpan.FromMilliseconds(200),
                        BackoffType = DelayBackoffType.Exponential,
                        ShouldHandle = ShouldRetryProviderRequest,
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
            }
        );
    }

    private static ValueTask<bool> ShouldRetryProviderRequest(
        Polly.Retry.RetryPredicateArguments<HttpResponseMessage> arguments
    )
    {
        var request = arguments.Context.GetRequestMessage();
        if (!IsRetryableProviderRequest(request))
            return PredicateResult.False();

        return DefaultTransientHttpRetryPredicate(arguments);
    }

    private static bool IsRetryableProviderRequest(HttpRequestMessage? request)
    {
        if (request?.Method is null)
            return false;

        if (request.Method == HttpMethod.Get || request.Method == HttpMethod.Head)
            return true;

        if (request.Method != HttpMethod.Post)
            return false;

        if (!request.Headers.TryGetValues("Idempotency-Key", out var values))
            return false;

        var keys = values.ToArray();
        return keys.Length == 1 && !string.IsNullOrWhiteSpace(keys[0]);
    }

    private static bool IsHttpEndpoint(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private static bool IsHttpsNonLoopbackEndpoint(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && !IsLoopbackHost(uri.Host);

    private static bool IsSmtpAbsent(SmtpOptions options) =>
        string.IsNullOrWhiteSpace(options.Host)
        && options.Port == 0
        && string.IsNullOrWhiteSpace(options.FromAddress);

    private static bool IsValidEmailAddress(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        try
        {
            return new MailAddress(value).Address == value;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool IsLoopbackHost(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var normalizedHost = value.Trim().TrimEnd('.');
        return string.Equals(normalizedHost, "localhost", StringComparison.OrdinalIgnoreCase)
            || (
                IPAddress.TryParse(normalizedHost, out var address) && IPAddress.IsLoopback(address)
            );
    }

    private static bool IsKeycloakAdminAbsent(KeycloakAdminOptions options) =>
        string.IsNullOrWhiteSpace(options.AdminBaseUrl)
        && string.IsNullOrWhiteSpace(options.ClientId)
        && string.IsNullOrWhiteSpace(options.ClientSecret);

    private static bool IsKeycloakAdminComplete(KeycloakAdminOptions options) =>
        IsHttpEndpoint(options.AdminBaseUrl)
        && !string.IsNullOrWhiteSpace(options.Realm)
        && !string.IsNullOrWhiteSpace(options.ClientId)
        && !string.IsNullOrWhiteSpace(options.ClientSecret);
}
