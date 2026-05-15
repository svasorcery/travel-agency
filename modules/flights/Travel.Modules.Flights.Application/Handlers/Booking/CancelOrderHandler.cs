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
using Wolverine.Attributes;
using Wolverine.Marten;

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
        IMartenOutbox outbox,
        TimeProvider time,
        ILogger<CancelOrderCommand> log,
        CancellationToken ct
    )
    {
        if (cmd.AggregateId == Guid.Empty)
            return Error.Validation(
                "Flights.CommandInvalid",
                "CancelOrderCommand.AggregateId is required."
            );
        if (cmd.UserId == Guid.Empty)
            return Error.Validation(
                "Flights.CommandInvalid",
                "CancelOrderCommand.UserId is required."
            );

        using var _ = log.BeginScope(
            new Dictionary<string, object>
            {
                ["order_id"] = cmd.AggregateId,
                ["user_id"] = cmd.UserId,
            }
        );

        // Enroll the document session with the Wolverine outbox so outgoing messages
        // commit atomically with the events on the same SaveChangesAsync.
        outbox.Enroll(marten);

        // 1. Load aggregate with optimistic concurrency tracking.
        var stream = await marten.Events.FetchForWriting<BookingAggregate>(cmd.AggregateId, ct);
        var agg = stream.Aggregate;
        if (agg is null)
            return FlightsErrors.OfferNotFound(cmd.AggregateId.ToString());

        // 2. Idempotency: already in a terminal cancelled/refunded state — no-op success.
        if (agg.Status is BookingStatus.Cancelled or BookingStatus.Refunded)
            return new CancelledOrderResult(cmd.AggregateId, agg.Status.ToString());

        // 3. Ticketed orders cannot be cancelled — the refund flow goes through
        //    the webhook-driven OrderRefunded path (spec §4.1).
        if (agg.Status is BookingStatus.Ticketed)
            return FlightsErrors.OrderNotCancellable(
                $"Order in state {agg.Status} cannot be cancelled."
            );

        // 4. Best-effort provider cancellation when there is a provider order
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

        // 5. Append domain event and enqueue the notification via the outbox BEFORE
        //    SaveChangesAsync so both ride the same Marten transaction. If
        //    SaveChangesAsync rolls back the buffered message is discarded along
        //    with the event.
        var orderCancelled = new OrderCancelled(CancelReason.User, time.GetUtcNow());
        stream.AppendOne(orderCancelled);
        await outbox.PublishAsync(new OrderCancelledNotification(cmd.AggregateId, cmd.UserId));

        var saveResult = await marten.SaveOrConcurrencyConflictAsync(ct);
        if (saveResult.IsError)
            return saveResult.Errors;
        metrics.RecordAggregateEventsAppended(nameof(OrderCancelled));

        // 6. Project read model from the in-memory aggregate with the cancel applied —
        //    avoids a redundant re-read of the stream (SAGA-M2).
        agg.Apply(orderCancelled);
        await projector.Project(agg, cmd.UserId, ct);

        return new CancelledOrderResult(cmd.AggregateId, "Cancelled");
    }
}
