using ErrorOr;
using Marten;
using Travel.Modules.Flights.Application.Queries;
using Travel.Modules.Flights.Core.Aggregates;
using Travel.Modules.Flights.Core.Errors;
using Wolverine.Attributes;

namespace Travel.Modules.Flights.Application.Handlers.Booking;

public static class GetBookingCreationHandler
{
    [WolverineHandler]
    public static async Task<ErrorOr<BookingCreationStatusResult>> Handle(
        GetBookingCreationQuery query,
        IQuerySession session,
        TimeProvider time,
        CancellationToken ct
    )
    {
        var booking = await session.Events.AggregateStreamAsync<BookingAggregate>(
            query.AggregateId,
            token: ct
        );
        return
            booking is null
            || query.UserId == Guid.Empty
            || booking.OwnerUserId != query.UserId
            || !booking.HasConsistentMutationOwner
            ? FlightsErrors.OfferNotFound(query.AggregateId.ToString())
            : new BookingCreationStatusResult(booking, time.GetUtcNow());
    }
}
