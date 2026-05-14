using ErrorOr;
using Marten;
using Microsoft.Extensions.Logging;
using Travel.Modules.Flights.Application.Commands;
using Travel.Modules.Flights.Application.Contracts;
using Travel.Modules.Flights.Application.Observability;
using Travel.Modules.Flights.Core.Aggregates;
using Travel.Modules.Flights.Core.DomainEvents;
using Travel.Modules.Flights.Core.Errors;
using Travel.Modules.Flights.Core.Providers;
using Wolverine;
using Wolverine.Attributes;

namespace Travel.Modules.Flights.Application.Handlers.Booking;

public static class CancelOrderHandler
{
    [WolverineHandler]
    public static async Task<ErrorOr<CancelledOrderResult>> Handle(
        CancelOrderCommand cmd,
        IDocumentSession marten,
        IEnumerable<IFlightBookingProvider> bookingProviders,
        IOrderReadModelProjector projector,
        IFlightsMetrics metrics,
        IMessageBus bus,
        TimeProvider time,
        ILogger<CancelOrderCommand> log,
        CancellationToken ct
    )
    {
        using var _ = log.BeginScope(
            new Dictionary<string, object>
            {
                ["order_id"] = cmd.AggregateId,
                ["user_id"] = cmd.UserId,
            }
        );

        // 1. Load aggregate
        var agg = await marten.Events.AggregateStreamAsync<BookingAggregate>(
            cmd.AggregateId,
            token: ct
        );
        if (agg is null)
            return FlightsErrors.OfferNotFound(cmd.AggregateId.ToString());

        // 2. Idempotency: already in a terminal cancelled/refunded state
        if (agg.Status is BookingStatus.Cancelled or BookingStatus.Refunded)
            return new CancelledOrderResult(cmd.AggregateId, agg.Status.ToString());

        // 3. Best-effort provider cancellation when there is a provider order
        if (agg.ProviderOrderId is not null)
        {
            var provider = bookingProviders.Single();
            var cancelResult = await provider.CancelOrderAsync(agg.ProviderOrderId, ct);
            if (cancelResult.IsError)
                log.LogWarning(
                    "Provider cancellation failed for order {ProviderOrderId} (aggregate {AggregateId}): {Error}. Continuing with domain cancellation.",
                    agg.ProviderOrderId,
                    cmd.AggregateId,
                    cancelResult.FirstError.Description
                );
        }

        // 4. Append domain event and save
        marten.Events.Append(
            cmd.AggregateId,
            new OrderCancelled(CancelReason.User, time.GetUtcNow())
        );
        await marten.SaveChangesAsync(ct);
        metrics.RecordAggregateEventsAppended(nameof(OrderCancelled));

        // 5. Project read model
        var updatedAgg = await marten.Events.AggregateStreamAsync<BookingAggregate>(
            cmd.AggregateId,
            token: ct
        );
        await projector.Project(updatedAgg!, cmd.UserId, time, ct);

        // 6. Publish notification
        await bus.PublishAsync(new OrderCancelledNotification(cmd.AggregateId, cmd.UserId));

        return new CancelledOrderResult(cmd.AggregateId, "Cancelled");
    }
}
