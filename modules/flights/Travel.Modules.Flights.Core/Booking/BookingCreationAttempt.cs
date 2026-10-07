using Travel.Modules.Flights.Core.DomainEvents;
using Travel.Modules.Flights.Core.Providers.Dtos;
using Travel.Modules.Flights.Core.ValueObjects;

namespace Travel.Modules.Flights.Core.Booking;

public enum BookingCreationOutcome
{
    InProgress = 0,
    Matches = 1,
    CreatedWithDifferences = 2,
    NotCreated = 3,
    ManualReviewRequired = 4,
}

public sealed record BookingCreationAttempt(
    Guid Id,
    Guid OwnerId,
    string Digest,
    Guid QuoteRevision,
    BookingPurchase Accepted,
    ProtectedPassengerPartySnapshot ProtectedParty,
    Guid SenderInstanceId,
    DateTimeOffset StartedAt,
    BookingCreationOutcome Outcome = BookingCreationOutcome.InProgress
)
{
    public long Revision { get; init; } = 1;
    public BookedOrderFacts? Actual { get; init; }
    public string? KnownOrderId { get; init; }
    public bool SenderCompleted { get; init; }
    public bool CreationCompleted { get; init; }
    public bool HadUnknown { get; init; }
    public BookingEvidenceSource Source { get; init; }
    public string Reason { get; init; } = "";
    public DateTimeOffset? ObservedAt { get; init; }
    public bool IsUnresolved =>
        Outcome is BookingCreationOutcome.InProgress or BookingCreationOutcome.ManualReviewRequired;

    public override string ToString() => nameof(BookingCreationAttempt);
}

public sealed record CreationAdmission(bool IsNew, Guid AttemptId, BookingCreationStarted? Event);

public sealed record CreationObservationDecision(bool Changed, BookingCreationObserved? Event);
