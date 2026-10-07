using Travel.Modules.Flights.Core.Booking;
using Travel.Modules.Flights.Core.Cancellation;
using Travel.Modules.Flights.Core.DomainEvents;
using Travel.Modules.Flights.Core.Providers.Dtos;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Travel.Shared.Abstractions;

namespace Travel.Modules.Flights.Core.Aggregates;

public sealed partial class BookingAggregate
{
    private readonly Dictionary<Guid, ConfirmationAttempt> confirmationAttempts = new();
    public IReadOnlyDictionary<Guid, ConfirmationAttempt> ConfirmationAttempts =>
        new System.Collections.ObjectModel.ReadOnlyDictionary<Guid, ConfirmationAttempt>(
            confirmationAttempts
        );
    public Guid? CurrentConfirmationAttemptId { get; private set; }
    public ConfirmationAttempt? CurrentConfirmationAttempt =>
        CurrentConfirmationAttemptId is { } id
        && confirmationAttempts.TryGetValue(id, out var attempt)
            ? attempt
            : null;
    public bool HasConfirmationBarrier => CurrentConfirmationAttempt is { IsTerminal: false };

    public void Apply(ConfirmationAttemptStarted e)
    {
        confirmationAttempts.Add(
            e.AttemptId,
            new ConfirmationAttempt
            {
                Id = e.AttemptId,
                OwnerId = e.OwnerId,
                Revision = 1,
                AdmissionId = e.AdmissionId,
                Fingerprint = e.Fingerprint,
                ProviderOrderRef = e.ProviderOrderRef,
                AcceptedMoney = e.AcceptedMoney,
                ExpectedPurchase = CurrentCreation?.Accepted is { HasServices: true } purchase
                    ? purchase
                    : null,
                AdmittedAt = e.OccurredAt,
                Phase = ConfirmationAttemptPhase.Started,
            }
        );
        CurrentConfirmationAttemptId = e.AttemptId;
    }

    public void Apply(ConfirmationEffectsClaimed e) =>
        UpdateConfirmation(
            e.AttemptId,
            attempt =>
                attempt with
                {
                    Phase = ConfirmationAttemptPhase.EffectsClaimed,
                    EffectsClaimedAt = e.OccurredAt,
                    DispatchOwnerInstanceId = e.DispatchOwnerInstanceId,
                }
        );

    public void Apply(ConfirmationPaymentReferenceRecorded e) =>
        UpdateConfirmation(
            e.AttemptId,
            attempt => attempt with { PaymentReference = e.PaymentReference }
        );

    public void Apply(ConfirmationCaptureObserved e) =>
        UpdateConfirmation(
            e.AttemptId,
            attempt =>
                attempt with
                {
                    PaymentReference = e.PaymentReference,
                    CapturedAt = e.OccurredAt,
                }
        );

    public void Apply(ConfirmationAttemptCompleted e) =>
        UpdateConfirmation(
            e.AttemptId,
            attempt =>
                attempt with
                {
                    Phase = ConfirmationAttemptPhase.Completed,
                    CompletedAt = e.OccurredAt,
                    PaymentReference = e.PaymentReference,
                    SupplierPaymentEvidence = e.SupplierPaymentEvidence,
                    ServiceProof = e.ServiceProof,
                    ResolutionSource = e.Source,
                    Reason = CancellationReason.None,
                }
        );

    public void Apply(ConfirmationAttemptClosedWithoutEffects e) =>
        UpdateConfirmation(
            e.AttemptId,
            attempt =>
                attempt with
                {
                    Phase = ConfirmationAttemptPhase.NotDispatched,
                    Reason = e.Reason,
                }
        );

    public void Apply(ConfirmationAttemptRequiredManualReview e) =>
        UpdateConfirmation(
            e.AttemptId,
            attempt =>
                attempt with
                {
                    Phase = ConfirmationAttemptPhase.ManualReviewRequired,
                    Reason = e.Reason,
                }
        );

    private void UpdateConfirmation(Guid id, Func<ConfirmationAttempt, ConfirmationAttempt> update)
    {
        var prior = confirmationAttempts[id];
        confirmationAttempts[id] = update(prior) with { Revision = prior.Revision + 1 };
    }

