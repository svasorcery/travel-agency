using ErrorOr;
using Marten;
using Travel.Modules.Flights.Application.Queries;
using Travel.Modules.Flights.Core.Aggregates;
using Travel.Modules.Flights.Core.Errors;
using Travel.Modules.Flights.Core.Providers;
using Travel.Modules.Flights.Core.Providers.Dtos;
using Wolverine.Attributes;

namespace Travel.Modules.Flights.Application.Handlers.Booking;

public static class GetAncillariesHandler
{
    [WolverineHandler]
    public static async Task<ErrorOr<AncillaryCatalogResult>> Handle(
        GetAncillariesQuery query,
        IQuerySession session,
        IFlightAncillaryProvider provider,
        TimeProvider time,
        CancellationToken ct
    )
    {
        var booking = await session.Events.AggregateStreamAsync<BookingAggregate>(
            query.AggregateId,
            token: ct
        );
        if (
            query.UserId == Guid.Empty
            || booking is null
            || booking.OwnerUserId is { } owner && owner != query.UserId
        )
            return FlightsErrors.OfferNotFound(query.AggregateId.ToString());
        if (
            booking.Status != BookingStatus.OfferQuoted
            || booking.HasCreationBarrier
            || booking.QuoteBinding is null
            || booking.QuoteBinding.Revision != query.QuoteRevision
            || booking.ExpiresAt <= time.GetUtcNow()
        )
            return Error.Conflict("Flights.QuoteRevisionMismatch", "Refresh the current quote.");
        var facts = await provider.ReadCatalogAsync(
            booking.ProviderOfferRef!,
            query.IncludeSeats,
            ct
        );
        if (facts.IsError)
            return facts.Errors;
        if (
            facts.Value.Offer.Party != booking.QuoteBinding.Party
            || !BookedOrderFacts.SameItinerary(facts.Value.Offer.Itinerary, booking.Itinerary!)
        )
            return Error.Conflict(
                "Flights.QuoteRevisionMismatch",
                "Offer membership changed; refresh the quote."
            );
        return new AncillaryCatalogResult(query.AggregateId, booking.QuoteBinding, facts.Value);
    }
}
