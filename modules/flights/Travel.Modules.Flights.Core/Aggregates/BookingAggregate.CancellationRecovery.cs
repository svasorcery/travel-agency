using Travel.Modules.Flights.Core.Cancellation;
using Travel.Modules.Flights.Core.DomainEvents;
using Travel.Modules.Flights.Core.Providers.Dtos;
using Travel.Shared.Abstractions;

namespace Travel.Modules.Flights.Core.Aggregates;

public sealed partial class BookingAggregate
{
    private readonly Dictionary<
        Guid,
        (Guid OperationId, CancellationObservation Observation)
    > cancellationObservations = new();
    private readonly Dictionary<Guid, (Guid OperationId, string Fingerprint)> refreshReceipts =
        new();
    public DateTimeOffset? NextOwnerRefreshAt { get; private set; }
    public SupplierOrderCancellationFact? ObservedSupplierCancellation { get; private set; }

    private readonly Dictionary<
        Guid,
        (Guid OperationId, Guid Epoch, CancellationUnknownStage Stage, DateTimeOffset StartedAt)
    > cancellationReads = new();

    public void Apply(CancellationObservationStarted e)
    {
        var operation = cancellationOperations[e.OperationId];
        if (e.ReadAllowed && e.ReadId != Guid.Empty)
            cancellationReads.Add(
                e.ReadId,
                (
                    e.OperationId,
                    e.Epoch,
                    operation.ConfirmationDispatchedAt is null
                        ? CancellationUnknownStage.Preparation
                        : CancellationUnknownStage.Confirmation,
                    e.OccurredAt
                )
            );
        UpdateCancellation(
            e.OperationId,
            op =>
                op with
                {
                    Recovery = e.Recovery,
                    ActiveReadId =
                        e.ReadAllowed && e.ReadId != Guid.Empty ? e.ReadId : op.ActiveReadId,
                    ConsumedRefreshRequestId =
                        e.Slot == -1 ? e.ReadId : op.ConsumedRefreshRequestId,
                }
        );
    }

    public void Apply(CancellationObservationRecorded e)
    {
        cancellationObservations[e.ReadId] = (e.OperationId, e.Observation);
        if (e.Observation.OrderCancellation is { } fact)
        {
            ObservedSupplierCancellation = fact;
            if (Status != BookingStatus.Refunded)
            {
                Status = BookingStatus.Cancelled;
                CancelledAt = fact.CancelledAt;
            }
        }
        UpdateCancellation(
            e.OperationId,
            op =>
                op.ActiveReadId == e.ReadId
                    ? op with
                    {
                        ActiveReadId = null,
                        Recovery = op.Recovery?.CompleteWindow(
                            op.Recovery.ReadWindowStartedAt ?? default
                        ),
                    }
                    : op
        );
    }

    public void Apply(CancellationRefreshRequested e)
    {
        refreshReceipts.Add(e.RefreshRequestId, (e.OperationId, e.Fingerprint));
        NextOwnerRefreshAt = e.NextRefreshAt;
        UpdateCancellation(
            e.OperationId,
            op =>
                op with
                {
                    LastRefreshRequestId = e.RefreshRequestId,
                    LastRefreshFingerprint = e.Fingerprint,
                }
        );
    }

    public CancellationDecision DecideCancellationAdmissionDeadline(
        Guid operationId,
        Guid admissionId,
        CancellationUnknownStage stage,
        DateTimeOffset now
    )
    {
        if (
            !cancellationOperations.TryGetValue(operationId, out var op)
            || op.IsTerminal
            || op.Phase == CancellationPhase.ManualReviewRequired
        )
            return NoOpCancellation(operationId);
        if (stage == CancellationUnknownStage.Preparation)
        {
            if (
                op.PreparationAdmissionId != admissionId
                || op.PreparationDispatchedAt is not null
                || op.Phase != CancellationPhase.Preparing
                || now < op.AdmittedAt.AddSeconds(310)
            )
                return NoOpCancellation(operationId);
        }
        else if (stage == CancellationUnknownStage.Confirmation)
        {
            if (
                op.ConfirmationAdmissionId != admissionId
                || op.ConfirmationDispatchedAt is not null
                || op.Phase != CancellationPhase.Accepted
                || op.AcceptedAt is null
                || now < op.AcceptedAt.Value.AddSeconds(310)
            )
                return NoOpCancellation(operationId);
        }
        else
            return NoOpCancellation(operationId);
        return AllowCancellation(
            operationId,
            new CancellationManualReviewRequired(
                operationId,
                stage,
                CancellationReason.ManualVerificationRequired,
                true,
                now
            )
        );
    }

