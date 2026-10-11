using Shouldly;
using Travel.Modules.Flights.Core.Cancellation;
using Travel.Modules.Flights.Core.DomainEvents;
using Travel.Modules.Flights.Core.Refunds;

namespace Travel.Modules.Flights.Tests.Unit.Refunds;

public sealed class SupplierCancellationReportTests
{
    private static readonly Guid Report = Guid.Parse("00000000-0000-0000-0000-000000000201");
    private static readonly Guid Actor = Guid.Parse("00000000-0000-0000-0000-000000000202");

    [Fact]
    public void Signed_report_retains_only_a_report_and_no_customer_money()
    {
        var b = RefundTestBookings.Held();
        var d = b.DecideSupplierCancellationReport(
            Report,
            RefundTestBookings.Order,
            RefundTestBookings.Now
        );
        d.Kind.ShouldBe(CancellationDecisionKind.Allowed);
        d.Events.Count.ShouldBe(1);
        foreach (var e in d.Events)
            RefundTestBookings.Apply(b, e);
        b.HasSupplierCancellationReportBarrier.ShouldBeTrue();
        b.Status.ShouldBe(Core.Aggregates.BookingStatus.Held);
        b.RefundedAt.ShouldBeNull();
        b.DecideSupplierCancellationReport(Report, RefundTestBookings.Order, RefundTestBookings.Now)
            .Events.Count.ShouldBe(0);
    }

    [Fact]
    public void Report_blocks_a_live_confirmation_but_does_not_declare_the_sender_stopped()
    {
        var b = RefundTestBookings.Held();
        var admission = Guid.NewGuid();
        RefundTestBookings.Apply(
            b,
            new ConfirmationAttemptStarted(
                RefundTestBookings.Confirmation,
                RefundTestBookings.Owner,
                admission,
                new string('c', 64),
                RefundTestBookings.Order,
                RefundTestBookings.Money(100m),
                RefundTestBookings.Now
            )
        );
        RefundTestBookings.Apply(
            b,
            new ConfirmationEffectsClaimed(
                RefundTestBookings.Confirmation,
                admission,
                RefundTestBookings.Sender,
                RefundTestBookings.Now
            )
        );
        Reported(b);
        b.CanContinueConfirmation(RefundTestBookings.Confirmation, RefundTestBookings.Sender)
            .ShouldBeFalse();
        b.HasConfirmationBarrier.ShouldBeTrue();
    }

    [Fact]
    public void Report_without_cancellation_operation_can_be_reviewed_without_inventing_money()
    {
        var b = RefundTestBookings.Held();
        RefundTestBookings.Apply(
            b,
            new OrderConfirmed(
                RefundTestBookings.Order,
                RefundTestBookings.Payment,
                RefundTestBookings.Now
            )
        );
        Reported(b);
        var input = Input(
            b,
            ManualResolutionDecisionKind.ConfirmCancellation,
            new(
                Report,
                RefundTestBookings.Order,
                true,
                false,
                false,
                RefundTestBookings.Now.AddSeconds(-1)
            )
        );
        Apply(b, b.DecideManualResolution(Actor, input, RefundTestBookings.Now.AddSeconds(1)));
        b.HasSupplierCancellationReportBarrier.ShouldBeFalse();
        b.Status.ShouldBe(Core.Aggregates.BookingStatus.Cancelled);
        b.CancelledAt.ShouldBe(RefundTestBookings.Now.AddSeconds(-1));
        b.CurrentCancellation.ShouldBeNull();
        b.CustomerRefund.ShouldBeNull();
        b.RefundedAt.ShouldBeNull();
    }

