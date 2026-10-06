using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Travel.Modules.Flights.Core.Cancellation;
using Travel.Modules.Flights.Core.DomainEvents;
using Travel.Modules.Flights.Core.Providers.Dtos;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Shared.Abstractions;

namespace Travel.Modules.Flights.Core.Aggregates;

public sealed partial class BookingAggregate
{
    private readonly Dictionary<Guid, BookingOperationReviewRecorded> manualResolutionReceipts =
        new();

    public IReadOnlyCollection<BookingOperationReviewRecorded> ManualReviewHistory =>
        manualResolutionReceipts.Values;

    public void Apply(BookingOperationReviewRecorded e) =>
        manualResolutionReceipts.Add(e.ResolutionId, e);

    // The operator identity is supplied by the separately authorized Application/API boundary.
    public CancellationDecision DecideManualResolution(
        Guid actor,
        ManualResolutionInput input,
        DateTimeOffset now
    )
    {
        if (
            actor == Guid.Empty
            || !HasConsistentMutationOwner
            || OwnerUserId is null
            || OwnerUserId == Guid.Empty
            || input is null
            || input.AggregateId != Id
            || input.TargetId == Guid.Empty
            || input.ResolutionId == Guid.Empty
            || input.ExpectedRevision <= 0
            || !Enum.IsDefined(input.TargetKind)
            || input.TargetKind == ManualResolutionTargetKind.Unknown
            || input.Evidence is not { } evidence
            || !EvidenceReference(evidence.EvidenceRef)
            || evidence.ObservedAt > now
            || evidence.ObservedAt == default
            || !Enum.IsDefined(input.Decision)
            || input.Decision == ManualResolutionDecisionKind.Unknown
            || !Enum.IsDefined(evidence.Category)
            || evidence.Category == ManualEvidenceCategory.Unknown
        )
            return RejectCancellation(CancellationReason.InconsistentEvidence);

        if (!ValidManualEvidenceShape(input))
            return RejectCancellation(CancellationReason.InconsistentEvidence);
        var payload = ManualPayloadHash(input);
        if (manualResolutionReceipts.TryGetValue(input.ResolutionId, out var prior))
            return
                prior.ActorId == actor
                && prior.TargetId == input.TargetId
                && prior.TargetKind == input.TargetKind
                && prior.PayloadHash == payload
                ? NoOpCancellation(
                    input.TargetKind == ManualResolutionTargetKind.Cancellation
                        ? input.TargetId
                        : null
                )
                : RejectCancellation(CancellationReason.InconsistentEvidence);

        if (!ValidManualEvidenceBinding(input))
            return RejectCancellation(CancellationReason.InconsistentEvidence);

        if (input.TargetKind == ManualResolutionTargetKind.LegacyHeld)
        {
            if (
                input.TargetId != Id
                || input.ExpectedRevision != Version
                || Status != BookingStatus.Held
                || MutationCoordinationEnabled
            )
                return RejectCancellation(CancellationReason.StaleProposal);
            if (input.Decision == ManualResolutionDecisionKind.RecordInconclusive)
                return AppendManualAudit(
                    actor,
                    input,
                    payload,
                    evidence,
                    CancellationResolutionSource.OperatorVerified,
                    AllowCancellation(Id),
                    now
                );
            if (
                input.TargetId != Id
                || input.ExpectedRevision != Version
                || Status != BookingStatus.Held
                || MutationCoordinationEnabled
                || input.Decision != ManualResolutionDecisionKind.EnableLegacyCoordination
                || !evidence.LegacyMutationFleetDrained
                || !evidence.SupplierFinalNoEffectsConfirmed
                || !evidence.WalletFinalNoEffectsConfirmed
                || !evidence.SenderEgressIsolated
                || !evidence.OldInstancesCannotResume
                || evidence.Category != ManualEvidenceCategory.QuiescenceAndNoEffectsAttestation
                || evidence.StoppedAt is not { } stopped
                || stopped > evidence.ObservedAt
                || now - evidence.ObservedAt > TimeSpan.FromMinutes(15)
                || !EvidenceReference(evidence.QuiescenceRef)
            )
                return RejectCancellation(CancellationReason.InconsistentEvidence);
            return AppendManualAudit(
                actor,
                input,
                payload,
                evidence,
                CancellationResolutionSource.OperatorVerified,
                AllowCancellation(Id, new BookingMutationCoordinationEnabled(now)),
                now
            );
        }
        if (input.TargetKind == ManualResolutionTargetKind.Confirmation)
        {
            if (
                !confirmationAttempts.TryGetValue(input.TargetId, out var attempt)
                || !ConfirmationCurrent(attempt, input.ExpectedRevision)
                || attempt.IsTerminal
            )
                return RejectCancellation(CancellationReason.StaleProposal);
            CancellationDecision decision;
            if (input.Decision == ManualResolutionDecisionKind.RecordInconclusive)
                decision = AllowCancellation(attempt.Id);
            else if (
                input.Decision == ManualResolutionDecisionKind.CloseNotDispatched
                && attempt.EffectsClaimedAt is null
            )
                decision = AllowCancellation(
                    attempt.Id,
                    new ConfirmationAttemptClosedWithoutEffects(
                        attempt.Id,
                        CancellationReason.None,
                        now
                    )
                );
            else if (
                input.Decision == ManualResolutionDecisionKind.ConfirmNoEffect
                && attempt.DispatchOwnerInstanceId is { } sender
                && attempt.CapturedAt is null
                && attempt.SupplierPaymentEvidence is null
                && evidence.WalletFinalNoEffectsConfirmed
                && evidence.ProviderOrderRef == attempt.ProviderOrderRef
                && ValidQuiescence(
                    evidence,
                    sender,
                    attempt.EffectsClaimedAt ?? attempt.AdmittedAt,
                    now
                )
            )
                decision = AllowCancellation(
                    attempt.Id,
                    new ConfirmationAttemptClosedWithoutEffects(
                        attempt.Id,
                        CancellationReason.None,
                        now
                    )
                );
            else if (
                input.Decision == ManualResolutionDecisionKind.ConfirmBooking
                && attempt.DispatchOwnerInstanceId is { } bookingSender
                && attempt.EffectsClaimedAt is { } claimedAt
                && attempt.PaymentReference is { } savedPayment
                && !savedPayment.IsEmpty
                && evidence.Category == ManualEvidenceCategory.PaymentAndSupplierAttestation
                && evidence.PaymentReference == savedPayment
                && evidence.AcceptedMoney == attempt.AcceptedMoney
                && evidence.ProviderOrderRef == attempt.ProviderOrderRef
                && evidence.WalletCaptureConfirmed
                && evidence.SupplierBookingPaymentConfirmed
                && evidence.PaymentCorrelationAttested
                && ValidSenderStop(evidence, bookingSender, claimedAt, now)
            )
            {
                var receipt = SupplierPaymentEvidence.Create(
                    evidence.SupplierReceiptRef ?? "",
                    attempt.AcceptedMoney!,
                    SupplierPaymentKind.Balance
                );
                if (
                    receipt.IsError
                    || (
                        attempt.SupplierPaymentEvidence is { } savedReceipt
                        && savedReceipt != receipt.Value
                    )
                )
                    return RejectCancellation(CancellationReason.InconsistentEvidence);
                List<IDomainEvent> events =
                [
                    new ConfirmationAttemptCompleted(
                        attempt.Id,
                        savedPayment,
                        attempt.AcceptedMoney!,
                        attempt.ProviderOrderRef,
                        receipt.Value,
                        CancellationResolutionSource.OperatorVerified,
                        now
                    ),
                ];
                if (PaymentRef != savedPayment)
                    events.Add(
                        new PaymentAuthorized(
                            savedPayment,
                            attempt.AcceptedMoney!,
                            attempt.CapturedAt ?? evidence.ObservedAt
                        )
                    );
                if (Status == BookingStatus.Held)
                    events.Add(new OrderConfirmed(attempt.ProviderOrderRef, savedPayment, now));
                decision = AllowCancellation(attempt.Id, events.ToArray());
            }
            else
                return RejectCancellation(CancellationReason.InconsistentEvidence);
            return AppendManualAudit(
                actor,
                input,
                payload,
                evidence,
                CancellationResolutionSource.OperatorVerified,
                decision,
                now
            );
        }
        if (
            input.TargetKind != ManualResolutionTargetKind.Cancellation
            || !cancellationOperations.TryGetValue(input.TargetId, out var op)
            || !CancellationCurrent(op, input.ExpectedRevision)
        )
            return RejectCancellation(CancellationReason.StaleProposal);

        var source = CancellationResolutionSource.OperatorVerified;
        CancellationDecision result;
        switch (input.Decision)
        {
            case ManualResolutionDecisionKind.RecordInconclusive:
                if (op.IsTerminal)
                    return RejectCancellation(CancellationReason.StaleProposal);
                result = AllowCancellation(op.Id);
                break;
            case ManualResolutionDecisionKind.CloseNotDispatched:
                if (
                    op.IsTerminal
                    || (
                        op.AcceptedAt is null
                            ? op.PreparationDispatchedAt is not null
                            : op.ConfirmationDispatchedAt is not null
                    )
                )
                    return RejectCancellation(CancellationReason.InconsistentEvidence);
                result = AllowCancellation(op.Id, new CancellationReviewWasAbandoned(op.Id, now));
                break;
            case ManualResolutionDecisionKind.ConfirmPreparedTerms:
                if (
                    op.IsTerminal
                    || op.PreparationDispatchedAt is null
                    || op.PreparationCompleted
                    || op.AcceptedAt is not null
                    || !evidence.PreparationCorrelationAttested
                    || evidence.Category != ManualEvidenceCategory.SupplierSupportAttestation
                    || evidence.ProviderOrderRef != op.ProviderOrderRef
                    || evidence.ItineraryPartyHash != op.ItineraryPartyHash
                    || evidence.ObservedAt < op.PreparationDispatchedAt
                    || evidence.Settlement is not { } settlement
                )
                    return RejectCancellation(CancellationReason.InconsistentEvidence);
                var restoredTerms = CancellationTerms.Create(
                    new CancellationTermsInput(
                        1,
                        Id,
                        op.OwnerId,
                        op.Id,
                        op.ProviderOrderRef,
                        evidence.ProviderCancellationRef ?? "",
                        op.ItineraryPartyHash,
                        evidence.Refund,
                        evidence.Destination ?? CancellationRefundDestination.Unknown,
                        settlement with
                        {
                            Provenance = CancellationResolutionSource.OperatorVerified,
                        },
                        evidence.TermsExpiresAt,
                        "cancellation-v1"
                    ),
                    now
                );
                if (restoredTerms.IsError)
                    return RejectCancellation(CancellationReason.InconsistentEvidence);
                result = DecideCancellationTerms(op.Id, op.Revision, restoredTerms.Value, now);
                if (result.Kind != CancellationDecisionKind.Allowed)
                    return result;
                break;
            case ManualResolutionDecisionKind.ConfirmCancellation:
                if (
                    op.Terms is null
                    || op.AcceptedAt is null
                    || op.ConfirmationDispatchedAt is null
                )
                    return RejectCancellation(CancellationReason.InconsistentEvidence);
                CancellationConfirmationFacts facts;
                if (evidence.Category == ManualEvidenceCategory.StoredSupplierObservation)
                {
                    if (
                        !evidence.EvidenceRef.StartsWith("API-", StringComparison.Ordinal)
                        || !Guid.TryParseExact(evidence.EvidenceRef.AsSpan(4), "N", out var readId)
                        || !cancellationObservations.TryGetValue(readId, out var saved)
                        || saved.OperationId != op.Id
                        || saved.Observation.State != CancellationObservationState.Confirmed
                        || saved.Observation.Quote is not { } quote
                    )
                        return RejectCancellation(CancellationReason.InconsistentEvidence);
                    facts = new(
                        saved.Observation.ProviderOrderRef,
                        saved.Observation.ProviderCancellationRef ?? "",
                        quote.ItineraryPartyHash ?? "",
                        quote.Refund,
                        quote.Destination,
                        quote.Settlement,
                        saved.Observation.ConfirmedAt,
                        saved.Observation.ObservedAt,
                        CancellationResolutionSource.SupplierApi
                    );
                    source = CancellationResolutionSource.SupplierApi;
                }
                else if (evidence.Category == ManualEvidenceCategory.SupplierSupportAttestation)
                    facts = new(
                        evidence.ProviderOrderRef ?? "",
                        evidence.ProviderCancellationRef ?? "",
                        evidence.ItineraryPartyHash ?? "",
                        evidence.Refund,
                        evidence.Destination ?? CancellationRefundDestination.Unknown,
                        evidence.Settlement!,
                        evidence.ConfirmedAt,
                        evidence.ObservedAt,
                        CancellationResolutionSource.OperatorVerified
                    );
                else
                    return RejectCancellation(CancellationReason.InconsistentEvidence);

                var proof =
                    source == CancellationResolutionSource.SupplierApi
                        ? CancellationEvidence.FromSupplierConfirmation(op.Terms, facts, now)
                        : CancellationEvidence.FromOperatorConfirmation(op.Terms, facts, now);
                if (proof.IsError)
                    return RejectCancellation(CancellationReason.InconsistentEvidence);
                result = DecideCancellationSuccess(op.Id, op.Revision, proof.Value, now);
                if (result.Kind != CancellationDecisionKind.Allowed)
                    return result;
                break;
            case ManualResolutionDecisionKind.ConfirmNoEffect:
                if (
                    op.IsTerminal
                    || op.DispatchOwnerInstanceId is null
                    || evidence.ProviderOrderRef != op.ProviderOrderRef
                    || (
                        op.ProviderCancellationRef is not null
                        && evidence.ProviderCancellationRef != op.ProviderCancellationRef
                    )
                    || !ValidQuiescence(
                        evidence,
                        op.DispatchOwnerInstanceId.Value,
                        op.ConfirmationDispatchedAt ?? op.PreparationDispatchedAt ?? op.AdmittedAt,
                        now
                    )
                )
                    return RejectCancellation(CancellationReason.InconsistentEvidence);
                result = AllowCancellation(
                    op.Id,
                    new CancellationRejected(
                        op.Id,
                        op.UnknownStage,
                        CancellationResolutionSource.OperatorVerified,
                        CancellationReason.None,
                        now
                    )
                );
                break;
            default:
                return RejectCancellation(CancellationReason.InconsistentEvidence);
        }

        var audit = new BookingOperationReviewRecorded(
            input.TargetKind,
            input.TargetId,
            input.ResolutionId,
            actor,
            payload,
            input.Decision,
            CopyManualEvidence(evidence),
            source,
            now
        );
        return AllowCancellation(
            op.Id,
            new IDomainEvent[] { audit }
                .Concat(result.Events)
                .ToArray()
        );
    }

