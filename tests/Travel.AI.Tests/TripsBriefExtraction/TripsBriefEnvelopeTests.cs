using Shouldly;
using Travel.Trips.Evaluation;
using Xunit;

namespace Travel.AI.Tests.TripsBriefExtraction;

[Trait("Category", "Integration")]
public sealed class TripsBriefEnvelopeTests
{
    [Fact]
    public async Task Real_worker_and_ledger_share_kernel_caps_and_keep_durable_fake_evidence()
    {
        var root = Root();
        await TripsBriefEvaluationEnvelope.PrepareAsync(
            root,
            TestContext.Current.CancellationToken
        );
        var evidence = Path.Combine(
            Path.GetTempPath(),
            "trips-envelope-source",
            Guid.NewGuid().ToString("N")
        );
        var probe = await TripsBriefEvaluationEnvelope.ProbeAsync(
            root,
            evidence,
            false,
            TestContext.Current.CancellationToken
        );
        probe.LedgerLoopbackOnly.ShouldBeTrue();
        probe.BudgetVerified.ShouldBeTrue();
        probe.SharedCgroup.ShouldBeTrue();
        probe.RealProviderCalls.ShouldBe(0);
        probe.FakeSdkCalls.ShouldBe(1);
        probe.LedgerRows.ShouldBe(1);
        probe.WritableBytes.ShouldBeLessThanOrEqualTo(256L * 1024 * 1024);
        probe.ArchiveBytes.ShouldBe(64L * 1024 * 1024);
        probe.CleanupVerified.ShouldBeTrue();
        probe.QuotaRejectionsVerified.ShouldBeTrue();
        var read =
            Travel.Tests.AiEvals.TripsBriefExtraction.TripsBriefEvaluationArchiveJournal.Read(
                probe.ArchivePath
            );
        read.Receipt.ShouldNotBeNull();
        read.Receipt.Passed.ShouldBeFalse();
        read.Receipt.Cases.Length.ShouldBe(48);
        read.IndexSha256.ShouldBe(probe.IndexSha256);
    }

    [Fact]
    public async Task Worker_exit_keeps_uncertain_intent_and_does_not_retry_or_remove_claim()
    {
        var root = Root();
        await TripsBriefEvaluationEnvelope.PrepareAsync(
            root,
            TestContext.Current.CancellationToken
        );
        var evidence = Path.Combine(
            Path.GetTempPath(),
            "trips-envelope-crash",
            Guid.NewGuid().ToString("N")
        );
        var probe = await TripsBriefEvaluationEnvelope.ProbeAsync(
            root,
            evidence,
            true,
            TestContext.Current.CancellationToken
        );
        probe.RealProviderCalls.ShouldBe(0);
        probe.FakeSdkCalls.ShouldBe(0);
        probe.CleanupVerified.ShouldBeTrue();
        Travel
            .Tests.AiEvals.TripsBriefExtraction.TripsBriefEvaluationArchiveJournal.Read(
                probe.ArchivePath
            )
            .UncertainCaseIds.ShouldBe(["D01"]);
    }

    private static string Root([System.Runtime.CompilerServices.CallerFilePath] string file = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "../../.."));
}
