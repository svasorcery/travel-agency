using Travel.Modules.Flights.Core.Booking;
using Travel.Modules.Flights.Core.Providers.Dtos;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Shared.Abstractions;

namespace Travel.Modules.Flights.Core.DomainEvents;

public sealed record BookingPurchaseQuoted(BookingPurchase Purchase, DateTimeOffset OccurredAt)
    : IDomainEvent;

public sealed record BookingCreationStarted(
    Guid AttemptId,
    Guid OwnerId,
    string Digest,
    Guid QuoteRevision,
    BookingPurchase Accepted,
    ProtectedPassengerPartySnapshot ProtectedParty,
    Guid SenderInstanceId,
    DateTimeOffset OccurredAt
) : IDomainEvent;

public sealed record BookingCreationObserved(
    Guid AttemptId,
    BookingCreationObservation Observation,
    DateTimeOffset OccurredAt
) : IDomainEvent;