    public CancellationDecision DecideConfirmationStart(
        Guid owner,
        Guid attemptId,
        Guid admissionId,
        long bookingVersion,
        string fingerprint,
        DateTimeOffset now
    )
    {
        if (!CancellationOwner(owner))
            return RejectCancellation(CancellationReason.IdentityMismatch);
        if (attemptId == Guid.Empty || admissionId == Guid.Empty || !CancellationHash(fingerprint))
            return RejectCancellation(CancellationReason.InvalidResponse);
        if (confirmationAttempts.TryGetValue(attemptId, out var old))
            return old.OwnerId == owner && old.Fingerprint == fingerprint
                ? NoOpCancellation()
                : RejectCancellation(CancellationReason.IdentityMismatch);
        if (Version != bookingVersion)
            return RejectCancellation(CancellationReason.StaleProposal);
        if (
            Status != BookingStatus.Held
            || !MutationCoordinationEnabled
            || TotalAmount is null
            || string.IsNullOrWhiteSpace(ProviderOrderId)
        )
            return RejectCancellation(CancellationReason.ManualVerificationRequired);
        if (ExpiresAt <= now)
            return RejectCancellation(CancellationReason.TermsExpired);
        if (HasConfirmationBarrier || CurrentCancellation is { IsTerminal: false })
            return RejectCancellation(CancellationReason.Pending);
        if (RecoverySchedule.Create(attemptId, now).IsError)
            return RejectCancellation(CancellationReason.InvalidResponse);
        return AllowCancellation(
            attemptId,
            new ConfirmationAttemptStarted(
                attemptId,
                owner,
                admissionId,
                fingerprint,
                ProviderOrderId,
                TotalAmount,
                now
            )
        );
    }

    public CancellationDecision DecideConfirmationEffectsClaim(
        Guid attemptId,
        long revision,
        Guid admissionId,
        Guid sender,
        DateTimeOffset now
    )
    {
        if (!confirmationAttempts.TryGetValue(attemptId, out var attempt))
            return RejectCancellation(CancellationReason.IdentityMismatch);
        if (attempt.EffectsClaimedAt is not null)
            return NoOpCancellation();
        if (
            !ConfirmationCurrent(attempt, revision)
            || attempt.Phase != ConfirmationAttemptPhase.Started
            || attempt.AdmissionId != admissionId
            || sender == Guid.Empty
            || Status != BookingStatus.Held
            || CurrentCancellation is { IsTerminal: false }
        )
            return RejectCancellation(CancellationReason.Pending);
        if (ExpiresAt <= now)
            return RejectCancellation(CancellationReason.TermsExpired);
        if (now >= attempt.AdmittedAt.AddSeconds(310))
            return DecideConfirmationDeadline(attemptId, admissionId, now);
        return AllowCancellation(
            attemptId,
            new ConfirmationEffectsClaimed(attemptId, admissionId, sender, now)
        );
    }

    public CancellationDecision DecideConfirmationPaymentReference(
        Guid attemptId,
        long revision,
        Guid sender,
        PaymentRef payment,
        DateTimeOffset now
    )
    {
        if (!confirmationAttempts.TryGetValue(attemptId, out var attempt))
            return RejectCancellation(CancellationReason.IdentityMismatch);
        if (attempt.PaymentReference is { } prior)
            return prior == payment
                ? NoOpCancellation()
                : RejectCancellation(CancellationReason.InconsistentEvidence);
        if (
            !ConfirmationCurrent(attempt, revision)
            || attempt.EffectsClaimedAt is null
            || attempt.IsTerminal
            || attempt.DispatchOwnerInstanceId != sender
            || payment.IsEmpty
        )
            return RejectCancellation(CancellationReason.InconsistentEvidence);
        return AllowCancellation(
            attemptId,
            new ConfirmationPaymentReferenceRecorded(attemptId, payment, now)
        );
    }

    public CancellationDecision DecideConfirmationCapture(
        Guid attemptId,
        long revision,
        Guid sender,
        PaymentRef payment,
        Money amount,
        DateTimeOffset now
    )
    {
        if (!confirmationAttempts.TryGetValue(attemptId, out var attempt))
            return RejectCancellation(CancellationReason.IdentityMismatch);
        if (attempt.CapturedAt is not null)
            return attempt.PaymentReference == payment && attempt.AcceptedMoney == amount
                ? NoOpCancellation()
                : RejectCancellation(CancellationReason.InconsistentEvidence);
        if (
            !ConfirmationCurrent(attempt, revision)
            || attempt.IsTerminal
            || attempt.EffectsClaimedAt is null
            || attempt.DispatchOwnerInstanceId != sender
            || payment.IsEmpty
            || attempt.PaymentReference != payment
            || attempt.AcceptedMoney != amount
        )
            return RejectCancellation(CancellationReason.InconsistentEvidence);
        return AllowCancellation(
            attemptId,
            new ConfirmationCaptureObserved(attemptId, payment, amount, now)
        );
    }

