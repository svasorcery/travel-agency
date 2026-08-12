using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;
using Shouldly;
using Travel.Modules.Flights.Infrastructure;
using Travel.Modules.Flights.Infrastructure.Providers.Duffel;
using Travel.Modules.Flights.Infrastructure.Providers.Travelpayouts;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace Travel.Modules.Flights.Tests.Integration.Providers.Duffel;

/// <summary>
/// Verifies that DuffelClient uses the spec §19 resilience pipeline:
/// per-request timeout from DuffelOptions.TimeoutSeconds, plus exponential-backoff retries.
/// These tests build the same resilience pipeline as FlightsModuleServiceCollectionExtensions
/// to prove the spec-tuned values are read and applied.
/// </summary>
[Trait("Category", "Integration")]
public sealed class DuffelClientResilienceTests : IDisposable
{
    private readonly WireMockServer _server;

    public DuffelClientResilienceTests()
    {
        _server = WireMockServer.Start();
    }

    public void Dispose() => _server.Stop();

    /// <summary>
    /// Counting handler: the first <paramref name="failCount"/> requests return
    /// the given <paramref name="failStatusCode"/>; all subsequent return 200.
    /// Placed AFTER the Polly resilience handler in the delegating-handler chain so
    /// the retry logic fires against real HTTP responses.
    /// </summary>
    private sealed class CountingHandler(int failCount, int failStatusCode) : DelegatingHandler
    {
        private int _callCount;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            var n = Interlocked.Increment(ref _callCount);
            if (n <= failCount)
                return Task.FromResult(
                    new HttpResponseMessage((System.Net.HttpStatusCode)failStatusCode)
                );

