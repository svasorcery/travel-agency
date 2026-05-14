using ErrorOr;
using Marten;
using Travel.Modules.Flights.Application.Commands;
using Travel.Modules.Flights.Core.Aggregates;
using Travel.Modules.Flights.Core.DomainEvents;
using Travel.Modules.Flights.Core.Errors;
using Travel.Modules.Flights.Core.Providers;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Travel.Modules.Flights.Core.ValueObjects.Offer;
using Wolverine.Attributes;

namespace Travel.Modules.Flights.Application.Handlers.Booking;

public static class HoldOfferHandler
{
    [WolverineHandler]
    public static async Task<ErrorOr<HeldOrderResult>> Handle(
        HoldOfferCommand cmd,
        IEnumerable<IFlightBookingProvider> bookingProviders,
        IDocumentSession marten,
        TimeProvider time,
        CancellationToken ct
    )
    {
        var agg = await marten.Events.AggregateStreamAsync<BookingAggregate>(
            cmd.AggregateId,
            token: ct
        );
        if (agg is null)
            return FlightsErrors.OfferNotFound(cmd.AggregateId.ToString());

        if (agg.Status != BookingStatus.OfferQuoted)
            return Error.Conflict(
                "Flights.InvalidState",
                $"Cannot hold an offer when booking is in state {agg.Status}."
            );

        if (agg.ExpiresAt <= time.GetUtcNow())
            return FlightsErrors.OfferExpired;

        // M1: single booking provider
        var provider = bookingProviders.Single();

        var offer = new BookableOffer(
            Id: agg.OfferId!.Value,
            Itinerary: agg.Itinerary!,
            TotalAmount: agg.TotalAmount!,
            Provider: ProviderId.Duffel,
            FetchedAt: time.GetUtcNow(),
            ExpiresAt: agg.ExpiresAt!.Value,
            FareConditions: new FareConditions(false, false, null, null),
            ProviderOfferRef: agg.ProviderOfferRef!
        );

        var held = await provider.HoldOfferAsync(offer, cmd.Passenger, ct);
        if (held.IsError)
            return held.FirstError;

        marten.Events.Append(
            cmd.AggregateId,
            new OfferHeld(
                OrderId: held.Value.ProviderOrderId,
                Passenger: cmd.Passenger,
                HeldUntil: held.Value.HeldUntil,
                HeldAt: time.GetUtcNow()
            )
        );
        await marten.SaveChangesAsync(ct);

        return new HeldOrderResult(
            cmd.AggregateId,
            held.Value.ProviderOrderId,
            held.Value.HeldUntil
        );
    }
}
