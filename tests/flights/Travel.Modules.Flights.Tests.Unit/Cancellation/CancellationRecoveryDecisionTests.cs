using Shouldly;
using Travel.Modules.Flights.Core.Cancellation;
using Travel.Modules.Flights.Core.DomainEvents;
using Travel.Modules.Flights.Core.Providers.Dtos;

namespace Travel.Modules.Flights.Tests.Unit.Cancellation;

public sealed class CancellationRecoveryDecisionTests
{
    private static readonly DateTimeOffset Now = CancellationTestData.Now;

    [Fact]
    public void Old_preparation_deadline_cannot_close_later_consent_admission()
    {
        var booking = CancellationDecisionTests.Prepared();
        CancellationDecisionTests.Apply(booking, CancellationDecisionTests.Consent(booking));
        var op = booking.CurrentCancellation!;
        var result = booking.DecideCancellationAdmissionDeadline(
            op.Id,
            op.PreparationAdmissionId,
            CancellationUnknownStage.Preparation,
            Now.AddSeconds(400)
        );
        result.Kind.ShouldBe(CancellationDecisionKind.NoOp);
        booking.CurrentCancellation!.Phase.ShouldBe(CancellationPhase.Accepted);
    }

    [Fact]
    public void Late_confirmation_admission_deadline_is_locally_not_dispatched_not_supplier_rejection()
    {
        var booking = CancellationDecisionTests.Prepared();
        CancellationDecisionTests.Apply(booking, CancellationDecisionTests.Consent(booking));
        var op = booking.CurrentCancellation!;
        var result = booking.DecideCancellationAdmissionDeadline(
            op.Id,
            op.ConfirmationAdmissionId!.Value,
            CancellationUnknownStage.Confirmation,
            Now.AddSeconds(310)
        );
        CancellationDecisionTests.Apply(booking, result);
        booking.CurrentCancellation!.Phase.ShouldBe(CancellationPhase.ManualReviewRequired);
        booking.CurrentCancellation!.Outcome.ShouldBe(CancellationOutcome.None);
    }

    [Fact]
    public void Recovery_deadline_preserves_unknown_barrier_without_another_supplier_dispatch()
    {
        var booking = Dispatched();
        var op = booking.CurrentCancellation!;
        CancellationDecisionTests.Apply(
            booking,
            booking.DecideCancellationRecoveryDeadline(
                op.Id,
                op.Recovery!.Epoch,
                Now.AddSeconds(310)
            )
        );
        booking.CurrentCancellation!.Phase.ShouldBe(CancellationPhase.ManualReviewRequired);
        booking.CurrentCancellation!.Outcome.ShouldBe(CancellationOutcome.Unknown);
    }

    [Fact]
    public void Read_reservation_is_saved_once_before_observation_and_budget_does_not_reset()
    {
        var booking = Dispatched();
        var op = booking.CurrentCancellation!;
        var read = Guid.NewGuid();
        var reserve = booking.DecideCancellationObserve(
            op.Id,
            op.Revision,
            op.Recovery!.Epoch,
            0,
            read,
            Now.AddSeconds(2)
        );
        CancellationDecisionTests.Apply(booking, reserve);
        op = booking.CurrentCancellation!;
        op.Recovery!.ConsumedMask.ShouldBe(1);
        op.ActiveReadId.ShouldBe(read);
        booking
            .DecideCancellationObserve(
                op.Id,
                op.Revision,
                op.Recovery.Epoch,
                0,
                Guid.NewGuid(),
                Now.AddSeconds(3)
            )
            .Kind.ShouldBe(CancellationDecisionKind.NoOp);
    }

    [Fact]
    public void Matching_late_observation_finalizes_manual_result_but_pending_cannot_erase_success()
    {
        var booking = Dispatched();
        var op = booking.CurrentCancellation!;
        var read = Guid.NewGuid();
        CancellationDecisionTests.Apply(
            booking,
            booking.DecideCancellationObserve(
                op.Id,
                op.Revision,
                op.Recovery!.Epoch,
                0,
                read,
                Now.AddSeconds(2)
            )
        );
        op = booking.CurrentCancellation!;
        CancellationDecisionTests.Apply(
            booking,
            booking.DecideCancellationRecoveryDeadline(
                op.Id,
                op.Recovery!.Epoch,
                Now.AddSeconds(310)
            )
        );
        op = booking.CurrentCancellation!;
        var quote = new CancellationQuoteFacts(
            op.ProviderOrderRef,
            op.Terms!.ProviderCancellationRef,
            op.Terms.Refund,
            op.Terms.Destination,
            op.Terms.Settlement,
            op.Terms.ExpiresAt,
            Now,
            op.ItineraryPartyHash
        );
        var observation = new CancellationObservation(
            op.ProviderOrderRef,
            op.Terms.ProviderCancellationRef,
            CancellationObservationState.Confirmed,
            quote,
            Now.AddSeconds(1),
            Now.AddSeconds(311),
            CancellationResolutionSource.SupplierApi
        );
        CancellationDecisionTests.Apply(
            booking,
            booking.DecideCancellationObservation(
                op.Id,
                op.Revision,
                read,
                observation,
                Now.AddSeconds(311)
            )
        );
        op = booking.CurrentCancellation!;
        op.Outcome.ShouldBe(CancellationOutcome.Succeeded);
        booking
            .DecideCancellationObservation(
                op.Id,
                op.Revision,
                Guid.NewGuid(),
                observation with
                {
                    State = CancellationObservationState.Pending,
                    ConfirmedAt = null,
                },
                Now.AddSeconds(312)
            )
            .Kind.ShouldBe(CancellationDecisionKind.NoOp);
    }

