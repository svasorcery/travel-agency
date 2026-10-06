using Travel.Modules.Flights.Core.Cancellation;
using Travel.Modules.Flights.Core.DomainEvents;
using Travel.Modules.Flights.Core.Providers.Dtos;

namespace Travel.Modules.Flights.Core.Aggregates;

public sealed partial class BookingAggregate
{
    public CancellationDecision DecideCancellationTermsUnavailable(
        Guid id,
        long revision,
        bool completed,
        string? providerCancellationRef,
        CancellationReason reason,
        DateTimeOffset now
    )
    {
        if (!cancellationOperations.TryGetValue(id, out var op))
            return RejectCancellation(CancellationReason.MissingTerms);
        if (op.IsTerminal || op.PreparationCompleted)
            return NoOpCancellation(id);
        if (
            !CancellationCurrent(op, revision)
            || op.PreparationDispatchedAt is null
            || op.AcceptedAt is not null
            || !Enum.IsDefined(reason)
            || reason == CancellationReason.None
            || (
                completed
                && (
                    providerCancellationRef is null
                    || !CancellationTerms.ValidReference(providerCancellationRef)
                )
            )
        )
            return RejectCancellation(CancellationReason.InconsistentEvidence);
        return AllowCancellation(
            id,
            new CancellationTermsWereUnavailable(
                id,
                completed,
                providerCancellationRef,
                reason,
                now
            )
        );
    }

    public CancellationDecision DecideCancellationRefusal(
        Guid id,
        long revision,
        CancellationUnknownStage stage,
        bool definitive,
        CancellationReason reason,
        DateTimeOffset now
    )
    {
        if (!cancellationOperations.TryGetValue(id, out var op))
            return RejectCancellation(CancellationReason.MissingTerms);
        if (op.IsTerminal || op.Phase == CancellationPhase.ManualReviewRequired)
            return NoOpCancellation(id);
        var claimed = stage switch
        {
            CancellationUnknownStage.Preparation => op.PreparationDispatchedAt is not null
                && !op.PreparationCompleted
                && op.ConfirmationDispatchedAt is null,
            CancellationUnknownStage.Confirmation => op.ConfirmationDispatchedAt is not null,
            _ => false,
        };
        if (
            !CancellationCurrent(op, revision)
            || !claimed
            || !Enum.IsDefined(reason)
            || reason == CancellationReason.None
        )
            return RejectCancellation(CancellationReason.InconsistentEvidence);
        if (!definitive)
            return DecideCancellationUnknown(id, revision, stage, reason, now);
        if (
            stage == CancellationUnknownStage.Confirmation
                ? op.HadConfirmationUnknown
                : op.HadPreparationUnknown
        )
            return AllowCancellation(
                id,
                new CancellationManualReviewRequired(id, stage, reason, false, now)
            );
        return AllowCancellation(
            id,
            new CancellationRejected(
                id,
                stage,
                CancellationResolutionSource.SupplierApi,
                reason,
                now
            )
        );
    }

    public CancellationDecision DecideCancellationReviewExpiry(
        Guid id,
        long revision,
        DateTimeOffset now
    )
    {
        if (!cancellationOperations.TryGetValue(id, out var op) || op.IsTerminal)
            return NoOpCancellation(id);
        if (!CancellationCurrent(op, revision))
            return RejectCancellation(CancellationReason.StaleProposal);
        if (
            op.Phase != CancellationPhase.TermsReady
            || !op.PreparationCompleted
            || op.AcceptedAt is not null
            || op.ConfirmationDispatchedAt is not null
            || op.Terms is not { } terms
            || terms.ExpiresAt > now
        )
            return NoOpCancellation(id);
        return AllowCancellation(id, new CancellationReviewExpired(id, now));
    }

    public CancellationDecision DecideCancellationNotDispatched(
        Guid id,
        long revision,
        CancellationUnknownStage stage,
        CancellationReason reason,
        DateTimeOffset now
    )
    {
        if (!cancellationOperations.TryGetValue(id, out var op) || op.IsTerminal)
            return NoOpCancellation(id);
        if (!CancellationCurrent(op, revision))
            return RejectCancellation(CancellationReason.StaleProposal);
        var unclaimed = stage switch
        {
            CancellationUnknownStage.Preparation => op.Phase == CancellationPhase.Preparing
                && op.PreparationDispatchedAt is null,
            CancellationUnknownStage.Confirmation => op.Phase == CancellationPhase.Accepted
                && op.ConfirmationDispatchedAt is null,
            _ => false,
        };
        if (!unclaimed || !Enum.IsDefined(reason) || reason == CancellationReason.None)
            return NoOpCancellation(id);
        return AllowCancellation(
            id,
            new CancellationManualReviewRequired(id, stage, reason, true, now)
        );
    }
}
