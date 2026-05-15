using System.Diagnostics;
using ErrorOr;
using Marten;
using Microsoft.Extensions.Logging;
using Travel.Modules.Flights.Application.Commands;
using Travel.Modules.Flights.Application.Observability;
using Travel.Modules.Flights.Application.Persistence;
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
        if (cmd.AggregateId == Guid.Empty)
            return Error.Validation(
                "Flights.CommandInvalid",
                "HoldOfferCommand.AggregateId is required."
            );

        using var _ = log.BeginScope(
            new Dictionary<string, object>
            {
                ["order_id"] = cmd.AggregateId,
                ["correlation_id"] =
                    System.Diagnostics.Activity.Current?.TraceId.ToString() ?? string.Empty,
            }
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

        // FareConditions are captured at quote-time on the OfferQuoted event so the
        // hold request carries the exact terms shown to the user. Defensive fallback
        // covers any pre-WS2 stream that does not have the field on its OfferQuoted.
        var fareConditions = agg.FareConditions;
        if (fareConditions is null)
        {
            log.LogWarning(
                "BookingAggregate {AggregateId} predates FareConditions on OfferQuoted; using restrictive defaults.",
                cmd.AggregateId
            );
            fareConditions = new FareConditions(false, false, null, null);
        }
        var offer = new BookableOffer(
            Id: agg.OfferId!.Value,
            Itinerary: agg.Itinerary!,
            TotalAmount: agg.TotalAmount!,
            Provider: ProviderId.Duffel,
            FetchedAt: time.GetUtcNow(),
            ExpiresAt: agg.ExpiresAt!.Value,
            FareConditions: fareConditions,
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

        using var transitionSpan = FlightsActivitySource.Source.StartActivity(
            "booking.event.OfferHeld",
            ActivityKind.Internal
        );
        transitionSpan?.SetTag("aggregate.id", cmd.AggregateId.ToString());
        transitionSpan?.SetTag("aggregate.version", stream.CurrentVersion + 1);

        var saveResult = await marten.SaveOrConcurrencyConflictAsync(ct);
        if (saveResult.IsError)
            return saveResult.Errors;
        metrics.RecordAggregateEventsAppended(nameof(OfferHeld));

        return new HeldOrderResult(
            cmd.AggregateId,
            held.Value.ProviderOrderId,
            held.Value.HeldUntil
        );
    }
}
