using System.Diagnostics;
using ErrorOr;
using Marten;
using Microsoft.Extensions.Logging;
using Travel.Modules.Flights.Application.Booking;
using Travel.Modules.Flights.Application.Commands;
using Travel.Modules.Flights.Application.Contracts;
using Travel.Modules.Flights.Application.Observability;
using Travel.Modules.Flights.Application.Persistence;
using Travel.Modules.Flights.Core.Aggregates;
using Travel.Modules.Flights.Core.DomainEvents;
using Travel.Modules.Flights.Core.Errors;
using Travel.Modules.Flights.Core.Providers;
using Wolverine.Attributes;
using Wolverine.Marten;

namespace Travel.Modules.Flights.Application.Handlers.Booking;

public static class CancelOrderHandler
{
    // The explicit booking helper owns the commit and conflict translation.
    // Do not let generated middleware attempt a second save after a rejected write.
    [NonTransactional]
    [WolverineHandler]
    public static async Task<ErrorOr<CancelledOrderResult>> Handle(
        CancelOrderCommand cmd,
        IDocumentSession marten,
        IEnumerable<IFlightBookingProvider> bookingProviders,
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
                ["correlation_id"] =
                    System.Diagnostics.Activity.Current?.TraceId.ToString() ?? string.Empty,
            }
        );

        // 1. Load aggregate with optimistic concurrency tracking.
        var stream = await marten.Events.FetchForWriting<BookingAggregate>(cmd.AggregateId, ct);
        var agg = stream.Aggregate;
        var requiredVersion = stream.CurrentVersion + 1;
        if (agg is null)
            return FlightsErrors.OfferNotFound(cmd.AggregateId.ToString());

        // 2. Domain decisions before returning state or calling the provider.
        var ownerDecision = agg.DecideOwner(BookingTransition.Cancel, cmd.UserId);
        if (ownerDecision is BookingTransitionDecision.Rejected ownerRejected)
            return BookingTransitionErrorMapper.ToOwnerError(ownerRejected.Reason, cmd.AggregateId);

        var transitionDecision = agg.DecideCancel();
        if (transitionDecision is BookingTransitionDecision.IdempotentNoOp)
            return CreateResult(agg);
        if (transitionDecision is BookingTransitionDecision.Rejected transitionRejected)
            return BookingTransitionErrorMapper.ToError(transitionRejected.Reason);

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

        using var transitionSpan = FlightsActivitySource.Source.StartActivity(
            "booking.event.OrderCancelled",
            ActivityKind.Internal
        );
        transitionSpan?.SetTag("aggregate.id", cmd.AggregateId.ToString());
        transitionSpan?.SetTag("aggregate.version", stream.CurrentVersion + 1);

        var saveResult = await marten.SaveOrConcurrencyConflictAsync(
            outbox,
            cmd.AggregateId,
            [
                new OrderCancelledNotification(
                    cmd.AggregateId,
                    cmd.UserId,
                    CancelReason.User,
                    requiredVersion
                ),
            ],
            ct
        );
        if (saveResult.IsError)
            return saveResult.Errors;
        metrics.RecordAggregateEventsAppended(nameof(OrderCancelled));

        // 6. Apply locally only to construct the command response.
        agg.Apply(orderCancelled);

        return CreateResult(agg);
    }

    private static CancelledOrderResult CreateResult(BookingAggregate aggregate) =>
        new(
            aggregate.Id,
            aggregate.Status.ToString(),
            new OrderCommandSnapshot(
                aggregate.TotalAmount!,
                aggregate.Itinerary!,
                aggregate.TicketNumbers.ToArray(),
                aggregate.BookedAt ?? aggregate.ConfirmedAt ?? default,
                aggregate.TicketedAt,
                aggregate.CancelledAt,
                aggregate.RefundedAt
            )
        );
}
