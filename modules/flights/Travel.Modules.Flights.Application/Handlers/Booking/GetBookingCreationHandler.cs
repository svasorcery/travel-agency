using ErrorOr;
using Marten;
using Travel.Modules.Flights.Application.Queries;
using Travel.Modules.Flights.Core.Aggregates;
using Travel.Modules.Flights.Core.Errors;
using Wolverine.Attributes;

namespace Travel.Modules.Flights.Application.Handlers.Booking;

public static class GetBookingCreationHandler
{
    [NonTransactional]
    [WolverineHandler]
    public static async Task<ErrorOr<BookingCreationStatusResult>> Handle(
        GetBookingCreationQuery query,
        IDocumentSession session,
        TimeProvider time,
        CancellationToken ct
    )
    {
        var stream = await session.Events.FetchForWriting<BookingAggregate>(query.AggregateId, ct);
        var booking = stream.Aggregate;
        return
            booking is null
            || query.UserId == Guid.Empty
            || booking.OwnerUserId != query.UserId
            || !booking.HasConsistentMutationOwner
            ? FlightsErrors.OfferNotFound(query.AggregateId.ToString())
            : new BookingCreationStatusResult(
                booking,
                time.GetUtcNow(),
                stream.CurrentVersion ?? 0
            );
    }
}