    private static CancellationDecision AppendManualAudit(
        Guid actor,
        ManualResolutionInput input,
        string payload,
        ManualResolutionEvidence evidence,
        CancellationResolutionSource source,
        CancellationDecision decision,
        DateTimeOffset now
    )
    {
        var audit = new BookingOperationReviewRecorded(
            input.TargetKind,
            input.TargetId,
            input.ResolutionId,
            actor,
            payload,
            input.Decision,
            CopyManualEvidence(evidence),
            source,
            now
        );
        return new CancellationDecision(
            CancellationDecisionKind.Allowed,
            CancellationReason.None,
            new EquatableArray<IDomainEvent>(
                new IDomainEvent[] { audit }
                    .Concat(decision.Events)
                    .ToArray()
            ),
            input.TargetKind == ManualResolutionTargetKind.Cancellation ? input.TargetId : null
        );
    }

    private static bool ValidQuiescence(
        ManualResolutionEvidence evidence,
        Guid sender,
        DateTimeOffset claimedAt,
        DateTimeOffset now
    ) =>
        evidence.Category == ManualEvidenceCategory.QuiescenceAndNoEffectsAttestation
        && evidence.SupplierFinalNoEffectsConfirmed
        && ValidSenderStop(evidence, sender, claimedAt, now);

