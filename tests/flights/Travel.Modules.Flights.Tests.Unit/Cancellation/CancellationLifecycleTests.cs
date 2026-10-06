using Shouldly;
using Travel.Modules.Flights.Core.Aggregates;
using Travel.Modules.Flights.Core.Cancellation;
using Travel.Modules.Flights.Core.Providers.Dtos;

namespace Travel.Modules.Flights.Tests.Unit.Cancellation;

public sealed class CancellationLifecycleTests
{
    private static readonly DateTimeOffset Now = CancellationTestData.Now;

    [Fact]
    public void Completed_unsupported_quote_can_be_abandoned_but_lost_creation_remains_blocked()
    {
        var completed = ClaimedPreparation();
        var op = completed.CurrentCancellation!;
        CancellationDecisionTests.Apply(
            completed,
            completed.DecideCancellationTermsUnavailable(
                op.Id,
                op.Revision,
                true,
                "occ_fictional_unsupported",
                CancellationReason.UnsupportedFinancialTerms,
                Now
            )
        );
        op = completed.CurrentCancellation!;
        CancellationDecisionTests.Apply(
            completed,
            completed.DecideCancellationAbandon(op.OwnerId, op.Id, op.Revision, Now)
        );
        completed.CurrentCancellation!.Phase.ShouldBe(CancellationPhase.Abandoned);

        var lost = ClaimedPreparation();
        op = lost.CurrentCancellation!;
        CancellationDecisionTests.Apply(
            lost,
            lost.DecideCancellationTermsUnavailable(
                op.Id,
                op.Revision,
                false,
                null,
                CancellationReason.InvalidResponse,
                Now
            )
        );
        op = lost.CurrentCancellation!;
        lost.DecideCancellationAbandon(op.OwnerId, op.Id, op.Revision, Now)
            .Kind.ShouldBe(CancellationDecisionKind.Rejected);
        op.Outcome.ShouldBe(CancellationOutcome.Unknown);
    }

    [Fact]
    public void Initial_affirmative_rejection_closes_but_late_refusal_cannot_clear_previous_uncertainty()
    {
        var first = CancellationRecoveryDecisionTests.Dispatched();
        var op = first.CurrentCancellation!;
        CancellationDecisionTests.Apply(
            first,
            first.DecideCancellationRefusal(
                op.Id,
                op.Revision,
                CancellationUnknownStage.Confirmation,
                true,
                CancellationReason.NotCancellable,
                Now
            )
        );
        first.CurrentCancellation!.Outcome.ShouldBe(CancellationOutcome.Rejected);

        var lost = CancellationRecoveryDecisionTests.Dispatched();
        op = lost.CurrentCancellation!;
        CancellationDecisionTests.Apply(
            lost,
            lost.DecideCancellationUnknown(
                op.Id,
                op.Revision,
                CancellationUnknownStage.Confirmation,
                CancellationReason.ProviderUnavailable,
                Now
            )
        );
        op = lost.CurrentCancellation!;
        CancellationDecisionTests.Apply(
            lost,
            lost.DecideCancellationRefusal(
                op.Id,
                op.Revision,
                CancellationUnknownStage.Confirmation,
                true,
                CancellationReason.NotCancellable,
                Now
            )
        );
        lost.CurrentCancellation!.Phase.ShouldBe(CancellationPhase.ManualReviewRequired);
        lost.CurrentCancellation!.Outcome.ShouldBe(CancellationOutcome.Unknown);
    }

    [Fact]
    public void Unaccepted_review_expires_but_expired_consent_or_unknown_create_cannot_release_barrier()
    {
        var review = CancellationDecisionTests.Prepared();
        var op = review.CurrentCancellation!;
        CancellationDecisionTests.Apply(
            review,
            review.DecideCancellationReviewExpiry(op.Id, op.Revision, op.Terms!.ExpiresAt)
        );
        review.CurrentCancellation!.Phase.ShouldBe(CancellationPhase.Expired);
        CancellationDecisionTests.Consent(review).Kind.ShouldBe(CancellationDecisionKind.Rejected);

        var accepted = CancellationDecisionTests.Prepared();
        CancellationDecisionTests.Apply(accepted, CancellationDecisionTests.Consent(accepted));
        op = accepted.CurrentCancellation!;
        accepted
            .DecideCancellationReviewExpiry(op.Id, op.Revision, op.Terms!.ExpiresAt)
            .Kind.ShouldBe(CancellationDecisionKind.NoOp);
        var lost = ClaimedPreparation();
        op = lost.CurrentCancellation!;
        lost.DecideCancellationReviewExpiry(op.Id, op.Revision, Now.AddDays(1))
            .Kind.ShouldBe(CancellationDecisionKind.NoOp);
    }

