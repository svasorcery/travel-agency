using Travel.Shared.Abstractions;

namespace Travel.Modules.Flights.Core.DomainEvents;

public sealed record OrderTicketed(EquatableArray<string> TicketNumbers, DateTimeOffset TicketedAt)
    : IDomainEvent
{
    public DateTimeOffset OccurredAt => TicketedAt;
}
