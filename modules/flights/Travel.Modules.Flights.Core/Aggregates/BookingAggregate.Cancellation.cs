using Travel.Modules.Flights.Core.Cancellation;
using Travel.Modules.Flights.Core.DomainEvents;
using Travel.Modules.Flights.Core.Providers.Dtos;
using Travel.Shared.Abstractions;

namespace Travel.Modules.Flights.Core.Aggregates;

public sealed partial class BookingAggregate
{
    private readonly Dictionary<Guid, CancellationOperation> cancellationOperations = new();
    public IReadOnlyDictionary<Guid, CancellationOperation> CancellationOperations =>
        new System.Collections.ObjectModel.ReadOnlyDictionary<Guid, CancellationOperation>(
            cancellationOperations
        );
    public Guid? CurrentCancellationId { get; private set; }
    public bool MutationCoordinationEnabled { get; private set; }
    public bool HasConsistentMutationOwner =>
        OwnerUserId is { } owner
        && owner != Guid.Empty
        && cancellationOperations.Values.All(operation => operation.OwnerId == owner)
        && confirmationAttempts.Values.All(attempt => attempt.OwnerId == owner);
    public CancellationOperation? CurrentCancellation =>
        CurrentCancellationId is { } id && cancellationOperations.TryGetValue(id, out var operation)
            ? operation
            : null;

    public void Apply(BookingMutationCoordinationEnabled e) => MutationCoordinationEnabled = true;

    public void Apply(CancellationPreparationStarted e)
    {
        cancellationOperations.Add(
            e.OperationId,
            new CancellationOperation
            {
                Id = e.OperationId,
                OwnerId = e.OwnerId,
                Revision = 1,
                ProviderOrderRef = e.ProviderOrderRef,
                ItineraryPartyHash = e.ItineraryPartyHash,
                PrepareFingerprint = e.Fingerprint,
                PreparationAdmissionId = e.AdmissionId,
                AdmittedAt = e.OccurredAt,
                Phase = CancellationPhase.Preparing,
            }
        );
        CurrentCancellationId = e.OperationId;
    }

    public void Apply(CancellationPreparationDispatched e) =>
        UpdateCancellation(
            e.OperationId,
            op =>
                op with
                {
                    PreparationDispatchedAt = e.OccurredAt,
                    DispatchOwnerInstanceId = e.DispatchOwnerInstanceId,
                    Recovery = e.Recovery,
                }
        );

    public void Apply(CancellationTermsObtained e) =>
        UpdateCancellation(
            e.OperationId,
            op =>
                op with
                {
                    Terms = e.Terms,
                    ProviderCancellationRef = e.Terms.ProviderCancellationRef,
                    PreparationCompleted = true,
                    Phase = CancellationPhase.TermsReady,
                    Outcome = CancellationOutcome.None,
                    UnknownStage = CancellationUnknownStage.None,
                    Reason = CancellationReason.None,
                    ActiveReadId = null,
                    Recovery = op.Recovery?.CompleteWindow(
                        op.Recovery.ReadWindowStartedAt ?? default
                    ),
                }
        );

    public void Apply(CancellationTermsWereUnavailable e) =>
        UpdateCancellation(
            e.OperationId,
            op =>
                op with
                {
                    PreparationCompleted = e.CreationCompleted,
                    Outcome = e.CreationCompleted
                        ? CancellationOutcome.None
                        : CancellationOutcome.Unknown,
                    HadUnknown = op.HadUnknown || !e.CreationCompleted,
                    HadPreparationUnknown = op.HadPreparationUnknown || !e.CreationCompleted,
                    UnknownStage = e.CreationCompleted
                        ? CancellationUnknownStage.None
                        : CancellationUnknownStage.Preparation,
                    ProviderCancellationRef = e.ProviderCancellationRef,
                    Phase = e.CreationCompleted
                        ? CancellationPhase.UnsupportedTerms
                        : CancellationPhase.ManualReviewRequired,
                    Reason = e.Reason,
                }
        );

