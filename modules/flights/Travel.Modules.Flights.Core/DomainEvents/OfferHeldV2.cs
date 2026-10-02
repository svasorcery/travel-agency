using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Shared.Abstractions;

namespace Travel.Modules.Flights.Core.DomainEvents;

public sealed record OfferHeldV2(
    string OrderId,
    ProtectedPassengerSnapshot PassengerSnapshot,
    DateTimeOffset HeldUntil,
    DateTimeOffset HeldAt,
    Guid OwnerUserId
) : IDomainEvent
{
    public DateTimeOffset OccurredAt => HeldAt;
}
