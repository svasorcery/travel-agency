using Travel.Modules.Flights.Core.DomainEvents;

namespace Travel.Modules.Flights.Application.Contracts;

public sealed record OrderConfirmedNotification(Guid AggregateId, Guid UserId);

public sealed record OrderTicketedNotification(Guid AggregateId, Guid UserId);

public sealed record OrderCancelledNotification(
    Guid AggregateId,
    Guid UserId,
    CancelReason Reason = CancelReason.User
);
