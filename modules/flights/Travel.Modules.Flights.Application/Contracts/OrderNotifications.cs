using Travel.Modules.Flights.Application.Booking;
using Travel.Modules.Flights.Core.Aggregates;
using Travel.Modules.Flights.Core.DomainEvents;
using Travel.Shared.Abstractions;

namespace Travel.Modules.Flights.Application.Contracts;

public sealed record OrderConfirmedNotification(
    Guid AggregateId,
    Guid UserId,
    long? RequiredStreamVersion = null
);

public sealed record OrderTicketedNotification(
    Guid AggregateId,
    Guid UserId,
    long? RequiredStreamVersion = null
);

public sealed record OrderCancelledNotification(
    Guid AggregateId,
    Guid UserId,
    CancelReason Reason = CancelReason.User,
    long? RequiredStreamVersion = null
);

public sealed record OrderChangedNotification(
    Guid AggregateId,
    Guid UserId,
    long RequiredStreamVersion
);

public static class OrderChangedNotifications
{
    public static IReadOnlyList<object> For(
        BookingAggregate booking,
        long version,
        IReadOnlyList<IDomainEvent> events
    )
    {
        if (
            events.Count == 0
            || !(
                booking.Status
                    is BookingStatus.Held
                        or BookingStatus.Confirmed
                        or BookingStatus.Ticketed
                        or BookingStatus.Cancelled
                        or BookingStatus.Refunded
                || events.Any(e => e is OfferHeld or OfferHeldV2 or OfferHeldV3)
            )
        )
            return [];
        if (
            booking.OwnerUserId is not { } owner
            || owner == Guid.Empty
            || !booking.HasConsistentMutationOwner
        )
            throw new BookingSourceOwnershipMissingException(booking.Id);
        return [new OrderChangedNotification(booking.Id, owner, version)];
    }
}