    [Fact]
    public void Refresh_has_server_cooldown_and_exact_duplicate_does_not_repeat_read_work()
    {
        var booking = Dispatched();
        var op = booking.CurrentCancellation!;
        var request = Guid.NewGuid();
        var fingerprint = new string('f', 64);
        CancellationDecisionTests.Apply(
            booking,
            booking.DecideCancellationRefresh(
                CancellationTestData.Input().OwnerId,
                op.Id,
                op.Revision,
                request,
                fingerprint,
                Now
            )
        );
        op = booking.CurrentCancellation!;
        booking
            .DecideCancellationRefresh(
                CancellationTestData.Input().OwnerId,
                op.Id,
                op.Revision,
                request,
                fingerprint,
                Now.AddSeconds(1)
            )
            .Kind.ShouldBe(CancellationDecisionKind.NoOp);
        booking
            .DecideCancellationRefresh(
                CancellationTestData.Input().OwnerId,
                op.Id,
                op.Revision,
                Guid.NewGuid(),
                fingerprint,
                Now.AddSeconds(1)
            )
            .Kind.ShouldBe(CancellationDecisionKind.Rejected);
        booking.NextOwnerRefreshAt.ShouldBe(Now.AddSeconds(60));
    }

    [Fact]
    public void Duplicate_on_demand_envelope_does_not_read_again_after_completion()
    {
        var booking = Dispatched();
        var op = booking.CurrentCancellation!;
        var request = Guid.NewGuid();
        CancellationDecisionTests.Apply(
            booking,
            booking.DecideCancellationRefresh(
                CancellationTestData.Input().OwnerId,
                op.Id,
                op.Revision,
                request,
                new string('f', 64),
                Now
            )
        );
        op = booking.CurrentCancellation!;
        CancellationDecisionTests.Apply(
            booking,
            booking.DecideCancellationObserve(
                op.Id,
                op.Revision,
                op.Recovery!.Epoch,
                -1,
                request,
                Now
            )
        );
        op = booking.CurrentCancellation!;
        var observation = new CancellationObservation(
            op.ProviderOrderRef,
            op.ProviderCancellationRef,
            CancellationObservationState.Pending,
            null,
            null,
            Now.AddSeconds(1),
            CancellationResolutionSource.SupplierApi
        );
        CancellationDecisionTests.Apply(
            booking,
            booking.DecideCancellationObservation(
                op.Id,
                op.Revision,
                request,
                observation,
                Now.AddSeconds(1)
            )
        );
        op = booking.CurrentCancellation!;
        booking
            .DecideCancellationObserve(
                op.Id,
                op.Revision,
                op.Recovery!.Epoch,
                -1,
                request,
                Now.AddSeconds(2)
            )
            .Kind.ShouldBe(CancellationDecisionKind.NoOp);
    }

    [Fact]
    public void Old_preparation_pending_reply_cannot_weaken_later_accepted_consent()
    {
        var booking = CancellationDecisionTests.Prepared();
        CancellationDecisionTests.Apply(booking, CancellationDecisionTests.Consent(booking));
        var op = booking.CurrentCancellation!;
        var observation = new CancellationObservation(
            op.ProviderOrderRef,
            op.ProviderCancellationRef,
            CancellationObservationState.Pending,
            null,
            null,
            Now.AddSeconds(1),
            CancellationResolutionSource.SupplierApi
        );
        var decision = booking.DecideCancellationObservation(
            op.Id,
            op.Revision,
            Guid.NewGuid(),
            observation,
            Now.AddSeconds(1)
        );
        decision.Events.Any(e => e is CancellationOutcomeBecameUnknown).ShouldBeFalse();
        if (decision.Kind == CancellationDecisionKind.Allowed)
            CancellationDecisionTests.Apply(booking, decision);
        booking.CurrentCancellation!.Phase.ShouldBe(CancellationPhase.Accepted);
    }

    [Fact]
    public void Claimed_preparation_can_request_read_recovery_without_another_create()
    {
        var booking = CancellationDecisionTests.Held();
        var input = CancellationTestData.Input();
        var admission = Guid.NewGuid();
        CancellationDecisionTests.Apply(
            booking,
            booking.DecideCancellationPrepare(
                input.OwnerId,
                input.OperationId,
                booking.Version,
                new string('c', 64),
                admission,
                Now,
                input.ItineraryPartyHash
            )
        );
        var op = booking.CurrentCancellation!;
        CancellationDecisionTests.Apply(
            booking,
            booking.DecideCancellationPreparationDispatch(
                op.Id,
                op.Revision,
                admission,
                Guid.NewGuid(),
                Guid.NewGuid(),
                Now
            )
        );
        op = booking.CurrentCancellation!;
        booking
            .DecideCancellationRefresh(
                input.OwnerId,
                op.Id,
                op.Revision,
                Guid.NewGuid(),
                new string('f', 64),
                Now
            )
            .Kind.ShouldBe(CancellationDecisionKind.Allowed);
    }

    internal static Travel.Modules.Flights.Core.Aggregates.BookingAggregate Dispatched()
    {
        var booking = CancellationDecisionTests.Prepared();
        CancellationDecisionTests.Apply(booking, CancellationDecisionTests.Consent(booking));
        var op = booking.CurrentCancellation!;
        CancellationDecisionTests.Apply(
            booking,
            booking.DecideCancellationConfirmationDispatch(
                op.Id,
                op.Revision,
                op.ConfirmationAdmissionId!.Value,
                Guid.NewGuid(),
                Guid.NewGuid(),
                Now
            )
        );
        return booking;
    }
}