    public void Apply(CancellationTermsAccepted e) =>
        UpdateCancellation(
            e.OperationId,
            op =>
                op with
                {
                    Phase = CancellationPhase.Accepted,
                    AcceptedAt = e.OccurredAt,
                    ConsentFingerprint = e.Fingerprint,
                    ConfirmationAdmissionId = e.AdmissionId,
                }
        );

    public void Apply(CancellationConfirmationDispatched e) =>
        UpdateCancellation(
            e.OperationId,
            op =>
                op with
                {
                    Phase = CancellationPhase.DispatchClaimed,
                    ConfirmationDispatchedAt = e.OccurredAt,
                    DispatchOwnerInstanceId = e.DispatchOwnerInstanceId,
                    Recovery = e.Recovery,
                    ActiveReadId = null,
                }
        );

    public void Apply(CancellationOutcomeBecameUnknown e) =>
        UpdateCancellation(
            e.OperationId,
            op =>
                op with
                {
                    Phase = CancellationPhase.Unknown,
                    UnknownStage = e.Stage,
                    HadUnknown = true,
                    HadPreparationUnknown =
                        op.HadPreparationUnknown || e.Stage == CancellationUnknownStage.Preparation,
                    HadConfirmationUnknown =
                        op.HadConfirmationUnknown
                        || e.Stage == CancellationUnknownStage.Confirmation,
                    Outcome = CancellationOutcome.Unknown,
                    Reason = e.Reason,
                }
        );

    public void Apply(CancellationSucceeded e) =>
        UpdateCancellation(
            e.OperationId,
            op =>
                op with
                {
                    Phase = CancellationPhase.Succeeded,
                    Outcome = CancellationOutcome.Succeeded,
                    Evidence = e.Evidence,
                    ResolutionSource = e.Evidence.Source,
                    Reason = CancellationReason.None,
                    ActiveReadId = null,
                }
        );

    public void Apply(CancellationRejected e) =>
        UpdateCancellation(
            e.OperationId,
            op =>
                op with
                {
                    Phase = CancellationPhase.Rejected,
                    Outcome = CancellationOutcome.Rejected,
                    UnknownStage = e.Stage,
                    ResolutionSource = e.Source,
                    Reason = e.Reason,
                    ActiveReadId = null,
                }
        );

