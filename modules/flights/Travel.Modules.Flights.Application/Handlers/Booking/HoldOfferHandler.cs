using ErrorOr;
using JasperFx.Events;
using Marten;
using Microsoft.Extensions.Logging;
using Travel.Modules.Flights.Application.Commands;
using Travel.Modules.Flights.Application.Observability;
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
        IFlightsMetrics metrics,
        TimeProvider time,
        ILogger<HoldOfferCommand> log,
        CancellationToken ct
    )
    {
        using var _ = log.BeginScope(
            new Dictionary<string, object> { ["order_id"] = cmd.AggregateId }
        );

        var stream = await marten.Events.FetchForWriting<BookingAggregate>(cmd.AggregateId, ct);
        var agg = stream.Aggregate;
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

        stream.AppendOne(
            new OfferHeld(
                OrderId: held.Value.ProviderOrderId,
                Passenger: cmd.Passenger,
                HeldUntil: held.Value.HeldUntil,
                HeldAt: time.GetUtcNow()
            )
        );
        try
        {
            await marten.SaveChangesAsync(ct);
        }
        catch (EventStreamUnexpectedMaxEventIdException)
        {
            return FlightsErrors.ConcurrencyConflict;
        }
        metrics.RecordAggregateEventsAppended(nameof(OfferHeld));

        return new HeldOrderResult(
            cmd.AggregateId,
            held.Value.ProviderOrderId,
            held.Value.HeldUntil
        );
    }
}