    public CancellationDecision DecideCancellationRecoveryDeadline(
        Guid operationId,
        Guid epoch,
        DateTimeOffset now
    )
    {
        if (
            !cancellationOperations.TryGetValue(operationId, out var op)
            || op.IsTerminal
            || op.Phase == CancellationPhase.ManualReviewRequired
            || op.Recovery is not { } recovery
            || recovery.Epoch != epoch
            || now < recovery.Deadline
        )
            return NoOpCancellation(operationId);
        if (op.ConfirmationDispatchedAt is null && op.PreparationCompleted)
            return NoOpCancellation(operationId);
        return AllowCancellation(
            operationId,
            new CancellationManualReviewRequired(
                operationId,
                op.ConfirmationDispatchedAt is null
                    ? CancellationUnknownStage.Preparation
                    : CancellationUnknownStage.Confirmation,
                CancellationReason.ManualVerificationRequired,
                false,
                now
            )
        );
    }

    public CancellationDecision DecideCancellationObserve(
        Guid operationId,
        long revision,
        Guid epoch,
        int slot,
        Guid readId,
        DateTimeOffset now
    )
    {
        if (!cancellationOperations.TryGetValue(operationId, out var op))
            return RejectCancellation(CancellationReason.IdentityMismatch);
        if (
            op.IsTerminal
            || op.Phase is CancellationPhase.TermsReady or CancellationPhase.UnsupportedTerms
        )
            return NoOpCancellation(operationId);
        if (
            !CancellationCurrent(op, revision)
            || readId == Guid.Empty
            || op.Recovery is not { } recovery
            || recovery.Epoch != epoch
        )
            return RejectCancellation(CancellationReason.StaleProposal);

        if (slot == -1)
        {
            if (op.LastRefreshRequestId != readId || op.ConsumedRefreshRequestId == readId)
                return NoOpCancellation(operationId);
            if (op.ActiveReadId == readId)
                return NoOpCancellation(operationId);
            if (recovery.ReadWindowUntil > now)
                return AllowCancellation(
                    operationId,
                    new CancellationObservationStarted(
                        operationId,
                        epoch,
                        slot,
                        readId,
                        recovery,
                        now,
                        false
                    )
                );
            return AllowCancellation(
                operationId,
                new CancellationObservationStarted(
                    operationId,
                    epoch,
                    slot,
                    readId,
                    recovery.OpenSharedWindow(now),
                    now
                )
            );
        }
        if (op.Phase == CancellationPhase.ManualReviewRequired)
            return NoOpCancellation(operationId);
        var reservation = recovery.Reserve(slot, now);
        return reservation.Kind switch
        {
            RecoveryReservationKind.Read => AllowCancellation(
                operationId,
                new CancellationObservationStarted(
                    operationId,
                    epoch,
                    slot,
                    readId,
                    reservation.UpdatedSchedule,
                    now
                )
            ),
            RecoveryReservationKind.Skipped => AllowCancellation(
                operationId,
                new CancellationObservationStarted(
                    operationId,
                    epoch,
                    slot,
                    Guid.Empty,
                    reservation.UpdatedSchedule,
                    now
                )
            ),
            RecoveryReservationKind.Manual => DecideCancellationRecoveryDeadline(
                operationId,
                epoch,
                now
            ),
            _ => NoOpCancellation(operationId),
        };
    }

