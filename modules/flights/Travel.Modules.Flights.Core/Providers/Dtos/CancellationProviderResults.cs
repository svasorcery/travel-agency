using Travel.Modules.Flights.Core.Cancellation;
using Travel.Modules.Flights.Core.ValueObjects;

namespace Travel.Modules.Flights.Core.Providers.Dtos;

public enum CancellationReason
{
    None = 0,
    MissingTerms = 1,
    UnsupportedFinancialTerms = 2,
    TermsExpired = 3,
    ProviderUnavailable = 4,
    IdentityMismatch = 5,
    Pending = 6,
    Uncorrelated = 7,
    StaleProposal = 8,
    NotCancellable = 9,
    AlreadyCancelled = 10,
    InconsistentEvidence = 11,
    ManualVerificationRequired = 12,
    InvalidResponse = 13,
    RefreshTooSoon = 14,
}

public enum CancellationProviderPaymentState
{
    Unknown = 0,
    AwaitingPayment = 1,
    Paid = 2,
}

public enum CancellationQuoteOutcome
{
    Unknown = 0,
    TermsAvailable = 1,
    UnsupportedTerms = 2,
    DefinitivelyRejected = 3,
}

public enum CancellationEffectOutcome
{
    Unknown = 0,
    Confirmed = 1,
    DefinitivelyRejected = 2,
}

public enum CancellationObservationState
{
    Unknown = 0,
    Pending = 1,
    Confirmed = 2,
    DefinitivelyRejected = 3,
}

public sealed record CancellationEligibility(
    string ProviderOrderRef,
    bool CancellationAvailable,
    CancellationProviderPaymentState PaymentState,
    Money? AcceptedTotal,
    CancellationSettlementFacts Settlement,
    string? CurrentCancellationRef = null,
    string? ItineraryPartyHash = null,
    SupplierOrderCancellationFact? OrderCancellation = null
);

public sealed record CancellationQuoteFacts(
    string ProviderOrderRef,
    string ProviderCancellationRef,
    Money? Refund,
    CancellationRefundDestination Destination,
    CancellationSettlementFacts Settlement,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset CreatedAt,
    string? ItineraryPartyHash = null
);

/// <summary>UnsupportedTerms requires positive creation-completion facts, not a lost response.</summary>
public sealed record CancellationQuoteResult(
    CancellationQuoteOutcome Outcome,
    CancellationQuoteFacts? Quote,
    CancellationReason Reason = CancellationReason.None,
    SupplierOrderCancellationFact? OrderCancellation = null
);

/// <summary>A declared outcome still requires matching validated observations before local finalization.</summary>
public sealed record CancellationEffectResult(
    CancellationEffectOutcome Outcome,
    CancellationObservation? Observation,
    CancellationReason Reason = CancellationReason.None
);

public sealed record CancellationObservation(
    string ProviderOrderRef,
    string? ProviderCancellationRef,
    CancellationObservationState State,
    CancellationQuoteFacts? Quote,
    DateTimeOffset? ConfirmedAt,
    DateTimeOffset ObservedAt,
    CancellationResolutionSource Source,
    CancellationReason Reason = CancellationReason.None,
    SupplierOrderCancellationFact? OrderCancellation = null
);

/// <summary>Missing create correlation permits read/manual handling; it never authorizes another quote.</summary>
public sealed record CancellationCorrelation(
    string ProviderOrderRef,
    string? ProviderCancellationRef,
    CancellationTerms? AcceptedTerms,
    string ItineraryPartyHash
);

/// <summary>Known order cancellation only; it proves neither our consent nor a refund/customer payout.</summary>
public sealed record SupplierOrderCancellationFact(
    string ProviderOrderRef,
    string? ProviderCancellationRef,
    DateTimeOffset CancelledAt,
    DateTimeOffset ObservedAt,
    CancellationResolutionSource Source
)
{
    public override string ToString() => nameof(SupplierOrderCancellationFact);
}
