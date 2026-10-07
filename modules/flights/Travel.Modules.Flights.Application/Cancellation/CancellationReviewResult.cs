using Travel.Modules.Flights.Core.Booking;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Shared.Abstractions;

namespace Travel.Modules.Flights.Application.Cancellation;

public sealed record CancellationReviewResult(
    CancellationStatusResult Status,
    CancellationReviewTarget? Target,
    IReadOnlyList<CancellationReviewAudit> History,
    CreationReviewContext? CreationContext = null
);

public sealed record CreationReviewContext(
    Guid OwnerId,
    string ProviderOfferRef,
    Guid QuoteRevision,
    EquatableArray<Guid> BookingPassengerIds,
    Itinerary Itinerary,
    BookingPurchase Accepted
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
