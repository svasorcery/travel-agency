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
        IMessageBus bus,
        TimeProvider time,
        ILogger<ConfirmOrderCommand> log,
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
        marten.Events.Append(
            cmd.AggregateId,
            new PaymentAuthorized(paymentRef, agg.TotalAmount!, time.GetUtcNow())
        );

        // 4. Capture payment — on failure: compensate (cancel + refund)
        var captureResult = await payments.CaptureAsync(paymentRef, ct);
        if (captureResult.IsError)
        {
            metrics.RecordPaymentOutcome(false);
            marten.Events.Append(
                cmd.AggregateId,
                new OrderCancelled(CancelReason.System, time.GetUtcNow())
            );
            var refundResult = await payments.RefundAsync(paymentRef, agg.TotalAmount!, ct);
            if (refundResult.IsError)
                log.LogError(
                    "Refund failed for payment {PaymentRef} after capture failure: {Error}",
                    paymentRef,
                    refundResult.FirstError.Description
                );
            await marten.SaveChangesAsync(ct);
            metrics.RecordAggregateEventsAppended(nameof(PaymentAuthorized));
            metrics.RecordAggregateEventsAppended(nameof(OrderCancelled));
            var cancelledAgg = await marten.Events.AggregateStreamAsync<BookingAggregate>(
                cmd.AggregateId,
                token: ct
            );
            if (cancelledAgg is not null)
                await projector.Project(cancelledAgg, cmd.UserId, time, ct);
            return FlightsErrors.PaymentFailed("Capture failed");
        }

        // 5. Confirm with booking provider (M1: single provider)
        var provider = bookingProviders.Single();
        var confirmResult = await provider.ConfirmOrderAsync(agg.ProviderOrderId!, paymentRef, ct);
        if (confirmResult.IsError)
        {
            metrics.RecordPaymentOutcome(false);
            marten.Events.Append(
                cmd.AggregateId,
                new OrderCancelled(CancelReason.System, time.GetUtcNow())
            );
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
            await marten.SaveChangesAsync(ct);
            metrics.RecordAggregateEventsAppended(nameof(PaymentAuthorized));
            metrics.RecordAggregateEventsAppended(nameof(OrderCancelled));
            var cancelledAgg = await marten.Events.AggregateStreamAsync<BookingAggregate>(
                cmd.AggregateId,
                token: ct
            );
            if (cancelledAgg is not null)
                await projector.Project(cancelledAgg, cmd.UserId, time, ct);
            return FlightsErrors.PaymentFailed("Order confirmation failed at provider");
        }

        // 6. Append OrderConfirmed, record success metric, and save
        metrics.RecordPaymentOutcome(true);
        var confirmed = confirmResult.Value;
        marten.Events.Append(
            cmd.AggregateId,
            new OrderConfirmed(confirmed.ProviderOrderId, paymentRef, time.GetUtcNow())
        );
        await marten.SaveChangesAsync(ct);
        metrics.RecordAggregateEventsAppended(nameof(PaymentAuthorized));
        metrics.RecordAggregateEventsAppended(nameof(OrderConfirmed));

        // 7. Project read model
        var updatedAgg = await marten.Events.AggregateStreamAsync<BookingAggregate>(
            cmd.AggregateId,
            token: ct
        );
        await projector.Project(updatedAgg!, cmd.UserId, time, ct);

        // 8. Publish notification
        await bus.PublishAsync(new OrderConfirmedNotification(cmd.AggregateId, cmd.UserId));

        // 9. Return result
        return new ConfirmedOrderResult(
            cmd.AggregateId,
            "Confirmed",
            paymentRef.Value.ToString("N")
        );
    }
}
