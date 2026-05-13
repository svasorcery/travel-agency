using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Travel.Shared.Abstractions;

namespace Travel.Modules.Flights.Core.DomainEvents;

public enum RefundInitiator
{
    Airline,
}

public sealed record OrderRefunded(
    RefundRef RefundRef,
    Money RefundedAmount,
    RefundInitiator InitiatedBy,
    DateTimeOffset RefundedAt
) : IDomainEvent
{
    public DateTimeOffset OccurredAt => RefundedAt;
}
