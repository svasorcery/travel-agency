using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Travel.Shared.Abstractions;

namespace Travel.Modules.Flights.Core.DomainEvents;

public sealed record PaymentAuthorized(
    PaymentRef PaymentRef,
    Money Amount,
    DateTimeOffset AuthorizedAt
) : IDomainEvent
{
    public DateTimeOffset OccurredAt => AuthorizedAt;
}