    public CancellationDecision DecideCancellationExternalFact(
        Guid operationId,
        long revision,
        SupplierOrderCancellationFact fact,
        DateTimeOffset now
    )
    {
        if (!cancellationOperations.TryGetValue(operationId, out var op))
            return RejectCancellation(CancellationReason.IdentityMismatch);
        if (ObservedSupplierCancellation == fact)
            return NoOpCancellation(operationId);
        if (op.IsTerminal)
            return NoOpCancellation(operationId);
        if (
            !CancellationCurrent(op, revision)
            || fact.ProviderOrderRef != op.ProviderOrderRef
            || fact.Source != CancellationResolutionSource.SupplierApi
            || fact.CancelledAt == default
            || fact.CancelledAt > fact.ObservedAt
            || fact.ObservedAt > now
        )
            return RejectCancellation(CancellationReason.InconsistentEvidence);
        return AllowCancellation(
            operationId,
            new CancellationManualReviewRequired(
                operationId,
                op.ConfirmationAdmissionId is null
                    ? CancellationUnknownStage.Preparation
                    : CancellationUnknownStage.Confirmation,
                CancellationReason.InconsistentEvidence,
                op.ConfirmationAdmissionId is null
                    ? op.PreparationDispatchedAt is null
                    : op.ConfirmationDispatchedAt is null,
                now,
                fact
            )
        );
    }

    public CancellationDecision DecideCancellationObservation(
        Guid operationId,
        long revision,
        Guid readId,
        CancellationObservation observation,
        DateTimeOffset now
    )
    {
        if (!cancellationOperations.TryGetValue(operationId, out var op))
            return RejectCancellation(CancellationReason.IdentityMismatch);
        if (cancellationObservations.TryGetValue(readId, out var prior))
            return prior.OperationId == operationId && prior.Observation == observation
                ? NoOpCancellation(operationId)
                : RejectCancellation(CancellationReason.InconsistentEvidence);
        if (op.IsTerminal)
            return NoOpCancellation(operationId);
        if (
            !cancellationReads.TryGetValue(readId, out var binding)
            || binding.OperationId != operationId
        )
            return RejectCancellation(CancellationReason.InconsistentEvidence);
        if (
            !CancellationCurrent(op, revision)
            || readId == Guid.Empty
            || observation is null
            || observation.Source != CancellationResolutionSource.SupplierApi
            || observation.ObservedAt > now
            || observation.ObservedAt == default
            || observation.ObservedAt < binding.StartedAt
            || !Enum.IsDefined(observation.State)
            || observation.ProviderOrderRef != op.ProviderOrderRef
        )
            return RejectCancellation(CancellationReason.InconsistentEvidence);

        if (
            observation.OrderCancellation is { } orderFact
            && (
                orderFact.ProviderOrderRef != op.ProviderOrderRef
                || orderFact.Source != CancellationResolutionSource.SupplierApi
                || orderFact.CancelledAt == default
                || orderFact.CancelledAt > orderFact.ObservedAt
                || orderFact.ObservedAt > observation.ObservedAt
                || orderFact.ObservedAt > now
            )
        )
            return RejectCancellation(CancellationReason.InconsistentEvidence);
        var recorded = new CancellationObservationRecorded(operationId, readId, observation, now);
        if (
            observation.State == CancellationObservationState.Confirmed
            && op.Terms is { } terms
            && observation.Quote is { } quote
            && op.AcceptedAt is not null
            && op.ConfirmationDispatchedAt is not null
        )
        {
            var facts = new CancellationConfirmationFacts(
                observation.ProviderOrderRef,
                observation.ProviderCancellationRef ?? "",
                quote.ItineraryPartyHash ?? "",
                quote.Refund,
                quote.Destination,
                quote.Settlement,
                observation.ConfirmedAt,
                observation.ObservedAt,
                observation.Source
            );
            var evidence = CancellationEvidence.FromSupplierConfirmation(terms, facts, now);
            if (!evidence.IsError)
            {
                var success = DecideCancellationSuccess(operationId, revision, evidence.Value, now);
                if (success.Kind == CancellationDecisionKind.Allowed)
                    return AllowCancellation(
                        operationId,
                        new IDomainEvent[] { recorded }
                            .Concat(success.Events)
                            .ToArray()
                    );
            }
            return AllowCancellation(
                operationId,
                recorded,
                new CancellationManualReviewRequired(
                    operationId,
                    CancellationUnknownStage.Confirmation,
                    CancellationReason.InconsistentEvidence,
                    false,
                    now
                )
            );
        }

        if (observation.OrderCancellation is not null)
            return AllowCancellation(
                operationId,
                recorded,
                new CancellationManualReviewRequired(
                    operationId,
                    op.ConfirmationDispatchedAt is null
                        ? CancellationUnknownStage.Preparation
                        : CancellationUnknownStage.Confirmation,
                    CancellationReason.Uncorrelated,
                    false,
                    now
                )
            );
        if (
            binding.Epoch != op.Recovery?.Epoch
            || (
                binding.Stage == CancellationUnknownStage.Preparation
                && op.ConfirmationDispatchedAt is not null
            )
        )
            return AllowCancellation(operationId, recorded);
        if (
            op.PreparationCompleted
            && op.ConfirmationDispatchedAt is null
            && observation.State
                is CancellationObservationState.Pending
                    or CancellationObservationState.Unknown
        )
            return AllowCancellation(operationId, recorded);
        if (op.Phase == CancellationPhase.ManualReviewRequired)
            return AllowCancellation(operationId, recorded);
        var stage = op.ConfirmationDispatchedAt is null
            ? CancellationUnknownStage.Preparation
            : CancellationUnknownStage.Confirmation;
        if (
            op.ProviderCancellationRef is null
            || observation.State == CancellationObservationState.Confirmed
        )
            return AllowCancellation(
                operationId,
                recorded,
                new CancellationManualReviewRequired(
                    operationId,
                    stage,
                    CancellationReason.Uncorrelated,
                    false,
                    now
                )
            );
        return AllowCancellation(
            operationId,
            recorded,
            new CancellationOutcomeBecameUnknown(
                operationId,
                stage,
                observation.Reason == CancellationReason.None
                    ? CancellationReason.Pending
                    : observation.Reason,
                now
            )
        );
    }

