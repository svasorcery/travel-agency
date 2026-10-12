using ErrorOr;
using Marten;
using Travel.Modules.Flights.Application.FareRules;
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
                stream.CurrentVersion ?? 0,
                booking.FareSnapshot is { } snapshot
                    ? FareSummaryFormatter.Historical(
                        snapshot,
                        time.GetUtcNow(),
                        booking.Purchase?.HasServices == true
                    )
                    : FareSummaryFormatter.Unavailable(
                        query.AggregateId,
                        time.GetUtcNow(),
                        booking.PassengerCount,
                        provenance: "Historical",
                        offerId: booking.OfferId?.Value,
                        quoteRevision: booking.QuoteBinding?.Revision,
                        segmentCounts: booking
                            .Itinerary?.Slices.Select(s => s.Segments.Count)
                            .ToArray(),
                        hasSelectedServices: booking.Purchase?.HasServices == true
                    )
            );
    }
}
