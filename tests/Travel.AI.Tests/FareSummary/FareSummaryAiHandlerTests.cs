using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Travel.AI.FareSummary;
using Travel.IntegrationContracts.AI.FareSummary;
using Xunit;

namespace Travel.AI.Tests.FareSummary;

public sealed class FareSummaryAiHandlerTests
{
    [Theory]
    [InlineData(false, "InvalidOutput")]
    [InlineData(true, "AccountingUnavailable")]
    public async Task Known_usage_is_accounted_even_for_invalid_output_and_failure_does_not_retry(
        bool fail,
        string expected
    )
    {
        var now = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
        var time = new FakeTimeProvider(now);
        var chat = new FakeChat();
        var ledger = new FakeLedger(fail);
        var options = new FareSummaryOptions
        {
            Enabled = true,
            Model = "fictional",
            PricingModel = "fictional",
            PricingVerified = true,
            InputUsdPerMillion = 1,
            OutputUsdPerMillion = 1,
        };
        var request = new FareSummaryRequested(
            Guid.NewGuid(),
            new string('a', 64),
            FareSummaryMessageIdentity.PromptVersion,
            "ru",
            now.AddSeconds(4),
            [new("f1", "change", "change", "Allowed", null, null, null, [new("offer")])]
        );
        var reply = await FareSummaryAiHandler.Handle(
            request,
            new(chat, options, new(time), time),
            ledger,
            time,
            NullLogger<FareSummaryRequested>.Instance,
            TestContext.Current.CancellationToken
        );
        reply.Status.ShouldBe(expected);
        reply.Sections.ShouldBeNull();
        ledger.Calls.ShouldBe(1);
        chat.Calls.ShouldBe(1);
        ledger.Result!.CostUsd.ShouldBe(0.000015m);
    }

    [Fact]
    public async Task Missing_usage_has_no_ledger_row()
    {
        var time = new FakeTimeProvider();
        var chat = new FakeChat { Usage = null };
        var ledger = new FakeLedger(false);
        var options = new FareSummaryOptions
        {
            Enabled = true,
            Model = "fictional",
            PricingModel = "fictional",
            PricingVerified = true,
            InputUsdPerMillion = 1,
            OutputUsdPerMillion = 1,
        };
        var request = new FareSummaryRequested(
            Guid.NewGuid(),
            new string('a', 64),
            FareSummaryMessageIdentity.PromptVersion,
            "ru",
            time.GetUtcNow().AddSeconds(4),
            [new("f1", "change", "change", "Allowed", null, null, null, [new("offer")])]
        );
        var reply = await FareSummaryAiHandler.Handle(
            request,
            new(chat, options, new(time), time),
            ledger,
            time,
            NullLogger<FareSummaryRequested>.Instance,
            TestContext.Current.CancellationToken
        );
        reply.Status.ShouldBe("UsageUnknown");
        ledger.Calls.ShouldBe(0);
        reply.CostUsd.ShouldBeNull();
    }

    [Fact]
    public async Task Ledger_ignoring_cancellation_cannot_hold_reply_past_absolute_deadline()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));
        var chat = new FakeChat();
        var request = new FareSummaryRequested(
            Guid.NewGuid(),
            new string('a', 64),
            FareSummaryMessageIdentity.PromptVersion,
            "ru",
            time.GetUtcNow().AddSeconds(4),
            [new("f1", "change", "change", "Allowed", null, null, null, [new("offer")])]
        );
        var options = new FareSummaryOptions
        {
            Enabled = true,
            Model = "fictional",
            PricingModel = "fictional",
            PricingVerified = true,
            InputUsdPerMillion = 1,
            OutputUsdPerMillion = 1,
        };
        var ledger = new PendingLedger();
        var handling = FareSummaryAiHandler.Handle(
            request,
            new(chat, options, new(time), time),
            ledger,
            time,
            NullLogger<FareSummaryRequested>.Instance,
            TestContext.Current.CancellationToken
        );
        ledger.Calls.ShouldBe(1);
        time.Advance(TimeSpan.FromSeconds(4));
        var result = await handling.WaitAsync(
            TimeSpan.FromSeconds(1),
            TestContext.Current.CancellationToken
        );
        result.Status.ShouldBe("AccountingUnavailable");
        result.Sections.ShouldBeNull();
        chat.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task Accounting_is_inside_the_single_inflight_request_guard()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));
        var chat = new FakeChat();
        var ledger = new PendingLedger();
        var options = new FareSummaryOptions
        {
            Enabled = true,
            Model = "fictional",
            PricingModel = "fictional",
            PricingVerified = true,
            InputUsdPerMillion = 1,
            OutputUsdPerMillion = 1,
        };
        var extractor = new FareSummaryExtractor(chat, options, new(time), time);
        var request = new FareSummaryRequested(
            Guid.NewGuid(),
            new string('a', 64),
            FareSummaryMessageIdentity.PromptVersion,
            "ru",
            time.GetUtcNow().AddSeconds(4),
            [new("f1", "change", "change", "Allowed", null, null, null, [new("offer")])]
        );
        var first = FareSummaryAiHandler.Handle(
            request,
            extractor,
            ledger,
            time,
            NullLogger<FareSummaryRequested>.Instance,
            TestContext.Current.CancellationToken
        );
        var second = await FareSummaryAiHandler.Handle(
            request with
            {
                CorrelationId = Guid.NewGuid(),
            },
            extractor,
            new FakeLedger(false),
            time,
            NullLogger<FareSummaryRequested>.Instance,
            TestContext.Current.CancellationToken
        );
        second.Status.ShouldBe("Busy");
        chat.Calls.ShouldBe(1);
        ledger.Complete();
        await first.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
    }

    private sealed class PendingLedger : IFareSummaryLedger
    {
        private readonly TaskCompletionSource completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        public int Calls;

        public Task RecordAsync(FareSummaryPlanned result, CancellationToken cancellationToken)
        {
            Calls++;
            return completion.Task;
        }

        public void Complete() => completion.TrySetResult();
    }

    private sealed class FakeLedger(bool fail) : IFareSummaryLedger
    {
        public int Calls;
        public FareSummaryPlanned? Result;

        public Task RecordAsync(FareSummaryPlanned result, CancellationToken cancellationToken)
        {
            Calls++;
            Result = result;
            return fail
                ? Task.FromException(new InvalidOperationException("fictional ledger failure"))
                : Task.CompletedTask;
        }
    }

    private sealed class FakeChat : IChatClient
    {
        public int Calls;
        public UsageDetails? Usage = new() { InputTokenCount = 10, OutputTokenCount = 5 };

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default
        )
        {
            Calls++;
            return Task.FromResult(
                new ChatResponse(new ChatMessage(ChatRole.Assistant, "invalid"))
                {
                    Usage = Usage,
                    ModelId = "fictional",
                }
            );
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }
}
