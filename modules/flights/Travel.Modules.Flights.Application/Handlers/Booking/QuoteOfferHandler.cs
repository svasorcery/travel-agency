using ErrorOr;
using Marten;
using Microsoft.Extensions.Logging;
using Travel.Modules.Flights.Application.Booking;
using Travel.Modules.Flights.Application.Commands;
using Travel.Modules.Flights.Application.Observability;
using Travel.Modules.Flights.Application.Persistence;
using Travel.Modules.Flights.Core.Aggregates;
using Travel.Modules.Flights.Core.DomainEvents;
using Travel.Modules.Flights.Core.Errors;
using Travel.Modules.Flights.Core.Providers;
using Wolverine.Attributes;
using Wolverine.Marten;

namespace Travel.Modules.Flights.Application.Handlers.Booking;

public static class QuoteOfferHandler
{
    // The explicit booking helper owns the commit and conflict translation.
    // Do not let generated middleware attempt a second save after a rejected write.
    [NonTransactional]
    [WolverineHandler]
    public static async Task<ErrorOr<QuotedOfferResult>> Handle(
        QuoteOfferCommand cmd,
        IEnumerable<IFlightBookingProvider> bookingProviders,
        IDocumentSession marten,
        IMartenOutbox outbox,
        IFlightsMetrics metrics,
        TimeProvider time,
        ILogger<QuoteOfferCommand> log,
        CancellationToken ct
    )
    {
        // Guard inputs before any provider call — a missing offer ref would otherwise
        // be sent verbatim to the upstream booking provider.
        if (string.IsNullOrWhiteSpace(cmd.ProviderOfferRef))
            return Error.Validation(
                "Flights.CommandInvalid",
                "QuoteOfferCommand.ProviderOfferRef is required."
            );

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

            var decision = existingAgg.DecideReQuote(cmd.ProviderOfferRef);
            if (decision is BookingTransitionDecision.Rejected rejected)
                return BookingTransitionErrorMapper.ToError(rejected.Reason);

            var refreshedExisting = await provider.RefreshOfferAsync(cmd.ProviderOfferRef, ct);
            if (refreshedExisting.IsError)
                return refreshedExisting.FirstError;

            var oldAmount = existingAgg.TotalAmount!;
            var newAmount = refreshedExisting.Value.TotalAmount;
            var priceChanged = oldAmount != newAmount;

            stream.AppendOne(
                new OfferReQuoted(
                    OfferId: refreshedExisting.Value.Id,
                    OldAmount: oldAmount,
                    NewAmount: newAmount,
                    ReQuotedAt: time.GetUtcNow(),
                    RefreshedOffer: refreshedExisting.Value
                )
            );
            var requoteSaveResult = await marten.SaveOrConcurrencyConflictAsync(
                outbox,
                existingId,
                [],
                ct
            );
            if (requoteSaveResult.IsError)
                return requoteSaveResult.Errors;
            metrics.RecordAggregateEventsAppended(nameof(OfferReQuoted));

            return new QuotedOfferResult(
                existingId,
                refreshedExisting.Value,
                PriceChanged: priceChanged,
                OldAmount: priceChanged ? oldAmount : null,
                NewAmount: priceChanged ? newAmount : null
            );
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
        await marten.SaveBookingWithReconcileAsync(outbox, aggregateId, [], ct);
        metrics.RecordAggregateEventsAppended(nameof(OfferQuoted));

        return new QuotedOfferResult(aggregateId, refreshed.Value);
    }
}