    public void Apply(CancellationManualReviewRequired e)
    {
        if (e.OrderCancellation is { } fact)
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
                op with
                {
                    Phase = CancellationPhase.ManualReviewRequired,
                    UnknownStage = e.Stage,
                    Reason = e.Reason,
                    Outcome = e.NotDispatched
                        ? CancellationOutcome.None
                        : CancellationOutcome.Unknown,
                    HadUnknown = op.HadUnknown || !e.NotDispatched,
                    HadPreparationUnknown =
                        op.HadPreparationUnknown
                        || (!e.NotDispatched && e.Stage == CancellationUnknownStage.Preparation),
                    HadConfirmationUnknown =
                        op.HadConfirmationUnknown
                        || (!e.NotDispatched && e.Stage == CancellationUnknownStage.Confirmation),
                }
        );
    }

    public void Apply(CancellationReviewWasAbandoned e) =>
        UpdateCancellation(
            e.OperationId,
            op =>
                op with
                {
                    Phase = CancellationPhase.Abandoned,
                    Outcome = CancellationOutcome.None,
                }
        );

    public void Apply(CancellationReviewExpired e) =>
        UpdateCancellation(
            e.OperationId,
            op => op with { Phase = CancellationPhase.Expired, Outcome = CancellationOutcome.None }
        );

    private void UpdateCancellation(
        Guid id,
        Func<CancellationOperation, CancellationOperation> update
    )
    {
        var prior = cancellationOperations[id];
        cancellationOperations[id] = update(prior) with { Revision = prior.Revision + 1 };
    }

    public CancellationDecision DecideCancellationPrepare(
        Guid owner,
        Guid operationId,
        long bookingVersion,
        string fingerprint,
        Guid admissionId,
        DateTimeOffset now,
        string scope
    )
    {
        if (!CancellationOwner(owner))
            return RejectCancellation(CancellationReason.IdentityMismatch);
        if (
            operationId == Guid.Empty
            || admissionId == Guid.Empty
            || !CancellationHash(fingerprint)
            || !CancellationHash(scope)
        )
            return RejectCancellation(CancellationReason.InvalidResponse);
        if (cancellationOperations.TryGetValue(operationId, out var existing))
            return existing.OwnerId == owner && existing.PrepareFingerprint == fingerprint
                ? NoOpCancellation(operationId)
                : RejectCancellation(CancellationReason.IdentityMismatch);
        if (Status is BookingStatus.Cancelled or BookingStatus.Refunded)
            return NoOpCancellation();
        if (bookingVersion != Version)
            return RejectCancellation(CancellationReason.StaleProposal);
        if (
            Status is not (BookingStatus.Held or BookingStatus.Confirmed or BookingStatus.Ticketed)
            || string.IsNullOrWhiteSpace(ProviderOrderId)
        )
            return RejectCancellation(CancellationReason.NotCancellable);
        if (Status == BookingStatus.Held && !MutationCoordinationEnabled)
            return RejectCancellation(CancellationReason.ManualVerificationRequired);
        if (HasConfirmationBarrier)
            return RejectCancellation(CancellationReason.Pending);
        if (CurrentCancellation is { IsTerminal: false })
            return RejectCancellation(CancellationReason.Pending);
        return AllowCancellation(
            operationId,
            new CancellationPreparationStarted(
                operationId,
                owner,
                ProviderOrderId,
                scope.ToLowerInvariant(),
                fingerprint,
                admissionId,
                now
            )
        );
    }

    public CancellationDecision DecideCancellationPreparationDispatch(
        Guid operationId,
        long revision,
        Guid admissionId,
        Guid sender,
        Guid epoch,
        DateTimeOffset now
    )
    {
        if (!cancellationOperations.TryGetValue(operationId, out var op))
            return RejectCancellation(CancellationReason.MissingTerms);
        if (op.PreparationDispatchedAt is not null)
            return NoOpCancellation(operationId);
        if (Status is BookingStatus.Cancelled or BookingStatus.Refunded)
            return RejectCancellation(CancellationReason.AlreadyCancelled);

        if (
            !CancellationCurrent(op, revision)
            || op.Phase != CancellationPhase.Preparing
            || op.PreparationAdmissionId != admissionId
            || sender == Guid.Empty
            || epoch == Guid.Empty
        )
            return RejectCancellation(CancellationReason.StaleProposal);
        if (now >= op.AdmittedAt.AddSeconds(310))
            return DecideCancellationAdmissionDeadline(
                operationId,
                admissionId,
                CancellationUnknownStage.Preparation,
                now
            );
        var schedule = RecoverySchedule.Create(epoch, now);
        if (schedule.IsError)
            return RejectCancellation(CancellationReason.InvalidResponse);
        return AllowCancellation(
            operationId,
            new CancellationPreparationDispatched(
                operationId,
                admissionId,
                sender,
                schedule.Value,
                now
            )
        );
    }

    public CancellationDecision DecideCancellationTerms(
        Guid operationId,
        long revision,
        CancellationTerms terms,
        DateTimeOffset now
    )
    {
        if (!cancellationOperations.TryGetValue(operationId, out var op))
            return RejectCancellation(CancellationReason.MissingTerms);
        if (op.Terms?.Hash == terms.Hash)
            return NoOpCancellation(operationId);
        if (
            !CancellationCurrent(op, revision)
            || op.IsTerminal
            || op.Phase
                is not (
                    CancellationPhase.Preparing
                    or CancellationPhase.Unknown
                    or CancellationPhase.ManualReviewRequired
                )
            || op.PreparationDispatchedAt is null
            || op.PreparationCompleted
            || op.AcceptedAt is not null
            || terms.AggregateId != Id
            || terms.OwnerId != op.OwnerId
            || terms.OperationId != op.Id
            || terms.ProviderOrderRef != op.ProviderOrderRef
            || terms.ItineraryPartyHash != op.ItineraryPartyHash
            || terms.ExpiresAt <= now
        )
            return RejectCancellation(CancellationReason.InconsistentEvidence);
        return AllowCancellation(
            operationId,
            new CancellationTermsObtained(operationId, terms, now)
        );
    }

    public CancellationDecision DecideCancellationConsent(
        Guid owner,
        Guid operationId,
        long revision,
        long termsRevision,
        string hash,
        string notice,
        string fingerprint,
        Guid admissionId,
        DateTimeOffset now
    )
    {
        if (!CancellationOwner(owner))
            return RejectCancellation(CancellationReason.IdentityMismatch);
        if (!cancellationOperations.TryGetValue(operationId, out var op))
            return RejectCancellation(CancellationReason.MissingTerms);
        if (op.ConsentFingerprint is not null)
            return op.ConsentFingerprint == fingerprint
                ? NoOpCancellation(operationId)
                : RejectCancellation(CancellationReason.IdentityMismatch);
        if (
            !CancellationCurrent(op, revision)
            || op.OwnerId != owner
            || op.Phase != CancellationPhase.TermsReady
            || op.Terms is not { } terms
            || !op.PreparationCompleted
            || admissionId == Guid.Empty
            || !CancellationHash(fingerprint)
            || terms.Revision != termsRevision
            || terms.Hash != hash
            || terms.NoticeVersion != notice
        )
            return RejectCancellation(CancellationReason.StaleProposal);
        if (Status is BookingStatus.Cancelled or BookingStatus.Refunded)
            return RejectCancellation(CancellationReason.AlreadyCancelled);
        if (terms.ExpiresAt <= now)
            return RejectCancellation(CancellationReason.TermsExpired);
        return AllowCancellation(
            operationId,
            new CancellationTermsAccepted(
                operationId,
                termsRevision,
                hash,
                notice,
                fingerprint,
                admissionId,
                now
            )
        );
    }

    public CancellationDecision DecideCancellationConfirmationDispatch(
        Guid operationId,
        long revision,
        Guid admissionId,
        Guid sender,
        Guid epoch,
        DateTimeOffset now
    )
    {
        if (!cancellationOperations.TryGetValue(operationId, out var op))
            return RejectCancellation(CancellationReason.MissingTerms);
        if (op.ConfirmationDispatchedAt is not null)
            return NoOpCancellation(operationId);
        if (
            !CancellationCurrent(op, revision)
            || op.Phase != CancellationPhase.Accepted
            || op.AcceptedAt is null
            || op.ConfirmationAdmissionId != admissionId
            || op.Terms is null
            || sender == Guid.Empty
            || epoch == Guid.Empty
        )
            return RejectCancellation(CancellationReason.StaleProposal);
        if (Status is BookingStatus.Cancelled or BookingStatus.Refunded)
            return RejectCancellation(CancellationReason.AlreadyCancelled);
        if (op.Terms.ExpiresAt <= now)
            return RejectCancellation(CancellationReason.TermsExpired);
        if (now >= op.AcceptedAt.Value.AddSeconds(310))
            return DecideCancellationAdmissionDeadline(
                operationId,
                admissionId,
                CancellationUnknownStage.Confirmation,
                now
            );
        var schedule = RecoverySchedule.Create(epoch, now);
        if (schedule.IsError)
            return RejectCancellation(CancellationReason.InvalidResponse);
        return AllowCancellation(
            operationId,
            new CancellationConfirmationDispatched(
                operationId,
                admissionId,
                sender,
                schedule.Value,
                now
            )
        );
    }

    public CancellationDecision DecideCancellationUnknown(
        Guid operationId,
        long revision,
        CancellationUnknownStage stage,
        CancellationReason reason,
        DateTimeOffset now
    )
    {
        if (!cancellationOperations.TryGetValue(operationId, out var op))
            return RejectCancellation(CancellationReason.MissingTerms);
        if (op.IsTerminal || op.Phase == CancellationPhase.ManualReviewRequired)
            return NoOpCancellation(operationId);
        if (
            !CancellationCurrent(op, revision)
            || (
                stage == CancellationUnknownStage.Preparation
                && (op.PreparationDispatchedAt is null || op.PreparationCompleted)
            )
            || (
                stage == CancellationUnknownStage.Confirmation
                && op.ConfirmationDispatchedAt is null
            )
            || stage
                is not (
                    CancellationUnknownStage.Preparation
                    or CancellationUnknownStage.Confirmation
                )
        )
            return RejectCancellation(CancellationReason.StaleProposal);
        return AllowCancellation(
            operationId,
            new CancellationOutcomeBecameUnknown(operationId, stage, reason, now)
        );
    }

    public CancellationDecision DecideCancellationSuccess(
        Guid operationId,
        long revision,
        CancellationEvidence evidence,
        DateTimeOffset now
    )
    {
        if (!cancellationOperations.TryGetValue(operationId, out var op))
            return RejectCancellation(CancellationReason.MissingTerms);
        if (op.Phase == CancellationPhase.Succeeded)
            return NoOpCancellation(operationId);
        if (
            !CancellationCurrent(op, revision)
            || op.IsTerminal
            || op.Terms is null
            || op.AcceptedAt is null
            || op.ConfirmationDispatchedAt is null
            || evidence.ConfirmedAt < op.AcceptedAt
            || evidence.ObservedAt > now
            || evidence.ProviderOrderRef != op.ProviderOrderRef
            || evidence.ProviderCancellationRef != op.Terms.ProviderCancellationRef
            || evidence.ItineraryPartyHash != op.ItineraryPartyHash
            || evidence.Refund != op.Terms.Refund
            || evidence.Destination != op.Terms.Destination
            || !op.Terms.Settlement.MatchesEconomicFacts(evidence.Settlement)
        )
            return RejectCancellation(CancellationReason.InconsistentEvidence);
        List<IDomainEvent> events = [new CancellationSucceeded(operationId, evidence, now)];
        if (Status is not (BookingStatus.Cancelled or BookingStatus.Refunded))
            events.Add(new OrderCancelled(CancelReason.User, evidence.ConfirmedAt));
        return new(
            CancellationDecisionKind.Allowed,
            CancellationReason.None,
            new(events.ToArray()),
            operationId
        );
    }

    public CancellationDecision DecideCancellationAbandon(
        Guid owner,
        Guid operationId,
        long revision,
        DateTimeOffset now
    )
    {
        if (!CancellationOwner(owner))
            return RejectCancellation(CancellationReason.IdentityMismatch);
        if (!cancellationOperations.TryGetValue(operationId, out var op))
            return RejectCancellation(CancellationReason.MissingTerms);
        if (op.Phase == CancellationPhase.Abandoned)
            return NoOpCancellation(operationId);
        if (
            !CancellationCurrent(op, revision)
            || op.OwnerId != owner
            || !op.PreparationCompleted
            || op.AcceptedAt is not null
            || op.ConfirmationDispatchedAt is not null
            || op.Phase is not (CancellationPhase.TermsReady or CancellationPhase.UnsupportedTerms)
        )
            return RejectCancellation(CancellationReason.Pending);
        return AllowCancellation(operationId, new CancellationReviewWasAbandoned(operationId, now));
    }

    private bool CancellationOwner(Guid owner) =>
        owner != Guid.Empty && OwnerUserId == owner && HasConsistentMutationOwner;

    private bool CancellationCurrent(CancellationOperation operation, long revision) =>
        CurrentCancellationId == operation.Id && operation.Revision == revision;

    private static bool CancellationHash(string value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private static CancellationDecision AllowCancellation(Guid id, params IDomainEvent[] events) =>
        new(CancellationDecisionKind.Allowed, CancellationReason.None, new(events), id);

    private static CancellationDecision NoOpCancellation(Guid? id = null) =>
        new(CancellationDecisionKind.NoOp, CancellationReason.None, new([]), id);

    private static CancellationDecision RejectCancellation(CancellationReason reason) =>
        new(CancellationDecisionKind.Rejected, reason, new([]));
}
