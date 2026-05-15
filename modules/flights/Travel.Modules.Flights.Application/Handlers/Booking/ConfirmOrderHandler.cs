using ErrorOr;
using JasperFx.Events;
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

public static class ConfirmOrderHandler
{
    [WolverineHandler]
    public static async Task<ErrorOr<ConfirmedOrderResult>> Handle(
        ConfirmOrderCommand cmd,
        IDocumentSession marten,
        IEnumerable<IFlightBookingProvider> bookingProviders,
        IPaymentGateway payments,
        IOrderReadModelProjector projector,
        IFlightsMetrics metrics,
        IMartenOutbox outbox,
        TimeProvider time,
        ILogger<ConfirmOrderCommand> log,
        CancellationToken ct
    )
    {
        if (cmd.AggregateId == Guid.Empty)
            return Error.Validation(
                "Flights.CommandInvalid",
                "ConfirmOrderCommand.AggregateId is required."
            );
        if (cmd.UserId == Guid.Empty)
            return Error.Validation(
                "Flights.CommandInvalid",
                "ConfirmOrderCommand.UserId is required."
            );

        using var _ = log.BeginScope(
            new Dictionary<string, object>
            {
                ["order_id"] = cmd.AggregateId,
                ["user_id"] = cmd.UserId,
            }
        );

        // Enroll the document session with the Wolverine outbox so any outgoing
        // messages enqueued via the outbox commit atomically with the events on
        // the same SaveChangesAsync. Idempotent: safe to call repeatedly.
        outbox.Enroll(marten);

        // 1. Load aggregate with optimistic concurrency tracking. FetchForWriting captures
        //    the expected version at load time; AppendOne + SaveChangesAsync enforces it.
        var stream = await marten.Events.FetchForWriting<BookingAggregate>(cmd.AggregateId, ct);
        var agg = stream.Aggregate;
        if (agg is null)
            return FlightsErrors.OfferNotFound(cmd.AggregateId.ToString());

        // 2. State guard
        if (agg.Status != BookingStatus.Held)
            return Error.Conflict("Flights.InvalidState", $"Cannot confirm in state {agg.Status}.");

        // 3. Authorize payment
        var authorizeResult = await payments.AuthorizeAsync(
            agg.TotalAmount!,
            cmd.AggregateId.ToString("N"),
            ct
        );
        if (authorizeResult.IsError)
            return FlightsErrors.PaymentFailed(authorizeResult.FirstError.Description);

        var paymentRef = authorizeResult.Value;

        // 4. Capture payment — on failure: compensate (cancel + refund)
        var captureResult = await payments.CaptureAsync(paymentRef, ct);
        if (captureResult.IsError)
        {
            metrics.RecordPaymentOutcome(false);
            var paymentAuthorized = new PaymentAuthorized(
                paymentRef,
                agg.TotalAmount!,
                time.GetUtcNow()
            );
            var orderCancelled = new OrderCancelled(CancelReason.System, time.GetUtcNow());
            stream.AppendOne(paymentAuthorized);
            stream.AppendOne(orderCancelled);

            var refundResult = await payments.RefundAsync(paymentRef, agg.TotalAmount!, ct);
            if (refundResult.IsError)
                log.LogError(
                    "Refund failed for payment {PaymentRef} after capture failure: {Error}",
                    paymentRef,
                    refundResult.FirstError.Description
                );
            try
            {
                await marten.SaveChangesAsync(ct);
            }
            catch (EventStreamUnexpectedMaxEventIdException)
            {
                return FlightsErrors.ConcurrencyConflict;
            }
            metrics.RecordAggregateEventsAppended(nameof(PaymentAuthorized));
            metrics.RecordAggregateEventsAppended(nameof(OrderCancelled));

            // Apply locally to the in-memory aggregate so the projection reflects the
            // appended events without a redundant Marten re-read (SAGA-M2).
            agg.Apply(paymentAuthorized);
            agg.Apply(orderCancelled);
            await projector.Project(agg, cmd.UserId, time, ct);
            return FlightsErrors.PaymentFailed("Capture failed");
        }

        // 5. Confirm with booking provider (M1: single provider)
        var provider = bookingProviders.Single();
        var confirmResult = await provider.ConfirmOrderAsync(agg.ProviderOrderId!, paymentRef, ct);
        if (confirmResult.IsError)
        {
            metrics.RecordPaymentOutcome(false);
            var paymentAuthorized = new PaymentAuthorized(
                paymentRef,
                agg.TotalAmount!,
                time.GetUtcNow()
            );
            var orderCancelled = new OrderCancelled(CancelReason.System, time.GetUtcNow());
            stream.AppendOne(paymentAuthorized);
            stream.AppendOne(orderCancelled);
            log.LogError(
                "Provider order confirmation failed for aggregate {AggregateId}: {Error}",
                cmd.AggregateId,
                confirmResult.FirstError.Description
            );
            var refundResult = await payments.RefundAsync(paymentRef, agg.TotalAmount!, ct);
            if (refundResult.IsError)
                log.LogError(
                    "Refund failed for payment {PaymentRef} after provider confirmation failure: {Error}",
                    paymentRef,
                    refundResult.FirstError.Description
                );
            try
            {
                await marten.SaveChangesAsync(ct);
            }
            catch (EventStreamUnexpectedMaxEventIdException)
            {
                return FlightsErrors.ConcurrencyConflict;
            }
            metrics.RecordAggregateEventsAppended(nameof(PaymentAuthorized));
            metrics.RecordAggregateEventsAppended(nameof(OrderCancelled));

            agg.Apply(paymentAuthorized);
            agg.Apply(orderCancelled);
            await projector.Project(agg, cmd.UserId, time, ct);
            return FlightsErrors.PaymentFailed("Order confirmation failed at provider");
        }

        // 6. Append PaymentAuthorized + OrderConfirmed, record success metric, and save.
        metrics.RecordPaymentOutcome(true);
        var confirmed = confirmResult.Value;
        var paymentAuthorizedEvt = new PaymentAuthorized(
            paymentRef,
            agg.TotalAmount!,
            time.GetUtcNow()
        );
        var orderConfirmedEvt = new OrderConfirmed(
            confirmed.ProviderOrderId,
            paymentRef,
            time.GetUtcNow()
        );
        stream.AppendOne(paymentAuthorizedEvt);
        stream.AppendOne(orderConfirmedEvt);

        // 7. Enqueue the notification via the outbox BEFORE SaveChangesAsync so it
        //    rides the same Marten transaction as the events. If SaveChangesAsync
        //    rolls back (concurrency conflict, server crash), the buffered message
        //    is discarded along with the events.
        await outbox.PublishAsync(new OrderConfirmedNotification(cmd.AggregateId, cmd.UserId));

        try
        {
            await marten.SaveChangesAsync(ct);
        }
        catch (EventStreamUnexpectedMaxEventIdException)
        {
            return FlightsErrors.ConcurrencyConflict;
        }
        metrics.RecordAggregateEventsAppended(nameof(PaymentAuthorized));
        metrics.RecordAggregateEventsAppended(nameof(OrderConfirmed));

        // 8. Project read model — apply events locally to avoid a redundant re-read.
        agg.Apply(paymentAuthorizedEvt);
        agg.Apply(orderConfirmedEvt);
        await projector.Project(agg, cmd.UserId, time, ct);

        // 9. Return result
        return new ConfirmedOrderResult(
            cmd.AggregateId,
            "Confirmed",
            paymentRef.Value.ToString("N")
        );
    }
}
