using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Shouldly;
using Travel.Modules.Flights.Api.Composition;
using Travel.Modules.Flights.Application.Notifications;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Infrastructure.ExternalServices;
using Travel.Modules.Flights.Infrastructure.Notifications.Keycloak;
using Travel.Modules.Flights.Infrastructure.Providers.Duffel;
using Travel.Modules.Flights.Infrastructure.Providers.Travelpayouts;
using Xunit;

namespace Travel.Host.Tests.Integration.Flights;

[Trait("Category", "Integration")]
public sealed class FlightsHttpClientCompositionTests
{
    private static readonly Uri RequestUri = new("https://provider.example/test");

    public static TheoryData<string> TotalBudgetPipelines =>
        new() { "Duffel", "Travelpayouts", "Frankfurter", "KeycloakToken", "KeycloakAdmin" };

    [Fact]
    public async Task Provider_clients_apply_only_their_owned_retry_policies()
    {
        var duffel = new CountingHandler(HttpStatusCode.InternalServerError);
        var travelpayouts = new CountingHandler(HttpStatusCode.InternalServerError);
        var frankfurter = new CountingHandler(HttpStatusCode.InternalServerError);
        using var services = BuildConfiguredHostGraph(
            duffel: duffel,
            travelpayouts: travelpayouts,
            frankfurter: frankfurter
        );
        var ct = TestContext.Current.CancellationToken;

        using var duffelResponse = await services
            .GetRequiredService<DuffelClient>()
            .GetAsync("/test", ct);
        using var travelpayoutsResponse = await services
            .GetRequiredService<TravelpayoutsClient>()
            .GetAsync("/test", ct);
        var frankfurterResult = await services
            .GetRequiredService<FrankfurterClient>()
            .GetRateAsync(CurrencyCode.Create("USD").Value, CurrencyCode.Create("EUR").Value, ct);

        duffelResponse.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        travelpayoutsResponse.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        frankfurterResult.IsError.ShouldBeTrue();
        duffel.AttemptCount.ShouldBe(4);
        travelpayouts.AttemptCount.ShouldBe(4);
        frankfurter.AttemptCount.ShouldBe(3);
    }

    [Fact]
    public async Task Duffel_post_without_stable_provider_idempotency_key_makes_one_attempt()
    {
        var duffel = new CountingHandler(HttpStatusCode.InternalServerError);
        using var services = BuildConfiguredHostGraph(duffel: duffel);

        using var response = await services
            .GetRequiredService<DuffelClient>()
            .PostAsync("/air/orders", new { type = "hold" }, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        duffel.AttemptCount.ShouldBe(1);
    }

    [Fact]
    public async Task Duffel_post_with_stable_provider_idempotency_key_retries_transient_failure()
    {
        var duffel = new CountingHandler(HttpStatusCode.InternalServerError);
        using var services = BuildConfiguredHostGraph(duffel: duffel);

        using var response = await services
            .GetRequiredService<DuffelClient>()
            .PostAsync(
                "/air/orders/order_test/payments",
                new
                {
                    type = "balance",
                    amount = "10.00",
                    currency = "USD",
                },
                new Dictionary<string, string>
                {
                    ["Idempotency-Key"] = "11111111-1111-1111-1111-111111111111",
                },
                TestContext.Current.CancellationToken
            );

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        duffel.AttemptCount.ShouldBe(4);
    }

    [Theory]
    [MemberData(nameof(TotalBudgetPipelines))]
    public async Task Configured_provider_timeout_is_outer_total_pipeline_budget(string pipeline)
    {
        var handler = new NeverCompletingHandler();
        using var services = pipeline switch
        {
            "Duffel" => BuildConfiguredHostGraph(duffel: handler, timeoutSeconds: 1),
            "Travelpayouts" => BuildConfiguredHostGraph(travelpayouts: handler, timeoutSeconds: 1),
            "Frankfurter" => BuildConfiguredHostGraph(frankfurter: handler, timeoutSeconds: 1),
            "KeycloakToken" => BuildConfiguredHostGraph(keycloakToken: handler, timeoutSeconds: 1),
            "KeycloakAdmin" => BuildConfiguredHostGraph(
                keycloakToken: new CountingHandler(
                    (request, _) =>
                        new HttpResponseMessage(HttpStatusCode.OK)
                        {
                            RequestMessage = request,
                            Content = new StringContent(
                                """{"access_token":"registered-token","expires_in":300}""",
                                Encoding.UTF8,
                                "application/json"
                            ),
                        }
                ),
                keycloakAdmin: handler,
                timeoutSeconds: 1
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(pipeline), pipeline, null),
        };
        using var callCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken
        );
        var operation = InvokeProviderPipelineAsync(pipeline, services, callCancellation.Token);

        var completed = await Task.WhenAny(
            operation,
            Task.Delay(TimeSpan.FromMilliseconds(1800), TestContext.Current.CancellationToken)
        );
        if (completed != operation)
            await callCancellation.CancelAsync();

        try
        {
            await operation;
        }
        catch (Exception) when (completed == operation)
        {
            // Provider clients surface their configured Polly timeout differently:
            // raw HTTP clients throw, while Frankfurter translates it to ErrorOr.
        }

        completed.ShouldBeSameAs(
            operation,
            $"{pipeline} must finish within one configured total timeout budget, not one timeout per retry attempt."
        );
        handler.AttemptCount.ShouldBe(1);
    }