    private static bool ValidSenderStop(
        ManualResolutionEvidence evidence,
        Guid sender,
        DateTimeOffset claimedAt,
        DateTimeOffset now
    ) =>
        evidence.SenderEgressIsolated
        && evidence.OldInstancesCannotResume
        && evidence.StoppedAt is { } stopped
        && stopped >= claimedAt
        && stopped <= evidence.ObservedAt
        && now - evidence.ObservedAt <= TimeSpan.FromMinutes(15)
        && EvidenceReference(evidence.QuiescenceRef)
        && evidence.StoppedInstanceIds is { } instances
        && instances.Contains(sender)
        && instances.Count is > 0 and <= 64
        && instances.All(id => id != Guid.Empty);

    private static bool ValidManualEvidenceShape(ManualResolutionInput input)
    {
        var e = input.Evidence;
        static bool SupplierReference(string? value) =>
            value is null
            || (
                value.Length is > 0 and <= 256
                && value.All(c =>
                    c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '_' or '-'
                )
            );
        static bool EvidenceMoney(Money? value) =>
            value is null
            || (
                value.Amount >= 0m
                && value.Currency is not null
                && !CurrencyCode.Create(value.Currency.Value).IsError
            );
        if (
            !SupplierReference(e.ProviderOrderRef)
            || !SupplierReference(e.ProviderCancellationRef)
            || !SupplierReference(e.SupplierReceiptRef)
            || !EvidenceMoney(e.Refund)
            || !EvidenceMoney(e.AcceptedMoney)
            || (e.QuiescenceRef is not null && !EvidenceReference(e.QuiescenceRef))
            || (e.ItineraryPartyHash is not null && !CancellationHash(e.ItineraryPartyHash))
            || (e.Destination is { } destination && !Enum.IsDefined(destination))
            || (
                e.Settlement is { } settlement
                && (
                    !Enum.IsDefined(settlement.Composition)
                    || !Enum.IsDefined(settlement.Provenance)
                )
            )
            || (
                e.StoppedInstanceIds is { } instances
                && (
                    instances.Count is < 1 or > 64
                    || instances.Any(id => id == Guid.Empty)
                    || instances.Distinct().Count() != instances.Count
                )
            )
            || (e.PaymentReference is { } payment && payment.IsEmpty)
            || (e.StoppedAt is { } stopped && (stopped == default || stopped > e.ObservedAt))
            || (
                e.ConfirmedAt is { } confirmed && (confirmed == default || confirmed > e.ObservedAt)
            )
            || (e.TermsExpiresAt is { } expiry && expiry == default)
        )
            return false;
        return true;
    }