    [Fact]
    public void Earlier_refresh_id_remains_bound_after_a_newer_request()
    {
        var booking = CancellationRecoveryDecisionTests.Dispatched();
        var op = booking.CurrentCancellation!;
        var first = Guid.NewGuid();
        var originalRevision = op.Revision;
        var hash = new string('a', 64);
        CancellationDecisionTests.Apply(
            booking,
            booking.DecideCancellationRefresh(op.OwnerId, op.Id, op.Revision, first, hash, Now)
        );
        op = booking.CurrentCancellation!;
        CancellationDecisionTests.Apply(
            booking,
            booking.DecideCancellationObserve(
                op.Id,
                op.Revision,
                op.Recovery!.Epoch,
                -1,
                first,
                Now
            )
        );
        op = booking.CurrentCancellation!;
        CancellationDecisionTests.Apply(
            booking,
            booking.DecideCancellationRefresh(
                op.OwnerId,
                op.Id,
                op.Revision,
                Guid.NewGuid(),
                new string('b', 64),
                Now.AddSeconds(61)
            )
        );
        op = booking.CurrentCancellation!;
        booking
            .DecideCancellationRefresh(
                op.OwnerId,
                op.Id,
                originalRevision,
                first,
                hash,
                Now.AddSeconds(62)
            )
            .Kind.ShouldBe(CancellationDecisionKind.NoOp);
        booking
            .DecideCancellationRefresh(
                op.OwnerId,
                op.Id,
                op.Revision,
                first,
                new string('c', 64),
                Now.AddSeconds(122)
            )
            .Kind.ShouldBe(CancellationDecisionKind.Rejected);
    }

    [Fact]
    public void Failed_readonly_confirmation_preflight_blocks_without_inventing_supplier_rejection()
    {
        var booking = CancellationDecisionTests.Prepared();
        CancellationDecisionTests.Apply(booking, CancellationDecisionTests.Consent(booking));
        var op = booking.CurrentCancellation!;
        CancellationDecisionTests.Apply(
            booking,
            booking.DecideCancellationNotDispatched(
                op.Id,
                op.Revision,
                CancellationUnknownStage.Confirmation,
                CancellationReason.NotCancellable,
                Now
            )
        );
        op = booking.CurrentCancellation!;
        op.Phase.ShouldBe(CancellationPhase.ManualReviewRequired);
        op.Outcome.ShouldBe(CancellationOutcome.None);
        op.ResolutionSource.ShouldBe(CancellationResolutionSource.None);
        booking
            .DecideCancellationConfirmationDispatch(
                op.Id,
                op.Revision,
                op.ConfirmationAdmissionId!.Value,
                Guid.NewGuid(),
                Guid.NewGuid(),
                Now
            )
            .Kind.ShouldBe(CancellationDecisionKind.Rejected);
    }

    [Fact]
    public void Readonly_preflight_failure_cannot_close_an_already_claimed_confirmation()
    {
        var booking = CancellationRecoveryDecisionTests.Dispatched();
        var op = booking.CurrentCancellation!;
        var decision = booking.DecideCancellationNotDispatched(
            op.Id,
            op.Revision,
            CancellationUnknownStage.Confirmation,
            CancellationReason.ProviderUnavailable,
            Now
        );
        decision.Events.ShouldBeEmpty();
        booking.CurrentCancellation!.Phase.ShouldBe(CancellationPhase.DispatchClaimed);
    }

    internal static BookingAggregate ClaimedPreparation()
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
                new string('p', 64).Replace('p', 'a'),
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
        return booking;
    }
}