    public CancellationDecision DecideConfirmationComplete(
        Guid attemptId,
        long revision,
        PaymentRef payment,
        string providerOrderRef,
        SupplierPaymentEvidence evidence,
        CancellationResolutionSource source,
        DateTimeOffset now,
        BookingServiceProof? serviceProof = null
    )
    {
        if (!confirmationAttempts.TryGetValue(attemptId, out var attempt))
            return RejectCancellation(CancellationReason.IdentityMismatch);
        if (attempt.Phase == ConfirmationAttemptPhase.Completed)
            return NoOpCancellation();
        if (
            !ConfirmationCurrent(attempt, revision)
            || attempt.IsTerminal
            || attempt.CapturedAt is null
            || attempt.EffectsClaimedAt is null
            || attempt.PaymentReference != payment
            || payment.IsEmpty
            || attempt.ProviderOrderRef != providerOrderRef
            || evidence is null
            || evidence.Amount != attempt.AcceptedMoney
            || evidence.Kind != SupplierPaymentKind.Balance
            || (
                attempt.ExpectedPurchase is { } expected
                && (
                    serviceProof is null
                    || !serviceProof.Matches(expected, providerOrderRef)
                    || CreationBlocksConfirmation
                )
            )
            || source
                is not (
                    CancellationResolutionSource.SupplierApi
                    or CancellationResolutionSource.OperatorVerified
                )
        )
            return RejectCancellation(CancellationReason.InconsistentEvidence);
        List<IDomainEvent> events =
        [
            new ConfirmationAttemptCompleted(
                attemptId,
                payment,
                attempt.AcceptedMoney!,
                providerOrderRef,
                evidence,
                source,
                now,
                serviceProof
            ),
        ];
        if (PaymentRef != payment)
            events.Add(
                new PaymentAuthorized(payment, attempt.AcceptedMoney!, attempt.CapturedAt.Value)
            );
        if (Status == BookingStatus.Held)
            events.Add(new OrderConfirmed(providerOrderRef, payment, now));
        return AllowCancellation(attemptId, events.ToArray());
    }

    public CancellationDecision DecideConfirmationDeadline(
        Guid attemptId,
        Guid admissionId,
        DateTimeOffset now
    )
    {
        if (
            !confirmationAttempts.TryGetValue(attemptId, out var attempt)
            || attempt.AdmissionId != admissionId
            || attempt.IsTerminal
            || attempt.Phase == ConfirmationAttemptPhase.ManualReviewRequired
            || now < attempt.AdmittedAt.AddSeconds(310)
        )
            return NoOpCancellation();
        return attempt.EffectsClaimedAt is null
            ? AllowCancellation(
                attemptId,
                new ConfirmationAttemptClosedWithoutEffects(attemptId, CancellationReason.None, now)
            )
            : AllowCancellation(
                attemptId,
                new ConfirmationAttemptRequiredManualReview(
                    attemptId,
                    CancellationReason.ManualVerificationRequired,
                    now
                )
            );
    }

    private bool ConfirmationCurrent(ConfirmationAttempt attempt, long revision) =>
        CurrentConfirmationAttemptId == attempt.Id && attempt.Revision == revision;

    public bool CanContinueConfirmation(Guid attemptId, Guid sender) =>
        HasConsistentMutationOwner
        && CurrentConfirmationAttempt is { } attempt
        && attempt.Id == attemptId
        && attempt.DispatchOwnerInstanceId == sender
        && sender != Guid.Empty
        && attempt.Phase == ConfirmationAttemptPhase.EffectsClaimed
        && Status == BookingStatus.Held
        && !CreationBlocksConfirmation
        && CurrentCancellation is not { IsTerminal: false };

    public CancellationDecision DecideConfirmationNotDispatched(
        Guid attemptId,
        long revision,
        CancellationReason reason,
        DateTimeOffset now
    )
    {
        if (!confirmationAttempts.TryGetValue(attemptId, out var attempt) || attempt.IsTerminal)
            return NoOpCancellation();
        if (
            !ConfirmationCurrent(attempt, revision)
            || attempt.EffectsClaimedAt is not null
            || !Enum.IsDefined(reason)
            || reason == CancellationReason.None
        )
            return NoOpCancellation();
        return AllowCancellation(
            attemptId,
            new ConfirmationAttemptClosedWithoutEffects(attemptId, reason, now)
        );
    }

    public CancellationDecision DecideConfirmationManual(
        Guid attemptId,
        long revision,
        CancellationReason reason,
        DateTimeOffset now
    )
    {
        if (
            !confirmationAttempts.TryGetValue(attemptId, out var attempt)
            || attempt.IsTerminal
            || attempt.Phase == ConfirmationAttemptPhase.ManualReviewRequired
        )
            return NoOpCancellation();
        if (
            !ConfirmationCurrent(attempt, revision)
            || attempt.EffectsClaimedAt is null
            || !Enum.IsDefined(reason)
            || reason == CancellationReason.None
        )
            return NoOpCancellation();
        return AllowCancellation(
            attemptId,
            new ConfirmationAttemptRequiredManualReview(attemptId, reason, now)
        );
    }
}
