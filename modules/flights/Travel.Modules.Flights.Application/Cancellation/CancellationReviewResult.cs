namespace Travel.Modules.Flights.Application.Cancellation;

public sealed record CancellationReviewResult(
    CancellationStatusResult Status,
    CancellationReviewTarget? Target,
    IReadOnlyList<CancellationReviewAudit> History
);

public sealed record CancellationReviewTarget(
    string Kind,
    Guid TargetId,
    long Revision,
    string Phase,
    string ProviderOrderRef,
    string? ProviderCancellationRef,
    Guid? DispatchOwnerInstanceId,
    DateTimeOffset? DispatchedAt,
    string? PaymentReference,
    string? SupplierReceiptRef,
    string? ItineraryPartyHash = null,
    string? AcceptedAmount = null,
    string? AcceptedCurrency = null
);

public sealed record CancellationReviewAudit(
    Guid ResolutionId,
    Guid ActorId,
    string Decision,
    string Source,
    string EvidenceRef,
    DateTimeOffset RecordedAt
);