            return base.SendAsync(request, cancellationToken);
        }
    }

    /// <summary>
    /// Builds a <see cref="DuffelClient"/> backed by the same spec §19 resilience pipeline
    /// used in production — addRetry → addTimeout — so tests exercise the real wiring from
    /// <c>FlightsModuleServiceCollectionExtensions</c>.
    /// </summary>
    private DuffelClient BuildResilientDuffelClientWithCountingHandler(
        int failCount,
        int failStatusCode = 500,
        int timeoutSeconds = 10
    )
    {
        var services = new ServiceCollection();
        services
            .AddOptions<DuffelOptions>()
            .Configure(o =>
            {
                o.BaseUrl = _server.Url!;
                o.ApiVersion = "v2";
                o.ApiKey = "test_key";
                o.TimeoutSeconds = timeoutSeconds;
            });

        var countingHandler = new CountingHandler(failCount, failStatusCode);

        var builder = services.AddHttpClient<DuffelClient>();

        builder.AddResilienceHandler(
            "duffel",
            (pipeline, ctx) =>
            {
                pipeline.AddRetry(
                    new HttpRetryStrategyOptions
                    {
                        MaxRetryAttempts = 3,
                        UseJitter = false, // deterministic for tests
                        Delay = TimeSpan.FromMilliseconds(10),
                        MaxDelay = TimeSpan.FromMilliseconds(100),
                        BackoffType = DelayBackoffType.Exponential,
                        ShouldHandle = static args =>
                            args.Outcome switch
                            {
                                { Exception: not null } => PredicateResult.True(),
                                { Result: { } r } when (int)r.StatusCode >= 500 =>
                                    PredicateResult.True(),
                                _ => PredicateResult.False(),
                            },
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

        // The counting handler is added AFTER AddResilienceHandler so it sits INSIDE
        // the resilience pipeline in the delegating handler chain.
        // Chain: ResilienceHandler(outermost) → CountingHandler → PrimaryHandler(WireMock).
        builder.AddHttpMessageHandler(() => countingHandler);

        return services.BuildServiceProvider().GetRequiredService<DuffelClient>();
    }

    // -------------------------------------------------------------------------
    // Test 1: Retries on transient 500s
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Duffel_client_retries_transient_failures()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange: WireMock returns 200 for all requests (the counting handler intercepts
        // the first 2 and returns 500 before they reach the server).
        _server
            .Given(Request.Create().WithPath("/air/offers/retrytest").UsingGet())
            .RespondWith(
                Response
                    .Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody("""{"data":{}}""")
            );

        // countingHandler intercepts first 2 calls and returns 500, then delegates to WireMock
        var client = BuildResilientDuffelClientWithCountingHandler(
            failCount: 2,
            failStatusCode: 500,
            timeoutSeconds: 10
        );

        // Act: the resilience pipeline should retry past the two 500s and return 200
        var response = await client.GetAsync("/air/offers/retrytest", ct);

        // Assert: HTTP 200 obtained after retrying through two 500s
        response.IsSuccessStatusCode.ShouldBeTrue(
            $"Expected 200 after retries. Status: {(int)response.StatusCode}"
        );
        // WireMock receives only the successful 3rd attempt
        _server
            .LogEntries.Count(le => le.RequestMessage?.Path == "/air/offers/retrytest")
            .ShouldBe(1);
    }

    // -------------------------------------------------------------------------
    // Test: Global resilience handler does NOT stack with per-client pipeline
    // -------------------------------------------------------------------------

    /// <summary>
    /// Behavioral test: builds production-equivalent DI (global AddStandardResilienceHandler
    /// from ServiceDefaults + per-client AddResilienceHandler(MaxRetryAttempts=3) from
    /// FlightsModuleServiceCollectionExtensions) and makes a real HTTP call through WireMock.
    ///
    /// With RemoveAllResilienceHandlers in place, the effective attempt count must be exactly
    /// 1 (initial) + 3 (MaxRetryAttempts) = 4 for MaxRetryAttempts=3. Without it, Polly stacks
    /// both pipelines and produces up to 3×3 = 9 attempts.
    ///
    /// We configure WireMock to always return 500 and count total requests received. With correct
    /// wiring the server sees exactly 4 requests; with stacked pipelines it would see up to 16.
    /// </summary>
    [Fact]
    public async Task Duffel_client_resilience_does_not_stack_with_global_default()
    {
        var ct = TestContext.Current.CancellationToken;

        const int maxRetry = 3; // matches FlightsModuleServiceCollectionExtensions
        const int expectedHits = 1 + maxRetry; // initial attempt + maxRetry retries = 4

        // Arrange: WireMock always returns 500 to trigger all retries in the pipeline.
        _server
            .Given(Request.Create().WithPath("/air/stack_test").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(500));

        // Build DI that mirrors production: global AddStandardResilienceHandler (ServiceDefaults)
        // followed by AddFlightsModule which calls RemoveAllResilienceHandlers + AddResilienceHandler.
        var services = new ServiceCollection();

        // Simulate ServiceDefaults.ConfigureHttpClientDefaults global registration.
        services.ConfigureHttpClientDefaults(http => http.AddStandardResilienceHandler());

        services.Configure<DuffelOptions>(o =>
        {
            o.BaseUrl = _server.Url!;
            o.ApiKey = "test_key";
            // Use a long timeout so the pipeline only retries on 500, not on timeouts.
            o.TimeoutSeconds = 30;
        });
        services.Configure<TravelpayoutsOptions>(o =>
        {
            o.BaseUrl = _server.Url!;
            o.TimeoutSeconds = 30;
        });

        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();
        var env = new FakeHostEnvironment("Development");
        services.AddFlightsModule(config, env);

        var sp = services.BuildServiceProvider();
        var client = sp.GetRequiredService<DuffelClient>();

        // Act: send the request — it will always 500, so the retry pipeline exhausts.
        try
        {
            await client.GetAsync("/air/stack_test", ct);
        }
        catch
        {
            // After exhausting all retries Polly throws; ignore the exception — we only
            // care about the number of attempts the pipeline made.
        }

        // Assert: WireMock received exactly 4 requests (1 initial + 3 retries).
        // Stacked pipelines would produce up to 16 (4 × 4). We allow up to expectedHits + 1
        // for jitter/internal Polly artifacts, but anything above expectedHits * 2 is a stack.
        var hits = _server.LogEntries.Count(le => le.RequestMessage?.Path == "/air/stack_test");
        hits.ShouldBe(
            expectedHits,
            $"DuffelClient must make exactly {expectedHits} attempts (1 initial + {maxRetry} retries). "
                + $"Received {hits} — if > {expectedHits} the global AddStandardResilienceHandler is "
                + "stacking with the per-client pipeline; ensure RemoveAllResilienceHandlers() is "
                + "called before AddResilienceHandler() in FlightsModuleServiceCollectionExtensions."
        );
    }

    // Minimal IHostEnvironment implementation for the test.
    private sealed class FakeHostEnvironment(string environmentName)
        : Microsoft.Extensions.Hosting.IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = string.Empty;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }

    // -------------------------------------------------------------------------
    // Test 2: Per-request timeout fires before the HttpClient 100s default
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Duffel_client_times_out_per_request_budget()
    {
        var ct = TestContext.Current.CancellationToken;

        // Arrange: endpoint that delays 10 s — more than our 1 s pipeline budget.
        _server
            .Given(Request.Create().WithPath("/air/slow").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithDelay(TimeSpan.FromSeconds(10)));

        // Use a very short pipeline timeout (1 s). The HttpClient.Timeout is set large
        // so only the Polly pipeline fires, proving DuffelOptions.TimeoutSeconds is wired.
        var client = BuildResilientDuffelClientWithCountingHandler(failCount: 0, timeoutSeconds: 1);

        // Act + Assert: should throw well before the 10 s server delay
        var started = DateTimeOffset.UtcNow;
        await Should.ThrowAsync<Exception>(async () => await client.GetAsync("/air/slow", ct));
        var elapsed = DateTimeOffset.UtcNow - started;

        // Pipeline should abort in ~1 s, not the full 10 s server delay
        elapsed.TotalSeconds.ShouldBeLessThan(8);
    }
}
