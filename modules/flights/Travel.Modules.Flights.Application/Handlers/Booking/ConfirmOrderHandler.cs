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

public static class ConfirmOrderHandler
{
    // The explicit booking helper owns the commit and conflict translation.
    // Do not let generated middleware attempt a second save after a rejected write.
    [NonTransactional]
    [WolverineHandler]
    public static async Task<ErrorOr<ConfirmedOrderResult>> Handle(
        ConfirmOrderCommand cmd,
        IDocumentSession marten,
        IEnumerable<IFlightBookingProvider> bookingProviders,
        IPaymentGateway payments,
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
                ["correlation_id"] =
                    System.Diagnostics.Activity.Current?.TraceId.ToString() ?? string.Empty,
            }
        );

        // 1. Load aggregate with optimistic concurrency tracking. FetchForWriting captures
        //    the expected version at load time; AppendOne + SaveChangesAsync enforces it.
        var stream = await marten.Events.FetchForWriting<BookingAggregate>(cmd.AggregateId, ct);
        var agg = stream.Aggregate;
        var requiredVersion = stream.CurrentVersion + 2;
        if (agg is null)
            return FlightsErrors.OfferNotFound(cmd.AggregateId.ToString());

        // 2. Domain decisions before any provider or payment side effect
        var ownerDecision = agg.DecideOwner(BookingTransition.Confirm, cmd.UserId);
        if (ownerDecision is BookingTransitionDecision.Rejected ownerRejected)
            return BookingTransitionErrorMapper.ToOwnerError(ownerRejected.Reason, cmd.AggregateId);

        var transitionDecision = agg.DecideConfirm(time.GetUtcNow());
        if (transitionDecision is BookingTransitionDecision.Rejected transitionRejected)
            return BookingTransitionErrorMapper.ToError(transitionRejected.Reason);

        // --- Concurrency note: side effects precede the optimistic-write boundary ---
        //
        // Steps 3–5 (Authorize, Capture, ProviderConfirm) execute *before* SaveChangesAsync.
        // Under Marten optimistic concurrency, a concurrent confirm races to the same stream
        // version; the loser's SaveChangesAsync throws EventStreamUnexpectedMaxEventIdException
        // and is mapped to Flights.ConcurrencyConflict — but both threads have already run
        // steps 3–5. This is the intentional current design; moving these calls inside the
        // write boundary requires a saga refactor that is explicitly out of scope for WS2.
        //
        // Production safety of this arrangement relies on two guarantees:
        //
        // 1. Authorize is keyed by `cmd.AggregateId.ToString("N")` (see line below).
        //    This key is stable across all concurrent and retry calls that target the same
        //    booking, so the real Duffel gateway deduplicates both authorizations server-side
        //    (no double-charge).
        //
        // 2. ConfirmOrderAsync is keyed by `cmd.AggregateId.ToString("N")` (see the call at
        //    step 5 below). Both the Authorize and ConfirmOrderAsync calls share this same
        //    stable booking identifier as the Duffel Idempotency-Key, so the provider
        //    server-side deduplicates concurrent and retry calls for both operations.

        // 3. Authorize payment
        var paymentSw = Stopwatch.StartNew();
        var authorizeResult = await payments.AuthorizeAsync(
            agg.TotalAmount!,
            cmd.AggregateId.ToString("N"),
            ct
        );
        if (authorizeResult.IsError)
        {
            paymentSw.Stop();
            metrics.RecordPaymentDuration(paymentSw.Elapsed.TotalMilliseconds, "authorize_failed");
            return FlightsErrors.PaymentFailed(authorizeResult.FirstError.Description);
        }

        var paymentRef = authorizeResult.Value;

        // 4. Capture payment — on failure: compensate (cancel + refund)
        var captureResult = await payments.CaptureAsync(paymentRef, ct);
        if (captureResult.IsError)
        {
            paymentSw.Stop();
            metrics.RecordPaymentDuration(paymentSw.Elapsed.TotalMilliseconds, "failure");
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
            var captureSaveResult = await marten.SaveOrConcurrencyConflictAsync(
                outbox,
                cmd.AggregateId,
                [],
                ct
            );
            if (captureSaveResult.IsError)
                return captureSaveResult.Errors;
            metrics.RecordAggregateEventsAppended(nameof(PaymentAuthorized));
            metrics.RecordAggregateEventsAppended(nameof(OrderCancelled));

            return FlightsErrors.PaymentFailed("Capture failed");
        }

        // 5. Confirm with booking provider (M1: single provider)
        // Pass the booking's stable AggregateId as the idempotency key so the Duffel
        // gateway deduplicates concurrent/retry confirm calls (WS4 Task 4.2).
        var provider = bookingProviders.Single();
        var confirmResult = await provider.ConfirmOrderAsync(
            agg.ProviderOrderId!,
            paymentRef,
            cmd.AggregateId.ToString("N"),
            ct
        );
        if (confirmResult.IsError)
        {
            paymentSw.Stop();
            metrics.RecordPaymentDuration(paymentSw.Elapsed.TotalMilliseconds, "failure");
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
            var confirmSaveResult = await marten.SaveOrConcurrencyConflictAsync(
                outbox,
                cmd.AggregateId,
                [],
                ct
            );
            if (confirmSaveResult.IsError)
                return confirmSaveResult.Errors;
            metrics.RecordAggregateEventsAppended(nameof(PaymentAuthorized));
            metrics.RecordAggregateEventsAppended(nameof(OrderCancelled));

            return FlightsErrors.PaymentFailed("Order confirmation failed at provider");
        }

        // 6. Append PaymentAuthorized + OrderConfirmed, record success metric, and save.
        paymentSw.Stop();
        metrics.RecordPaymentDuration(paymentSw.Elapsed.TotalMilliseconds, "success");
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

        using var transitionSpan = FlightsActivitySource.Source.StartActivity(
            "booking.event.OrderConfirmed",
            ActivityKind.Internal
        );
        transitionSpan?.SetTag("aggregate.id", cmd.AggregateId.ToString());
        transitionSpan?.SetTag("aggregate.version", stream.CurrentVersion + 2);

        // 7. Commit events, exact-version notification and reconciliation atomically.

        var saveResult = await marten.SaveOrConcurrencyConflictAsync(
            outbox,
            cmd.AggregateId,
            [new OrderConfirmedNotification(cmd.AggregateId, cmd.UserId, requiredVersion)],
            ct
        );
        if (saveResult.IsError)
            return saveResult.Errors;
        metrics.RecordAggregateEventsAppended(nameof(PaymentAuthorized));
        metrics.RecordAggregateEventsAppended(nameof(OrderConfirmed));

        // 9. Record conversion metric and return result.
        metrics.RecordOrderBooked();
        return new ConfirmedOrderResult(
            cmd.AggregateId,
            "Confirmed",
            paymentRef.Value.ToString("N")
        );
    }
}
