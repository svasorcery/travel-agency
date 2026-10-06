using Travel.Modules.Flights.Core.Providers.Dtos;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Travel.Shared.Abstractions;

namespace Travel.Modules.Flights.Core.Cancellation;

public enum ManualResolutionTargetKind
{
    Unknown = 0,
    Cancellation = 1,
    Confirmation = 2,
    LegacyHeld = 3,
}

public enum ManualResolutionDecisionKind
{
    Unknown = 0,
    RecordInconclusive = 1,
    CloseNotDispatched = 2,
    ConfirmPreparedTerms = 3,
    ConfirmCancellation = 4,
    ConfirmNoEffect = 5,
    ConfirmBooking = 6,
    EnableLegacyCoordination = 7,
}

public enum ManualEvidenceCategory
{
    Unknown = 0,
    Inconclusive = 1,
    SupplierSupportAttestation = 2,
    StoredSupplierObservation = 3,
    PaymentAndSupplierAttestation = 4,
    QuiescenceAndNoEffectsAttestation = 5,
}

public enum ManualResolutionDisposition
{
    Rejected = 0,
    Allowed = 1,
    NoOp = 2,
}

// Actor/audit time and a claimed resolution source deliberately do not come from this input.
public sealed record ManualResolutionInput(
    Guid AggregateId,
    ManualResolutionTargetKind TargetKind,
    Guid TargetId,
    long ExpectedRevision,
    Guid ResolutionId,
    ManualResolutionDecisionKind Decision,
    ManualResolutionEvidence Evidence
);

public sealed record ManualResolutionEvidence(
    string EvidenceRef,
    ManualEvidenceCategory Category,
    DateTimeOffset ObservedAt,
    string? ProviderOrderRef = null,
    string? ProviderCancellationRef = null,
    Money? Refund = null,
    CancellationRefundDestination? Destination = null,
    CancellationSettlementFacts? Settlement = null,
    string? ItineraryPartyHash = null,
    DateTimeOffset? ConfirmedAt = null,
    string? QuiescenceRef = null,
    DateTimeOffset? StoppedAt = null,
    EquatableArray<Guid>? StoppedInstanceIds = null,
    bool SupplierFinalNoEffectsConfirmed = false,
    bool SenderEgressIsolated = false,
    bool OldInstancesCannotResume = false,
    bool LegacyMutationFleetDrained = false,
    Money? AcceptedMoney = null,
    PaymentRef? PaymentReference = null,
    string? SupplierReceiptRef = null,
    bool WalletCaptureConfirmed = false,
    bool SupplierBookingPaymentConfirmed = false,
    bool PaymentCorrelationAttested = false,
    bool WalletFinalNoEffectsConfirmed = false,
    DateTimeOffset? TermsExpiresAt = null,
    bool PreparationCorrelationAttested = false
);

public sealed record ManualResolutionDecision(
    ManualResolutionDisposition Disposition,
    CancellationReason Reason
);
