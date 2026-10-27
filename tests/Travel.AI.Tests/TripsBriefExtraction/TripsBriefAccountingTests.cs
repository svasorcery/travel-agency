using Microsoft.Extensions.AI;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Travel.AI.TripsBriefExtraction;
using Travel.IntegrationContracts.AI.TripsBriefExtraction;
using Xunit;
using static Travel.AI.Tests.TripsBriefExtraction.TripsBriefExtractorTests;

namespace Travel.AI.Tests.TripsBriefExtraction;

public sealed class TripsBriefAccountingTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Noncooperative_accounting_cannot_extend_reply_or_release_active_lease(
        bool admissionStage
    )
    {
        var time = new FakeTimeProvider();
        var options = Enabled();
        var admission = new TripsBriefAdmission(options, time);
        var chat = new FakeChat();
        var ledger = new PendingLedger(admissionStage);
        var extractor = new TripsBriefExtractor(chat, options, admission, time);
        var handling = extractor.ExtractAsync(
            Request(time, options),
            ledger,
            TestContext.Current.CancellationToken
        );
        ledger.Started.ShouldBeTrue();
        chat.Calls.ShouldBe(admissionStage ? 0 : 1);
        time.Advance(TimeSpan.FromMilliseconds(4500));
        var result = await handling.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken
        );
        result.Status.ShouldBe("AccountingUnavailable");
        result.Fields.ShouldBeNull();
        admission.TryEnter(.001m).ShouldBeNull();
        ledger.Release();
        TripsBriefAdmission.Lease? next = null;
        SpinWait
            .SpinUntil(
                () => (next = admission.TryEnter(.001m)) is not null,
                TimeSpan.FromSeconds(2)
            )
            .ShouldBeTrue();
        next!.Dispose();
        chat.Calls.ShouldBe(admissionStage ? 0 : 1);
    }

    [Fact]
    public async Task Provider_and_accounting_tasks_both_hold_the_same_lease_until_both_settle()
    {
        var time = new FakeTimeProvider();
        var options = Enabled();
        var admission = new TripsBriefAdmission(options, time);
        var pending = new TaskCompletionSource<ChatResponse>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var chat = new FakeChat { Pending = pending };
        var ledger = new PendingLedger(false);
        var extractor = new TripsBriefExtractor(chat, options, admission, time);
        var handling = extractor.ExtractAsync(
            Request(time, options),
            ledger,
            TestContext.Current.CancellationToken
        );
        time.Advance(TimeSpan.FromMilliseconds(3500));
        await ledger.Signal.Task.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken
        );
        time.Advance(TimeSpan.FromMilliseconds(1000));
        (
            await handling.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken)
        ).Status.ShouldBe("AccountingUnavailable");
        ledger.Release();
        await Task.Yield();
        admission.TryEnter(.001m).ShouldBeNull("SDK still owns its prompt");
        pending.SetException(new InvalidOperationException("fictional private provider payload"));
        TripsBriefAdmission.Lease? next = null;
        SpinWait
            .SpinUntil(
                () => (next = admission.TryEnter(.001m)) is not null,
                TimeSpan.FromSeconds(2)
            )
            .ShouldBeTrue();
        next!.Dispose();
    }

    [Fact]
    public async Task Budget_is_rechecked_after_admission_storage_before_provider_dispatch()
    {
        var time = new FakeTimeProvider();
        var options = Enabled();
        var admission = new TripsBriefAdmission(options, time);
        var chat = new FakeChat();
        var ledger = new DelayedAdmissionLedger(time);
        var extractor = new TripsBriefExtractor(chat, options, admission, time);
        var result = await extractor.ExtractAsync(
            Request(time, options),
            ledger,
            TestContext.Current.CancellationToken
        );
        result.Status.ShouldBe("DeadlineExceeded");
        chat.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task Processing_policy_is_rechecked_after_admission_storage_before_dispatch()
    {
        var time = new FakeTimeProvider();
        var options = Enabled();
        var admission = new TripsBriefAdmission(options, time);
        var chat = new FakeChat();
        var ledger = new PolicyChangeLedger(options);
        var extractor = new TripsBriefExtractor(chat, options, admission, time);
        var result = await extractor.ExtractAsync(
            Request(time, options),
            ledger,
            TestContext.Current.CancellationToken
        );
        result.Status.ShouldBe("ProcessingPolicyChanged");
        chat.Calls.ShouldBe(0);
    }

    private sealed class PendingLedger(bool admissionStage) : ITripsBriefLedger
    {
        private readonly TaskCompletionSource pending = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        public TaskCompletionSource Signal { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Started;

        public void Release() => pending.TrySetResult();

        public Task AdmitAsync(Guid id, string model, CancellationToken ct) =>
            admissionStage ? Block() : Task.CompletedTask;

        public Task CompleteAsync(TripsBriefParsed result, CancellationToken ct) =>
            admissionStage ? Task.CompletedTask : Block();

        private Task Block()
        {
            Started = true;
            Signal.TrySetResult();
            return pending.Task;
        }
    }

    private sealed class DelayedAdmissionLedger(FakeTimeProvider time) : ITripsBriefLedger
    {
        public Task AdmitAsync(Guid id, string model, CancellationToken ct)
        {
            time.Advance(TimeSpan.FromMilliseconds(4100));
            return Task.CompletedTask;
        }

        public Task CompleteAsync(TripsBriefParsed result, CancellationToken ct) =>
            throw new InvalidOperationException("No provider dispatch should occur.");
    }

    private sealed class PolicyChangeLedger(TripsBriefOptions options) : ITripsBriefLedger
    {
        public Task AdmitAsync(Guid id, string model, CancellationToken ct)
        {
            options.Model = "changed";
            options.PricingModel = "changed";
            return Task.CompletedTask;
        }

        public Task CompleteAsync(TripsBriefParsed result, CancellationToken ct) =>
            throw new InvalidOperationException("No provider dispatch should occur.");
    }
}