    [Fact]
    public async Task Admin_client_does_not_multiply_exhausted_token_pipeline()
    {
        var token = new CountingHandler(HttpStatusCode.InternalServerError);
        var admin = new CountingHandler(HttpStatusCode.InternalServerError);
        using var services = BuildConfiguredHostGraph(keycloakToken: token, keycloakAdmin: admin);
        var factory = services.GetRequiredService<IHttpClientFactory>();
        var ct = TestContext.Current.CancellationToken;

        await Should.ThrowAsync<Exception>(async () =>
            await factory
                .CreateClient(KeycloakAdminAuthHandler.HttpClientName)
                .GetAsync(RequestUri, ct)
        );

        token.AttemptCount.ShouldBe(1);
        admin.AttemptCount.ShouldBe(0);
    }

    [Fact]
    public async Task Admin_client_retries_primary_request_without_reacquiring_successful_token()
    {
        var token = new CountingHandler(
            (request, _) =>
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    RequestMessage = request,
                    Content = new StringContent(
                        """{"access_token":"registered-token","expires_in":300}""",
                        Encoding.UTF8,
                        "application/json"
                    ),
                }
        );
        var admin = new CountingHandler(HttpStatusCode.InternalServerError);
        using var services = BuildConfiguredHostGraph(keycloakToken: token, keycloakAdmin: admin);
        var factory = services.GetRequiredService<IHttpClientFactory>();

        using var response = await factory
            .CreateClient(KeycloakAdminAuthHandler.HttpClientName)
            .GetAsync(RequestUri, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        token.AttemptCount.ShouldBe(1);
        admin.AttemptCount.ShouldBe(4);
    }

    [Fact]
    public async Task Health_clients_make_one_bounded_attempt()
    {
        var duffelHealth = new CountingHandler(HttpStatusCode.ServiceUnavailable);
        var travelpayoutsHealth = new CountingHandler(HttpStatusCode.ServiceUnavailable);
        using var services = BuildConfiguredHostGraph(
            duffelHealth: duffelHealth,
            travelpayoutsHealth: travelpayoutsHealth
        );
        var factory = services.GetRequiredService<IHttpClientFactory>();
        var ct = TestContext.Current.CancellationToken;

        using var duffelResponse = await factory.CreateClient("duffel-health").GetAsync("/", ct);
        using var travelpayoutsResponse = await factory
            .CreateClient("travelpayouts-health")
            .GetAsync("/", ct);

        duffelResponse.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        travelpayoutsResponse.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        duffelHealth.AttemptCount.ShouldBe(1);
        travelpayoutsHealth.AttemptCount.ShouldBe(1);
    }

