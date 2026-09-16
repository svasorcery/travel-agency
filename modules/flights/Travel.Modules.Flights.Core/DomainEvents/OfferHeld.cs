using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Shared.Abstractions;

namespace Travel.Modules.Flights.Core.DomainEvents;

public sealed record OfferHeld(
    string OrderId,
    PassengerInfo Passenger,
    DateTimeOffset HeldUntil,
    DateTimeOffset HeldAt,
    Guid? OwnerUserId = null
) : IDomainEvent
{
    public DateTimeOffset OccurredAt => HeldAt;
}
