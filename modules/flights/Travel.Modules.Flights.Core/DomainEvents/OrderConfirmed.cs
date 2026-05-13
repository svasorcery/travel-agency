using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Travel.Shared.Abstractions;

namespace Travel.Modules.Flights.Core.DomainEvents;

public sealed record OrderConfirmed(
    string OrderId,
    PaymentRef PaymentRef,
    DateTimeOffset ConfirmedAt
) : IDomainEvent
{
    public DateTimeOffset OccurredAt => ConfirmedAt;
}
