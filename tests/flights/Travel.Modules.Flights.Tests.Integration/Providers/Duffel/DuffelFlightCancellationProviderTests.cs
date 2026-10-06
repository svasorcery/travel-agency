using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Travel.Modules.Flights.Core.Cancellation;
using Travel.Modules.Flights.Core.Providers;
using Travel.Modules.Flights.Core.Providers.Dtos;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Infrastructure;
using Travel.Modules.Flights.Infrastructure.Providers.Duffel;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace Travel.Modules.Flights.Tests.Integration.Providers.Duffel;

[Trait("Category", "Integration")]
public sealed class DuffelFlightCancellationProviderTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
    private readonly WireMockServer server = WireMockServer.Start();
    private readonly FakeTimeProvider time = new(Now.AddSeconds(2));
    private readonly HttpClient http;
    private readonly DuffelFlightCancellationProvider provider;

    public DuffelFlightCancellationProviderTests()
    {
        new Uri(server.Url!).IsLoopback.ShouldBeTrue();
        http = new HttpClient { BaseAddress = new Uri(server.Url!) };
        provider = new(
            new DuffelClient(
                http,
                Options.Create(
                    new DuffelOptions
                    {
                        BaseUrl = server.Url!,
                        ApiKey = "fictional-test-key",
                        ApiVersion = "v2",
                    }
                )
            ),
            time
        );
    }

    public void Dispose()
    {
        http.Dispose();
        server.Stop();
        server.Dispose();
    }

    [Fact]
    public async Task Create_uses_one_post_and_never_confirms_the_returned_quote()
    {
        Order();
        Stub("/air/order_cancellations", false, 201, Cancellation(false));
        var result = await provider.CreateTermsAsync(
            "ord_fictional_1",
            TestContext.Current.CancellationToken
        );
        result.IsError.ShouldBeFalse();
        result.Value.Outcome.ShouldBe(CancellationQuoteOutcome.TermsAvailable);
        server.LogEntries.Count(e => e.RequestMessage!.Method == "POST").ShouldBe(1);
        server.LogEntries.ShouldNotContain(e =>
            e.RequestMessage!.Path!.Contains("actions/confirm")
        );
    }

    [Fact]
    public async Task Confirm_is_bodyless_and_success_requires_exact_saved_conditions()
    {
        Stub(
            "/air/order_cancellations/occ_fictional_1/actions/confirm",
            false,
            200,
            Cancellation(true)
        );
        var result = await provider.ConfirmAsync(Terms(), TestContext.Current.CancellationToken);
        result.IsError.ShouldBeFalse();
        result.Value.Outcome.ShouldBe(CancellationEffectOutcome.Confirmed);
        var request = server
            .LogEntries.Single(e => e.RequestMessage!.Method == "POST")
            .RequestMessage;
        string.IsNullOrEmpty(request!.Body).ShouldBeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("{\"data\":null}")]
    [InlineData("malformed")]
    public async Task Empty_or_malformed_success_is_unknown_without_repeat(string body)
    {
        Stub("/air/order_cancellations/occ_fictional_1/actions/confirm", false, 200, body);
        var result = await provider.ConfirmAsync(Terms(), TestContext.Current.CancellationToken);
        result.IsError.ShouldBeFalse();
        result.Value.Outcome.ShouldBe(CancellationEffectOutcome.Unknown);
        server.LogEntries.Count(e => e.RequestMessage!.Method == "POST").ShouldBe(1);
    }

    [Fact]
    public async Task Cancellation_get_404_can_recover_matching_order_evidence_with_two_gets_only()
    {
        Stub("/air/order_cancellations/occ_fictional_1", true, 404, "{}");
        Order(confirmed: true);
        var terms = Terms();
        var result = await provider.ObserveAsync(
            new(
                terms.ProviderOrderRef,
                terms.ProviderCancellationRef,
                terms,
                terms.ItineraryPartyHash
            ),
            TestContext.Current.CancellationToken
        );
        result.Value.State.ShouldBe(CancellationObservationState.Confirmed);
        server.LogEntries.Count.ShouldBe(2);
        server.LogEntries.ShouldNotContain(e => e.RequestMessage!.Method != "GET");
    }

    [Fact]
    public async Task Cancelled_order_with_different_cancellation_id_is_not_own_success()
    {
        Stub("/air/order_cancellations/occ_fictional_1", true, 404, "{}");
        Order(confirmed: true, cancellationId: "occ_foreign");
        var terms = Terms();
        var result = await provider.ObserveAsync(
            new(
                terms.ProviderOrderRef,
                terms.ProviderCancellationRef,
                terms,
                terms.ItineraryPartyHash
            ),
            TestContext.Current.CancellationToken
        );
        result.Value.State.ShouldBe(CancellationObservationState.Unknown);
        server.LogEntries.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Lost_create_has_no_heuristic_candidate_selection_or_list_scan()
    {
        Order(confirmed: true);
        var result = await provider.ObserveAsync(
            new("ord_fictional_1", null, null, new string('a', 64)),
            TestContext.Current.CancellationToken
        );
        result.Value.State.ShouldBe(CancellationObservationState.Unknown);
        result.Value.Reason.ShouldBe(CancellationReason.Uncorrelated);
        server
            .LogEntries.ShouldHaveSingleItem()
            .RequestMessage!.Path.ShouldBe("/air/orders/ord_fictional_1");
    }

    [Fact]
    public async Task Total_observation_budget_cancels_a_stalled_read_without_starting_fallback()
    {
        using var handler = new StalledHandler();
        using var controlled = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://127.0.0.1:1"),
        };
        var current = new DuffelFlightCancellationProvider(
            new DuffelClient(
                controlled,
                Options.Create(new DuffelOptions { ApiKey = "fictional" })
            ),
            time
        );
        var terms = Terms();
        var task = current.ObserveAsync(
            new(
                terms.ProviderOrderRef,
                terms.ProviderCancellationRef,
                terms,
                terms.ItineraryPartyHash
            ),
            TestContext.Current.CancellationToken
        );
        await handler.Entered.Task.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken
        );
        time.Advance(TimeSpan.FromSeconds(10));
        var result = await task;
        result.Value.State.ShouldBe(CancellationObservationState.Unknown);
        handler.Count.ShouldBe(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Registered_provider_never_retries_mutation_or_logs_operation_refs(bool create)
    {
        Order();
        var path = create
            ? "/air/order_cancellations"
            : "/air/order_cancellations/occ_fictional_1/actions/confirm";
        Stub(path, false, 503, "{}");
        using var capture = new LogCapture();
        using var services = RegisteredServices(capture);
        using var scope = services.CreateScope();
        var registered = scope.ServiceProvider.GetRequiredService<IFlightCancellationProvider>();
        if (create)
        {
            var result = await registered.CreateTermsAsync(
                "ord_fictional_1",
                TestContext.Current.CancellationToken
            );
            result.Value.Outcome.ShouldBe(CancellationQuoteOutcome.Unknown);
        }
        else
        {
            var result = await registered.ConfirmAsync(
                Terms(),
                TestContext.Current.CancellationToken
            );
            result.Value.Outcome.ShouldBe(CancellationEffectOutcome.Unknown);
        }
        server.LogEntries.Count(e => e.RequestMessage!.Method == "POST").ShouldBe(1);
        capture.Messages.ShouldNotContain(message =>
            message.Contains("occ_fictional_1")
            || message.Contains("ord_fictional_1")
            || message.Contains("fictional-test-key")
        );
    }

    [Fact]
    public async Task Duplicate_wire_identity_is_unknown_even_when_last_property_matches()
    {
        var body = Cancellation(true)
            .Replace(
                "\"id\":\"occ_fictional_1\"",
                "\"id\":\"occ_foreign\",\"id\":\"occ_fictional_1\""
            );
        Stub("/air/order_cancellations/occ_fictional_1/actions/confirm", false, 200, body);
        var result = await provider.ConfirmAsync(Terms(), TestContext.Current.CancellationToken);
        result.Value.Outcome.ShouldBe(CancellationEffectOutcome.Unknown);
        server.LogEntries.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Oversized_success_body_is_unknown_and_not_retained_or_retried()
    {
        Stub(
            "/air/order_cancellations/occ_fictional_1/actions/confirm",
            false,
            200,
            new string(' ', 512 * 1024 + 1)
        );
        var result = await provider.ConfirmAsync(Terms(), TestContext.Current.CancellationToken);
        result.Value.Outcome.ShouldBe(CancellationEffectOutcome.Unknown);
        server.LogEntries.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Existing_direct_cancel_stays_unsupported_before_http()
    {
        var old = new DuffelFlightBookingProvider(
            new DuffelClient(
                http,
                Options.Create(
                    new DuffelOptions { BaseUrl = server.Url!, ApiKey = "fictional-test-key" }
                )
            ),
            time,
            Microsoft
                .Extensions
                .Logging
                .Abstractions
                .NullLogger<DuffelFlightBookingProvider>
                .Instance
        );
        var result = await old.CancelOrderAsync(
            "ord_fictional_1",
            TestContext.Current.CancellationToken
        );
        result.IsError.ShouldBeTrue();
        result.FirstError.Code.ShouldBe("Flights.ProviderCancellationNotSupported");
        server.LogEntries.ShouldBeEmpty();
    }

    [Fact]
    public async Task Supplier_payment_success_exposes_exact_normalized_balance_receipt()
    {
        Stub(
            "/air/orders/ord_fictional_1",
            true,
            200,
            JsonSerializer.Serialize(
                new
                {
                    data = new
                    {
                        id = "ord_fictional_1",
                        total_amount = "100.00",
                        total_currency = "USD",
                        payment_status = new
                        {
                            awaiting_payment = true,
                            payment_required_by = Now.AddHours(1),
                        },
                    },
                }
            )
        );
        Stub(
            "/air/payments",
            false,
            201,
            JsonSerializer.Serialize(
                new
                {
                    data = new
                    {
                        id = "pay_fictional_receipt",
                        order_id = "ord_fictional_1",
                        status = "succeeded",
                        type = "balance",
                        amount = "100.00",
                        currency = "USD",
                    },
                }
            )
        );
        var old = new DuffelFlightBookingProvider(
            new DuffelClient(
                http,
                Options.Create(
                    new DuffelOptions { BaseUrl = server.Url!, ApiKey = "fictional-test-key" }
                )
            ),
            time,
            Microsoft
                .Extensions
                .Logging
                .Abstractions
                .NullLogger<DuffelFlightBookingProvider>
                .Instance
        );
        var result = await old.ConfirmOrderAsync(
            "ord_fictional_1",
            Travel.Modules.Flights.Core.ValueObjects.Identifiers.PaymentRef.New(),
            Money.Create(100m, CurrencyCode.Create("USD").Value).Value,
            TestContext.Current.CancellationToken
        );
        result.IsError.ShouldBeFalse();
        result.Value.PaymentEvidence.ShouldNotBeNull();
        result.Value.PaymentEvidence.Reference.ShouldBe("pay_fictional_receipt");
        result.Value.PaymentEvidence.Amount.Amount.ShouldBe(100m);
        result.Value.PaymentEvidence.Kind.ShouldBe(SupplierPaymentKind.Balance);
    }

    [Theory]
    [InlineData("refund_amount", "50.00")]
    [InlineData("refund_to", "card")]
    public async Task Changed_same_id_terms_are_rejected_before_confirm_post(
        string field,
        string value
    )
    {
        Stub(
            "/air/order_cancellations/occ_fictional_1/actions/confirm",
            false,
            200,
            Cancellation(true)
        );
        var pending = JsonSerializer.Deserialize<System.Text.Json.Nodes.JsonObject>(
            Cancellation(false)
        )!;
        pending["data"]![field] = value;
        Stub("/air/order_cancellations/occ_fictional_1", true, 200, pending.ToJsonString());
        var result = await provider.ConfirmAsync(Terms(), TestContext.Current.CancellationToken);
        result.IsError.ShouldBeFalse();
        result.Value.Outcome.ShouldBe(CancellationEffectOutcome.Unknown);
        server.LogEntries.ShouldNotContain(e => e.RequestMessage!.Method == "POST");
    }

    [Fact]
    public async Task Final_supplier_read_is_followed_by_continuation_fence_before_payment_post()
    {
        Stub(
            "/air/orders/ord_fictional_1",
            true,
            200,
            JsonSerializer.Serialize(
                new
                {
                    data = new
                    {
                        id = "ord_fictional_1",
                        total_amount = "100.00",
                        total_currency = "USD",
                        payment_status = new
                        {
                            awaiting_payment = true,
                            payment_required_by = Now.AddHours(1),
                        },
                    },
                }
            )
        );
        Stub("/air/payments", false, 201, "{}");
        var booking = new DuffelFlightBookingProvider(
            new DuffelClient(
                http,
                Options.Create(
                    new DuffelOptions { BaseUrl = server.Url!, ApiKey = "fictional-test-key" }
                )
            ),
            time,
            Microsoft
                .Extensions
                .Logging
                .Abstractions
                .NullLogger<DuffelFlightBookingProvider>
                .Instance
        );
        var guards = 0;
        var result = await booking.ConfirmOrderAsync(
            "ord_fictional_1",
            Travel.Modules.Flights.Core.ValueObjects.Identifiers.PaymentRef.New(),
            Money.Create(100m, CurrencyCode.Create("USD").Value).Value,
            _ =>
            {
                server.LogEntries.Count(e => e.RequestMessage!.Method == "GET").ShouldBe(1);
                guards++;
                return Task.FromResult(false);
            },
            TestContext.Current.CancellationToken
        );
        result.IsError.ShouldBeTrue();
        guards.ShouldBe(1);
        server.LogEntries.ShouldNotContain(e => e.RequestMessage!.Method == "POST");
    }

    [Fact]
    public async Task Order_cancelled_at_only_preserves_fact_without_own_success_or_financial_receipt()
    {
        Stub("/air/order_cancellations/occ_fictional_1", true, 404, "{}");
        Stub(
            "/air/orders/ord_fictional_1",
            true,
            200,
            JsonSerializer.Serialize(
                new
                {
                    data = new
                    {
                        id = "ord_fictional_1",
                        cancelled_at = Now.AddSeconds(1),
                        cancellation = (object?)null,
                    },
                }
            )
        );
        var terms = Terms();
        var result = await provider.ObserveAsync(
            new(
                terms.ProviderOrderRef,
                terms.ProviderCancellationRef,
                terms,
                terms.ItineraryPartyHash
            ),
            TestContext.Current.CancellationToken
        );
        result.Value.State.ShouldBe(CancellationObservationState.Unknown);
        result.Value.OrderCancellation.ShouldNotBeNull();
        result.Value.OrderCancellation.CancelledAt.ShouldBe(Now.AddSeconds(1));
        result.Value.Quote.ShouldBeNull();
    }

    [Fact]
    public async Task Cancelled_order_preflight_preserves_fact_without_creating_quote()
    {
        Stub(
            "/air/orders/ord_fictional_1",
            true,
            200,
            JsonSerializer.Serialize(
                new
                {
                    data = new
                    {
                        id = "ord_fictional_1",
                        cancelled_at = Now.AddSeconds(1),
                        available_actions = Array.Empty<string>(),
                    },
                }
            )
        );
        var inspection = await provider.InspectOrderAsync(
            "ord_fictional_1",
            TestContext.Current.CancellationToken
        );
        inspection.Value.OrderCancellation.ShouldNotBeNull();
        var creation = await provider.CreateTermsAsync(
            "ord_fictional_1",
            TestContext.Current.CancellationToken
        );
        creation.Value.OrderCancellation.ShouldNotBeNull();
        creation.Value.Outcome.ShouldBe(CancellationQuoteOutcome.DefinitivelyRejected);
        server.LogEntries.ShouldNotContain(e => e.RequestMessage!.Method == "POST");
    }

    private ServiceProvider RegisteredServices(LogCapture capture)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Flights:Duffel:BaseUrl"] = server.Url,
                    ["Flights:Duffel:ApiVersion"] = "v2",
                    ["Flights:Duffel:ApiKey"] = "fictional-test-key",
                    ["Flights:Travelpayouts:Enabled"] = "false",
                }
            )
            .Build();
        var services = new ServiceCollection();
        services.AddLogging(builder =>
            builder.ClearProviders().AddProvider(capture).SetMinimumLevel(LogLevel.Trace)
        );
        services.AddSingleton<TimeProvider>(time);
        typeof(DuffelClient)
            .Assembly.GetType(
                "Travel.Modules.Flights.Infrastructure.FlightsInfrastructureServiceCollectionExtensions"
            )!
            .GetMethod("AddFlightsInfrastructure", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, new object[] { services, config, new TestEnvironment() });
        return services.BuildServiceProvider();
    }

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "SyntheticCancellationContract";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class LogCapture : ILoggerProvider
    {
        public List<string> Messages { get; } = [];

        public ILogger CreateLogger(string categoryName) => new Logger(this);

        public void Dispose() { }

        private sealed class Logger(LogCapture owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel level) => true;

            public void Log<TState>(
                LogLevel level,
                EventId id,
                TState state,
                Exception? error,
                Func<TState, Exception?, string> formatter
            )
            {
                lock (owner.Messages)
                    owner.Messages.Add(formatter(state, error));
            }
        }
    }

    private sealed class StalledHandler : HttpMessageHandler
    {
        public TaskCompletionSource Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Count { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct
        )
        {
            Count++;
            Entered.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return new(HttpStatusCode.OK);
        }
    }

    private void Stub(string path, bool get, int status, string body)
    {
        if (!get && path.EndsWith("/actions/confirm", StringComparison.Ordinal))
            server
                .Given(
                    Request.Create().WithPath("/air/order_cancellations/occ_fictional_1").UsingGet()
                )
                .RespondWith(
                    Response
                        .Create()
                        .WithStatusCode(200)
                        .WithHeader("Content-Type", "application/json")
                        .WithBody(Cancellation(false))
                );
        server
            .Given(
                get
                    ? Request.Create().WithPath(path).UsingGet()
                    : Request.Create().WithPath(path).UsingPost()
            )
            .RespondWith(
                Response
                    .Create()
                    .WithStatusCode(status)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody(body)
            );
    }

    private void Order(bool confirmed = false, string cancellationId = "occ_fictional_1")
    {
        var node = JsonSerializer.Serialize(
            new
            {
                data = new
                {
                    id = "ord_fictional_1",
                    available_actions = new[] { "cancel" },
                    total_amount = "100.00",
                    total_currency = "USD",
                    payment_status = new { awaiting_payment = false },
                    cancelled_at = confirmed ? (DateTimeOffset?)Now.AddSeconds(1) : null,
                    cancellation = confirmed
                        ? JsonSerializer
                            .Deserialize<JsonElement>(Cancellation(true, cancellationId))
                            .GetProperty("data")
                        : (JsonElement?)null,
                },
            }
        );
        Stub("/air/orders/ord_fictional_1", true, 200, node);
    }

    private static string Cancellation(bool confirmed, string id = "occ_fictional_1") =>
        JsonSerializer.Serialize(
            new
            {
                data = new
                {
                    id,
                    order_id = "ord_fictional_1",
                    created_at = Now,
                    expires_at = Now.AddMinutes(10),
                    confirmed_at = confirmed ? (DateTimeOffset?)Now.AddSeconds(1) : null,
                    refund_amount = "17.25",
                    refund_currency = "USD",
                    refund_to = "balance",
                    airline_credits = Array.Empty<object>(),
                },
            }
        );

    private static CancellationTerms Terms() =>
        CancellationTerms
            .Create(
                new(
                    1,
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    "ord_fictional_1",
                    "occ_fictional_1",
                    new string('a', 64),
                    Money.Create(17.25m, CurrencyCode.Create("USD").Value).Value,
                    CancellationRefundDestination.Balance,
                    new(
                        SettlementComposition.CashOnly,
                        true,
                        CancellationResolutionSource.SupplierApi,
                        false,
                        false
                    ),
                    Now.AddMinutes(10),
                    "cancellation-v1"
                ),
                Now
            )
            .Value;
}