    [Fact]
    public async Task Absent_keycloak_configuration_validates_and_uses_fallback_without_http()
    {
        var token = new CountingHandler(HttpStatusCode.InternalServerError);
        var admin = new CountingHandler(HttpStatusCode.InternalServerError);
        using var services = BuildConfiguredHostGraph(
            configureKeycloak: false,
            keycloakToken: token,
            keycloakAdmin: admin
        );

        Should.NotThrow(() => services.GetRequiredService<IStartupValidator>().Validate());
        var userId = Guid.Parse("fb23d837-2952-49b2-8b92-3fe190d4065b");
        var profile = await services
            .GetRequiredService<IUserDirectory>()
            .GetAsync(userId, TestContext.Current.CancellationToken);

        profile.ShouldNotBeNull();
        profile.Email.ShouldBe("fb23d837295249b28b923fe190d4065b@example.test");
        token.AttemptCount.ShouldBe(0);
        admin.AttemptCount.ShouldBe(0);
    }

    private static ServiceProvider BuildConfiguredHostGraph(
        HttpMessageHandler? duffel = null,
        HttpMessageHandler? travelpayouts = null,
        HttpMessageHandler? frankfurter = null,
        HttpMessageHandler? keycloakToken = null,
        HttpMessageHandler? keycloakAdmin = null,
        HttpMessageHandler? duffelHealth = null,
        HttpMessageHandler? travelpayoutsHealth = null,
        bool configureKeycloak = true,
        int timeoutSeconds = 30
    )
    {
        var builder = new HostApplicationBuilder(
            new HostApplicationBuilderSettings
            {
                DisableDefaults = true,
                EnvironmentName = Environments.Development,
                ApplicationName = "Travel.Host.Tests.Integration",
            }
        );
        builder.Configuration.AddInMemoryCollection(
            ConfigurationValues(configureKeycloak, timeoutSeconds)
        );
        builder.AddServiceDefaults();
        builder.AddFlightsModule();

        ConfigurePrimary(builder.Services.AddHttpClient<DuffelClient>(), duffel);
        ConfigurePrimary(builder.Services.AddHttpClient<TravelpayoutsClient>(), travelpayouts);
        ConfigurePrimary(builder.Services.AddHttpClient<FrankfurterClient>(), frankfurter);
        ConfigurePrimary(
            builder.Services.AddHttpClient(KeycloakAdminTokenProvider.HttpClientName),
            keycloakToken
        );
        ConfigurePrimary(
            builder.Services.AddHttpClient(KeycloakAdminAuthHandler.HttpClientName),
            keycloakAdmin
        );
        ConfigurePrimary(builder.Services.AddHttpClient("duffel-health"), duffelHealth);
        ConfigurePrimary(
            builder.Services.AddHttpClient("travelpayouts-health"),
            travelpayoutsHealth
        );

        return builder.Services.BuildServiceProvider();
    }

    private static void ConfigurePrimary(IHttpClientBuilder builder, HttpMessageHandler? handler)
    {
        if (handler is not null)
            builder.ConfigurePrimaryHttpMessageHandler(() => handler);
    }