    private bool ValidManualEvidenceBinding(ManualResolutionInput input)
    {
        var e = input.Evidence;
        if (input.TargetKind == ManualResolutionTargetKind.Cancellation)
        {
            if (
                !cancellationOperations.TryGetValue(input.TargetId, out var op)
                || (e.ProviderOrderRef is not null && e.ProviderOrderRef != op.ProviderOrderRef)
                || (
                    e.ItineraryPartyHash is not null
                    && e.ItineraryPartyHash != op.ItineraryPartyHash
                )
                || (
                    e.ProviderCancellationRef is not null
                    && (
                        op.ProviderCancellationRef is { } known
                            ? e.ProviderCancellationRef != known
                            : input.Decision != ManualResolutionDecisionKind.ConfirmPreparedTerms
                    )
                )
                || e.PaymentReference is not null
                || e.AcceptedMoney is not null
                || e.SupplierReceiptRef is not null
            )
                return false;
            if (
                op.Terms is { } terms
                && (
                    (e.Refund is not null && e.Refund != terms.Refund)
                    || (e.Destination is not null && e.Destination != terms.Destination)
                    || (
                        e.Settlement is not null
                        && !terms.Settlement.MatchesEconomicFacts(e.Settlement)
                    )
                    || (e.TermsExpiresAt is not null && e.TermsExpiresAt != terms.ExpiresAt)
                )
            )
                return false;
        }
        else if (input.TargetKind == ManualResolutionTargetKind.Confirmation)
        {
            if (
                !confirmationAttempts.TryGetValue(input.TargetId, out var attempt)
                || (
                    e.ProviderOrderRef is not null && e.ProviderOrderRef != attempt.ProviderOrderRef
                )
                || (e.AcceptedMoney is not null && e.AcceptedMoney != attempt.AcceptedMoney)
                || (
                    e.PaymentReference is not null && e.PaymentReference != attempt.PaymentReference
                )
                || (
                    e.SupplierReceiptRef is not null
                    && attempt.SupplierPaymentEvidence is { } receipt
                    && e.SupplierReceiptRef != receipt.Reference
                )
                || e.ProviderCancellationRef is not null
                || e.ItineraryPartyHash is not null
                || e.Refund is not null
                || e.Destination is not null
                || e.Settlement is not null
                || e.TermsExpiresAt is not null
            )
                return false;
        }
        else if (
            input.TargetKind == ManualResolutionTargetKind.LegacyHeld
            && (
                (e.ProviderOrderRef is not null && e.ProviderOrderRef != ProviderOrderId)
                || e.PaymentReference is not null
                || e.ProviderCancellationRef is not null
                || e.SupplierReceiptRef is not null
                || e.AcceptedMoney is not null
                || e.ItineraryPartyHash is not null
                || e.Refund is not null
                || e.Destination is not null
                || e.Settlement is not null
                || e.TermsExpiresAt is not null
            )
        )
            return false;
        return true;
    }

