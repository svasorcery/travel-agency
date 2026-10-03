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

        // External effects still precede the optimistic event commit. The test wallet is
        // in-memory; neither it nor the supplier provides a cross-client/restart guarantee.
        var provider = bookingProviders.Single();
        var preflight = await RunConfirmationStepAsync(() =>
            provider.ValidateConfirmationAsync(agg.ProviderOrderId!, agg.TotalAmount!, ct)
        );
        if (preflight.IsError)
            return preflight.Errors;

        var paymentSw = Stopwatch.StartNew();
        var authorizeResult = await RunConfirmationStepAsync(() =>
            payments.AuthorizeAsync(agg.TotalAmount!, cmd.AggregateId.ToString("N"), ct)
        );
        if (authorizeResult.IsError)
        {
            paymentSw.Stop();
            metrics.RecordPaymentDuration(paymentSw.Elapsed.TotalMilliseconds, "authorize_failed");
            return authorizeResult.FirstError.Code == FlightsErrors.ConfirmationOutcomeUnknown.Code
                ? FlightsErrors.ConfirmationOutcomeUnknown
                : FlightsErrors.PaymentFailed("Payment authorization failed.");
        }

        var paymentRef = authorizeResult.Value;
        var captureResult = await RunConfirmationStepAsync(() =>
            payments.CaptureAsync(paymentRef, ct)
        );
        if (captureResult.IsError)
        {
            paymentSw.Stop();
            metrics.RecordPaymentDuration(paymentSw.Elapsed.TotalMilliseconds, "failure");
            metrics.RecordPaymentOutcome(false);
            return FlightsErrors.ConfirmationOutcomeUnknown;
        }

        var confirmResult = await RunConfirmationStepAsync(() =>
            provider.ConfirmOrderAsync(agg.ProviderOrderId!, paymentRef, agg.TotalAmount!, ct)
        );
        if (confirmResult.IsError)
        {
            paymentSw.Stop();
            metrics.RecordPaymentDuration(paymentSw.Elapsed.TotalMilliseconds, "failure");
            metrics.RecordPaymentOutcome(false);
            // Capture already ran. A final price/deadline rejection does not undo wallet effects.
            return FlightsErrors.ConfirmationOutcomeUnknown;
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

    private static async Task<ErrorOr<T>> RunConfirmationStepAsync<T>(
        Func<Task<ErrorOr<T>>> operation
    )
    {
        try
        {
            return await operation();
        }
        catch (TaskCanceledException cancelled)
        {
            throw new TaskCanceledException(
                "Confirmation was cancelled.",
                null,
                cancelled.CancellationToken
            );
        }
        catch (OperationCanceledException cancelled)
        {
            throw new OperationCanceledException(
                "Confirmation was cancelled.",
                cancelled.CancellationToken
            );
        }
        catch (Exception)
        {
            return FlightsErrors.ConfirmationOutcomeUnknown;
        }
    }
}
