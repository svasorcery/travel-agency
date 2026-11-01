using Shouldly;
using Travel.Hotels.Fixture;

namespace Travel.Modules.Hotels.Tests.Integration;

public sealed class HotelLoadLedgerTests
{
    [Fact]
    public void Stop_retains_completed_cancelled_unfinished_and_never_started_jobs()
    {
        var ledger = Plan();
        ledger.Begin(1, 1);
        ledger.Complete(1, "Response", 1.2, 429);
        ledger.Begin(2, 2);
        ledger.Complete(2, "Cancelled", 2.1, null);
        ledger.Begin(3, 3);
        ledger.Seal(10);
        var result = ledger.Snapshot();
        result.Planned.ShouldBe(4);
        result.AllObserved.ShouldBeFalse();
        result.Entries.Length.ShouldBe(4);
        result.Entries[0].Terminal.ShouldBe("Response");
        result.Entries[0].Status.ShouldBe(429);
        result.Entries[1].Terminal.ShouldBe("Cancelled");
        result.Entries[2].Terminal.ShouldBe("Unfinished");
        result.Entries[3].Terminal.ShouldBe("NotRun");
        result.Entries[3].Started.ShouldBeNull();
        result.Counts.Values.Sum().ShouldBe(4);
        ledger.Complete(3, "Response", 11, 200).ShouldBeFalse();
        ledger.Begin(4, 12).ShouldBeFalse();
        ledger.Snapshot().Entries.ShouldBe(result.Entries);
    }

    [Theory]
    [InlineData(301, false, true)]
    [InlineData(310, false, false)]
    public void Recovery_uses_its_own_cutoff_without_reusing_fixed_phase_result(
        double observed,
        bool phaseExpected,
        bool windowExpected
    )
    {
        HotelsLoadLedger
            .PhasePurchaseSuccess(284.5, 285, observed, 200, true, 1, 240, 300)
            .ShouldBe(phaseExpected);
        HotelsLoadLedger
            .WindowPurchaseSuccess(284.5, 285, observed, 200, true, 1, 305)
            .ShouldBe(windowExpected);
    }

    [Theory]
    [InlineData(null, true, 1, 200)]
    [InlineData(301d, false, 1, 200)]
    [InlineData(301d, true, null, 200)]
    [InlineData(301d, true, 0, 200)]
    [InlineData(301d, true, 2, 200)]
    [InlineData(301d, true, 1, 503)]
    public void Missing_confirmation_wrong_source_or_non_single_effect_cannot_pass(
        double? observed,
        bool source,
        int? effects,
        int? status
    ) =>
        HotelsLoadLedger
            .WindowPurchaseSuccess(284.5, 285, observed, status, source, effects, 305)
            .ShouldBeFalse();

    [Fact]
    public void A_later_window_can_recover_but_the_150_second_budget_and_cohort_stay_fixed()
    {
        HotelsLoadLedger.WindowPurchaseSuccess(284.5, 285, 310, 200, true, 1, 315).ShouldBeTrue();
        HotelsLoadLedger.WindowPurchaseSuccess(315, 315, 315, 200, true, 1, 315).ShouldBeFalse();
        HotelsLoadLedger.WindowPurchaseSuccess(254, 255, 310, 200, true, 1, 315).ShouldBeFalse();
        HotelsLoadLedger.PhasePurchaseSuccess(100, 101, 251, 200, true, 1, 0, 360).ShouldBeFalse();
        HotelsLoadLedger
            .PhasePurchaseSuccess(100, 101, double.NaN, 200, true, 1, 0, 360)
            .ShouldBeFalse();
    }

    [Fact]
    public void Duplicate_or_phantom_observations_fail_instead_of_changing_the_denominator()
    {
        var ledger = Plan();
        ledger.Begin(1, 1);
        ledger.Complete(1, "Response", 1.2, 200);
        Should.Throw<InvalidOperationException>(() => ledger.Complete(1, "Response", 1.3, 200));
        Should.Throw<KeyNotFoundException>(() => ledger.Begin(99, 99));
        Should.Throw<InvalidOperationException>(() => ledger.Begin(2, double.NaN));
        Should.Throw<ArgumentException>(() =>
            new HotelsLoadLedger([new(1, "search", 0), new(1, "search", 1)])
        );
        ledger.Snapshot().Planned.ShouldBe(4);
        ledger.Snapshot().Counts["Response"].ShouldBe(1);
    }

    private static HotelsLoadLedger Plan() =>
        new([new(1, "search", 1), new(2, "search", 2), new(3, "create", 3), new(4, "status", 4)]);
}
