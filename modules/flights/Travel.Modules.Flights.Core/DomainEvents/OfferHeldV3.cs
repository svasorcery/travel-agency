using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Shared.Abstractions;

namespace Travel.Modules.Flights.Core.DomainEvents;

public sealed record OfferHeldV3(
    string OrderId,
    ProtectedPassengerPartySnapshot PassengerSnapshot,
    DateTimeOffset HeldUntil,
    DateTimeOffset HeldAt,
    Guid OwnerUserId,
    Guid QuoteRevision,
    int PassengerCount
) : IDomainEvent
{
    public DateTimeOffset OccurredAt => HeldAt;
}
