using System.Globalization;
using ErrorOr;
using Travel.Modules.Flights.Core.Booking;
using Travel.Modules.Flights.Core.Cancellation;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Travel.Shared.Abstractions;

namespace Travel.Modules.Flights.Api.Contracts;

public sealed record PrepareCancellationRequest(
    Guid AggregateId,
    Guid OperationId,
    long ExpectedBookingVersion
);

public sealed record ConsentCancellationRequest(
    Guid AggregateId,
    Guid OperationId,
    long ExpectedOperationRevision,
    long TermsRevision,
    string TermsHash,
    string NoticeVersion,
    bool Accepted
);

public sealed record AbandonCancellationRequest(
    Guid AggregateId,
    Guid OperationId,
    long ExpectedOperationRevision
);

public sealed record RefreshCancellationRequest(
    Guid AggregateId,
    Guid OperationId,
    long ExpectedOperationRevision,
    Guid RefreshRequestId
);

public sealed record ResolveCancellationReviewRequest(
    Guid AggregateId,
    string TargetKind,
    Guid TargetId,
    long ExpectedRevision,
    Guid ResolutionId,
    string Decision,
    CancellationReviewEvidenceRequest Evidence
)
{
    public ErrorOr<ManualResolutionInput> ToInput()
    {
        if (
            AggregateId == Guid.Empty
            || TargetId == Guid.Empty
            || ResolutionId == Guid.Empty
            || ExpectedRevision <= 0
            || !CancellationRequestValidation.EnumValue(
                TargetKind,
                out ManualResolutionTargetKind kind
            )
            || !CancellationRequestValidation.EnumValue(
                Decision,
                out ManualResolutionDecisionKind decision
            )
            || Evidence is null
        )
            return CancellationRequestValidation.Invalid;
        var evidence = Evidence.ToEvidence();
        return evidence.IsError
            ? evidence.Errors
            : new ManualResolutionInput(
                AggregateId,
                kind,
                TargetId,
                ExpectedRevision,
                ResolutionId,
                decision,
                evidence.Value
            );
    }
}

public sealed record CancellationMoneyRequest(string Amount, string Currency)
{
    internal ErrorOr<Money> ToMoney()
    {
        if (
            Amount is null
            || !System.Text.RegularExpressions.Regex.IsMatch(Amount, @"^[0-9]+(\.[0-9]+)?$")
            || !decimal.TryParse(
                Amount,
                NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out var amount
            )
        )
            return CancellationRequestValidation.Invalid;
        var currency = CurrencyCode.Create(Currency);
        return currency.IsError ? currency.Errors : Money.Create(amount, currency.Value);
    }
}

public sealed record CancellationSettlementRequest(
    string Composition,
    bool CreditsKnownEmpty,
    bool OriginalCashOnlyVerified,
    bool UnpaidOrderVerified
)
{
    internal ErrorOr<CancellationSettlementFacts> ToFacts() =>
        CancellationRequestValidation.EnumValue(Composition, out SettlementComposition value)
            ? new CancellationSettlementFacts(
                value,
                CreditsKnownEmpty,
                CancellationResolutionSource.OperatorVerified,
                OriginalCashOnlyVerified,
                UnpaidOrderVerified
            )
            : CancellationRequestValidation.Invalid;
}

public sealed record CancellationReviewEvidenceRequest(
    string EvidenceRef,
    string Category,
    DateTimeOffset ObservedAt,
    string? ProviderOrderRef = null,
    string? ProviderCancellationRef = null,
    CancellationMoneyRequest? Refund = null,
    string? Destination = null,
    CancellationSettlementRequest? Settlement = null,
    string? ItineraryPartyHash = null,
    DateTimeOffset? ConfirmedAt = null,
    string? QuiescenceRef = null,
    DateTimeOffset? StoppedAt = null,
    Guid[]? StoppedInstanceIds = null,
    bool SupplierFinalNoEffectsConfirmed = false,
    bool SenderEgressIsolated = false,
    bool OldInstancesCannotResume = false,
    bool LegacyMutationFleetDrained = false,
    CancellationMoneyRequest? AcceptedMoney = null,
    Guid? PaymentReference = null,
    string? SupplierReceiptRef = null,
    bool WalletCaptureConfirmed = false,
    bool SupplierBookingPaymentConfirmed = false,
    bool PaymentCorrelationAttested = false,
    bool WalletFinalNoEffectsConfirmed = false,
    DateTimeOffset? TermsExpiresAt = null,
    bool PreparationCorrelationAttested = false,
    BookingCreationProofRequest? CreationEvidence = null,
    BookingServiceProofRequest? ServiceProof = null
)
{
    internal ErrorOr<ManualResolutionEvidence> ToEvidence()
    {
        if (
            !CancellationRequestValidation.EnumValue(Category, out ManualEvidenceCategory category)
            || ObservedAt == default
            || EvidenceRef is null
            || !System.Text.RegularExpressions.Regex.IsMatch(
                EvidenceRef,
                "^[A-Z0-9][A-Z0-9-]{7,63}$"
            )
        )
            return CancellationRequestValidation.Invalid;
        CancellationRefundDestination? destination = null;
        if (Destination is not null)
        {
            if (
                !CancellationRequestValidation.EnumValue(
                    Destination,
                    out CancellationRefundDestination parsed
                )
            )
                return CancellationRequestValidation.Invalid;
            destination = parsed;
        }
        Money? refund = null;
        Money? accepted = null;
        CancellationSettlementFacts? settlement = null;
        if (Refund is not null)
        {
            var value = Refund.ToMoney();
            if (value.IsError)
                return value.Errors;
            refund = value.Value;
        }
        if (AcceptedMoney is not null)
        {
            var value = AcceptedMoney.ToMoney();
            if (value.IsError)
                return value.Errors;
            accepted = value.Value;
        }
        if (Settlement is not null)
        {
            var value = Settlement.ToFacts();
            if (value.IsError)
                return value.Errors;
            settlement = value.Value;
        }
        BookingCreationProof? creation = null;
        BookingServiceProof? serviceProof = null;
        if (CreationEvidence is not null)
        {
            var mapped = CreationEvidence.ToProof();
            if (mapped.IsError)
                return mapped.Errors;
            creation = mapped.Value;
        }
        if (ServiceProof is not null)
        {
            var mapped = ServiceProof.ToProof();
            if (mapped.IsError)
                return mapped.Errors;
            serviceProof = mapped.Value;
        }
        return new ManualResolutionEvidence(
            EvidenceRef,
            category,
            ObservedAt,
            ProviderOrderRef,
            ProviderCancellationRef,
            refund,
            destination,
            settlement,
            ItineraryPartyHash,
            ConfirmedAt,
            QuiescenceRef,
            StoppedAt,
            StoppedInstanceIds is null
                ? (EquatableArray<Guid>?)null
                : new EquatableArray<Guid>(StoppedInstanceIds),
            SupplierFinalNoEffectsConfirmed,
            SenderEgressIsolated,
            OldInstancesCannotResume,
            LegacyMutationFleetDrained,
            accepted,
            PaymentReference is { } payment ? new PaymentRef(payment) : null,
            SupplierReceiptRef,
            WalletCaptureConfirmed,
            SupplierBookingPaymentConfirmed,
            PaymentCorrelationAttested,
            WalletFinalNoEffectsConfirmed,
            TermsExpiresAt,
            PreparationCorrelationAttested,
            creation,
            serviceProof
        );
    }
}