    private static bool EvidenceReference(string? value) =>
        value is { Length: >= 8 and <= 64 }
        && (value[0] is >= 'A' and <= 'Z' or >= '0' and <= '9')
        && value.All(c => c is >= 'A' and <= 'Z' or >= '0' and <= '9' or '-');

    private static ManualResolutionEvidence CopyManualEvidence(ManualResolutionEvidence evidence) =>
        evidence with
        {
            StoppedInstanceIds = evidence.StoppedInstanceIds is { } ids
                ? new EquatableArray<Guid>(ids.ToArray())
                : (EquatableArray<Guid>?)null,
        };

    private static string ManualPayloadHash(ManualResolutionInput input)
    {
        var e = input.Evidence;
        static object? MoneyValue(Money? money) =>
            money is null
                ? null
                : new
                {
                    Amount = money.Amount.ToString("G29", CultureInfo.InvariantCulture),
                    Currency = money.Currency?.Value,
                };
        var normalized = new
        {
            input.AggregateId,
            input.TargetKind,
            input.TargetId,
            input.ExpectedRevision,
            input.ResolutionId,
            input.Decision,
            e.EvidenceRef,
            e.Category,
            ObservedAt = e.ObservedAt.UtcTicks,
            e.ProviderOrderRef,
            e.ProviderCancellationRef,
            Refund = MoneyValue(e.Refund),
            e.Destination,
            e.Settlement,
            e.ItineraryPartyHash,
            ConfirmedAt = e.ConfirmedAt?.UtcTicks,
            e.QuiescenceRef,
            StoppedAt = e.StoppedAt?.UtcTicks,
            StoppedInstances = e
                .StoppedInstanceIds?.Select(id => id.ToString("N"))
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray(),
            e.SupplierFinalNoEffectsConfirmed,
            e.SenderEgressIsolated,
            e.OldInstancesCannotResume,
            e.LegacyMutationFleetDrained,
            AcceptedMoney = MoneyValue(e.AcceptedMoney),
            e.PaymentReference,
            e.SupplierReceiptRef,
            e.WalletCaptureConfirmed,
            e.SupplierBookingPaymentConfirmed,
            e.PaymentCorrelationAttested,
            e.WalletFinalNoEffectsConfirmed,
            TermsExpiresAt = e.TermsExpiresAt?.UtcTicks,
            e.PreparationCorrelationAttested,
        };
        return Convert
            .ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(normalized)))
            )
            .ToLowerInvariant();
    }
}
