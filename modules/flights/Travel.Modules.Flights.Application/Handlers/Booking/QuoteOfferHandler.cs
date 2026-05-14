using ErrorOr;
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
                QuotedAt: time.GetUtcNow()
            )
        );
        await marten.SaveChangesAsync(ct);
        metrics.RecordAggregateEventsAppended(nameof(OfferQuoted));

        return new QuotedOfferResult(aggregateId, refreshed.Value);
    }
}
