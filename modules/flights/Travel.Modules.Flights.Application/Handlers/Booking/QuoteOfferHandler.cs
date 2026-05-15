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
using Wolverine.Attributes;

namespace Travel.Modules.Flights.Application.Handlers.Booking;

public static class QuoteOfferHandler
{
    [WolverineHandler]
    public static async Task<ErrorOr<QuotedOfferResult>> Handle(
        QuoteOfferCommand cmd,
        IEnumerable<IFlightBookingProvider> bookingProviders,
        IDocumentSession marten,
        IFlightsMetrics metrics,
        TimeProvider time,
        ILogger<QuoteOfferCommand> log,
        CancellationToken ct
    )
    {
        var provider = bookingProviders.FirstOrDefault(p => p.Id == cmd.Provider);
        if (provider is null)
            return FlightsErrors.ProviderUnavailable(cmd.Provider.Value);

        // ── Re-quote path ────────────────────────────────────────────────────────
        // When the caller supplies an existing AggregateId we refresh the offer on
        // the existing stream (appending OfferReQuoted) rather than starting a new
        // stream. The stream must still be in OfferQuoted — once Held / Confirmed
        // the offer is locked.
        if (cmd.AggregateId is { } existingId && existingId != Guid.Empty)
        {
            using var requoteScope = log.BeginScope(
                new Dictionary<string, object> { ["order_id"] = existingId }
            );

            var stream = await marten.Events.FetchForWriting<BookingAggregate>(existingId, ct);
            var existingAgg = stream.Aggregate;
            if (existingAgg is null)
                return FlightsErrors.OfferNotFound(existingId.ToString());

            if (existingAgg.Status != BookingStatus.OfferQuoted)
                return Error.Conflict(
                    "Flights.InvalidState",
                    $"Cannot re-quote in state {existingAgg.Status}."
                );

            var refreshedExisting = await provider.RefreshOfferAsync(cmd.ProviderOfferRef, ct);
            if (refreshedExisting.IsError)
                return refreshedExisting.FirstError;

            stream.AppendOne(
                new OfferReQuoted(
                    OfferId: existingAgg.OfferId!.Value,
                    OldAmount: existingAgg.TotalAmount!,
                    NewAmount: refreshedExisting.Value.TotalAmount,
                    ReQuotedAt: time.GetUtcNow()
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
            metrics.RecordAggregateEventsAppended(nameof(OfferReQuoted));

            return new QuotedOfferResult(existingId, refreshedExisting.Value);
        }

        // ── New-stream path ──────────────────────────────────────────────────────
        var refreshed = await provider.RefreshOfferAsync(cmd.ProviderOfferRef, ct);
        if (refreshed.IsError)
            return refreshed.FirstError;

        var aggregateId = Guid.NewGuid();
        using var _ = log.BeginScope(new Dictionary<string, object> { ["order_id"] = aggregateId });
        marten.Events.StartStream<BookingAggregate>(
            aggregateId,
            new OfferQuoted(
                OfferId: refreshed.Value.Id,
                Itinerary: refreshed.Value.Itinerary,
                TotalAmount: refreshed.Value.TotalAmount,
                ExpiresAt: refreshed.Value.ExpiresAt,
                ProviderRef: refreshed.Value.ProviderOfferRef,
                QuotedAt: time.GetUtcNow(),
                FareConditions: refreshed.Value.FareConditions
            )
        );
        await marten.SaveChangesAsync(ct);
        metrics.RecordAggregateEventsAppended(nameof(OfferQuoted));

        return new QuotedOfferResult(aggregateId, refreshed.Value);
    }
}