    private static Dictionary<string, string?> ConfigurationValues(
        bool configureKeycloak,
        int timeoutSeconds
    )
    {
        var values = new Dictionary<string, string?>
        {
            ["OTEL_EXPORTER_OTLP_ENDPOINT"] = string.Empty,
            ["ConnectionStrings:travel"] =
                "Host=localhost;Database=travel;Username=test;Password=test",
            ["ConnectionStrings:redis"] = "localhost:6379",
            ["Flights:FeatureFlags:Travelpayouts:Enabled"] = "true",
            ["Flights:Duffel:BaseUrl"] = "https://duffel.example",
            ["Flights:Duffel:ApiKey"] = "duffel-test-key",
            ["Flights:Duffel:WebhookSecret"] = "duffel-test-webhook-secret",
            ["Flights:Duffel:TimeoutSeconds"] = timeoutSeconds.ToString(),
            ["Flights:Duffel:SearchTimeoutSeconds"] = timeoutSeconds.ToString(),
            ["Flights:Travelpayouts:BaseUrl"] = "https://travelpayouts.example",
            ["Flights:Travelpayouts:ApiToken"] = "travelpayouts-test-token",
            ["Flights:Travelpayouts:PartnerMarker"] = "travelpayouts-test-marker",
            ["Flights:Travelpayouts:TimeoutSeconds"] = timeoutSeconds.ToString(),
            ["Flights:Providers:Frankfurter:BaseAddress"] = "https://frankfurter.example/",
            ["Flights:Providers:Frankfurter:TimeoutSeconds"] = timeoutSeconds.ToString(),
            ["Flights:Smtp:Host"] = "localhost",
            ["Flights:Smtp:Port"] = "1025",
            ["Flights:Smtp:FromAddress"] = "noreply@travel.example",
        };

        if (configureKeycloak)
        {
            values["Flights:Keycloak:AdminBaseUrl"] = "https://identity.example";
            values["Flights:Keycloak:Realm"] = "travel";
            values["Flights:Keycloak:ClientId"] = "travel-host";
            values["Flights:Keycloak:ClientSecret"] = "keycloak-test-secret";
            values["Flights:Keycloak:TimeoutSeconds"] = timeoutSeconds.ToString();
        }

        return values;
    }

    private static async Task InvokeProviderPipelineAsync(
        string pipeline,
        IServiceProvider services,
        CancellationToken ct
    )
    {
        switch (pipeline)
        {
            case "Duffel":
                using (await services.GetRequiredService<DuffelClient>().GetAsync("/slow", ct)) { }
                break;
            case "Travelpayouts":
                using (
                    await services.GetRequiredService<TravelpayoutsClient>().GetAsync("/slow", ct)
                ) { }
                break;
            case "Frankfurter":
                _ = await services
                    .GetRequiredService<FrankfurterClient>()
                    .GetRateAsync(
                        CurrencyCode.Create("USD").Value,
                        CurrencyCode.Create("EUR").Value,
                        ct
                    );
                break;
            case "KeycloakToken":
                _ = await services
                    .GetRequiredService<IKeycloakAdminTokenProvider>()
                    .GetTokenAsync(ct);
                break;
            case "KeycloakAdmin":
                using (
                    await services
                        .GetRequiredService<IHttpClientFactory>()
                        .CreateClient(KeycloakAdminAuthHandler.HttpClientName)
                        .GetAsync(RequestUri, ct)
                ) { }
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(pipeline), pipeline, null);
        }
    }

    private sealed class CountingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, int, HttpResponseMessage> _responseFactory;
        private int _attemptCount;

        public CountingHandler(HttpStatusCode statusCode)
            : this((request, _) => new HttpResponseMessage(statusCode) { RequestMessage = request })
        { }

        public CountingHandler(Func<HttpRequestMessage, int, HttpResponseMessage> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        public int AttemptCount => Volatile.Read(ref _attemptCount);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            var attempt = Interlocked.Increment(ref _attemptCount);
            return Task.FromResult(_responseFactory(request, attempt));
        }
    }

    private sealed class NeverCompletingHandler : HttpMessageHandler
    {
        private int _attemptCount;

        public int AttemptCount => Volatile.Read(ref _attemptCount);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Interlocked.Increment(ref _attemptCount);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("The never-completing handler was not cancelled.");
        }
    }
}