    public CancellationDecision DecideCancellationRefresh(
        Guid owner,
        Guid operationId,
        long revision,
        Guid requestId,
        string fingerprint,
        DateTimeOffset now
    )
    {
        if (!CancellationOwner(owner))
            return RejectCancellation(CancellationReason.IdentityMismatch);
        if (!cancellationOperations.TryGetValue(operationId, out var op))
            return RejectCancellation(CancellationReason.IdentityMismatch);
        if (refreshReceipts.TryGetValue(requestId, out var prior))
            return prior.OperationId == operationId && prior.Fingerprint == fingerprint
                ? NoOpCancellation(operationId)
                : RejectCancellation(CancellationReason.IdentityMismatch);
        if (
            !CancellationCurrent(op, revision)
            || requestId == Guid.Empty
            || !CancellationHash(fingerprint)
        )
            return RejectCancellation(CancellationReason.StaleProposal);
        if (
            op.IsTerminal
            || op.Phase is CancellationPhase.TermsReady or CancellationPhase.UnsupportedTerms
            || (op.Phase == CancellationPhase.Preparing && op.PreparationDispatchedAt is null)
            || (op.Phase == CancellationPhase.Accepted && op.ConfirmationDispatchedAt is null)
            || op.Recovery is null
        )
            return NoOpCancellation(operationId);
        if (NextOwnerRefreshAt > now)
            return RejectCancellation(CancellationReason.RefreshTooSoon);
        if (
            op.LastRefreshRequestId is not null
            && op.LastRefreshRequestId != op.ConsumedRefreshRequestId
        )
            return NoOpCancellation(operationId);
        if (op.Recovery.ReadWindowUntil > now)
            return NoOpCancellation(operationId);
        return AllowCancellation(
            operationId,
            new CancellationRefreshRequested(
                operationId,
                requestId,
                fingerprint,
                now.AddSeconds(60),
                now
            )
        );
    }
}