    [Fact]
    public void Negative_review_needs_positive_no_pending_facts_and_preserves_competing_attempts()
    {
        var b = RefundTestBookings.Held();
        RefundTestBookings.Apply(
            b,
            new ConfirmationAttemptStarted(
                RefundTestBookings.Confirmation,
                RefundTestBookings.Owner,
                Guid.NewGuid(),
                new string('c', 64),
                RefundTestBookings.Order,
                RefundTestBookings.Money(100m),
                RefundTestBookings.Now
            )
        );
        Reported(b);
        var bad = Input(
            b,
            ManualResolutionDecisionKind.ConfirmNotCancelled,
            new(Report, RefundTestBookings.Order, false, true, false, null)
        );
        b.DecideManualResolution(Actor, bad, RefundTestBookings.Now.AddSeconds(1))
            .Kind.ShouldBe(CancellationDecisionKind.Rejected);
        var good = bad with
        {
            ResolutionId = Guid.NewGuid(),
            Evidence = bad.Evidence with
            {
                ReportEvidence = bad.Evidence.ReportEvidence! with
                {
                    NoPendingCancellationConfirmed = true,
                },
            },
        };
        Apply(b, b.DecideManualResolution(Actor, good, RefundTestBookings.Now.AddSeconds(1)));
        b.HasSupplierCancellationReportBarrier.ShouldBeFalse();
        b.HasConfirmationBarrier.ShouldBeTrue();
    }

    [Fact]
    public void Historical_report_resolution_replay_does_not_resolve_a_new_report()
    {
        var b = RefundTestBookings.Held();
        Reported(b);
        var input = Input(
            b,
            ManualResolutionDecisionKind.ConfirmNotCancelled,
            new(Report, RefundTestBookings.Order, false, true, true, null)
        );
        Apply(b, b.DecideManualResolution(Actor, input, RefundTestBookings.Now.AddSeconds(1)));
        var next = b.DecideSupplierCancellationReport(
            Guid.NewGuid(),
            RefundTestBookings.Order,
            RefundTestBookings.Now.AddSeconds(2)
        );
        Apply(b, next);
        b.DecideManualResolution(Actor, input, RefundTestBookings.Now.AddSeconds(3))
            .Kind.ShouldBe(CancellationDecisionKind.NoOp);
        b.HasSupplierCancellationReportBarrier.ShouldBeTrue();
    }

    [Fact]
    public void Unresolved_report_blocks_new_cancellation_admission()
    {
        var b = RefundTestBookings.Held();
        Reported(b);
        b.DecideCancellationPrepare(
                RefundTestBookings.Owner,
                Guid.NewGuid(),
                b.Version,
                new string('c', 64),
                Guid.NewGuid(),
                RefundTestBookings.Now,
                new string('a', 64)
            )
            .Kind.ShouldBe(CancellationDecisionKind.Rejected);
        b.DecideCancel().ShouldBeOfType<Core.Aggregates.BookingTransitionDecision.Rejected>();
    }

    private static void Reported(Core.Aggregates.BookingAggregate b) =>
        RefundTestBookings.Apply(
            b,
            new SupplierCancellationReported(
                Report,
                RefundTestBookings.Owner,
                RefundTestBookings.Order,
                RefundTestBookings.Now
            )
        );

    private static ManualResolutionInput Input(
        Core.Aggregates.BookingAggregate b,
        ManualResolutionDecisionKind decision,
        SupplierCancellationReportEvidence proof
    ) =>
        new(
            RefundTestBookings.Booking,
            ManualResolutionTargetKind.SupplierCancellationReport,
            Report,
            b.CurrentSupplierCancellationReport!.Revision,
            Guid.NewGuid(),
            decision,
            new(
                "EVIDENCE-FICTIONAL-REPORT",
                ManualEvidenceCategory.SupplierSupportAttestation,
                RefundTestBookings.Now,
                ReportEvidence: proof
            )
        );

    private static void Apply(Core.Aggregates.BookingAggregate b, CancellationDecision d)
    {
        d.Kind.ShouldBe(CancellationDecisionKind.Allowed);
        foreach (var e in d.Events)
            RefundTestBookings.Apply(b, e);
    }
}
