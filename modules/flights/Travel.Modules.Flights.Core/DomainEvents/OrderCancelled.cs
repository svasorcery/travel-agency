using Travel.Shared.Abstractions;

namespace Travel.Modules.Flights.Core.DomainEvents;

public enum CancelReason
{
    User,
    Airline,
    System,
}

public sealed record OrderCancelled(CancelReason Reason, DateTimeOffset CancelledAt) : IDomainEvent
{
    public DateTimeOffset OccurredAt => CancelledAt;
}
