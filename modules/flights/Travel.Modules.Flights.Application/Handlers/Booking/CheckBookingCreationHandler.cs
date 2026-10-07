using Marten;
using Travel.Modules.Flights.Application.Booking;
using Travel.Modules.Flights.Application.Cancellation;
using Travel.Modules.Flights.Core.Aggregates;
using Travel.Modules.Flights.Core.Booking;
using Travel.Modules.Flights.Core.Providers;
using Travel.Modules.Flights.Core.Providers.Dtos;
using Wolverine.Attributes;
using Wolverine.Marten;

namespace Travel.Modules.Flights.Application.Handlers.Booking;

public static class CheckBookingCreationHandler
{
    [NonTransactional]
    [WolverineHandler]
    public static async Task Handle(
        CheckBookingCreation message,
        IDocumentSession session,
        IMartenOutbox outbox,
        IEnumerable<IFlightBookingProvider> providers,
        TimeProvider time,
        CancellationToken ct
    )
    {
        var stream = await session.Events.FetchForWriting<BookingAggregate>(
            message.AggregateId,
            ct
        );
        CancellationDecisionWriter.RequireSource(stream.Aggregate);
        var booking = stream.Aggregate!;
        if (
            booking.CurrentCreation is not { } attempt
            || attempt.Id != message.AttemptId
            || attempt.CreationCompleted
            || attempt.Outcome == BookingCreationOutcome.NotCreated
        )
            return;
        var now = time.GetUtcNow();
        if (now < attempt.StartedAt.AddSeconds(150))
            return;
        var remaining = attempt.StartedAt.AddSeconds(180) - now;
        BookingCreationObservation observation = new(
            BookingCreationOutcome.ManualReviewRequired,
            null,
            attempt.KnownOrderId,
            attempt.KnownOrderId is not null,
            attempt.SenderCompleted,
            "RecoveryUnproven",
            now
        );
        if (
            remaining > TimeSpan.Zero
            && attempt.KnownOrderId is { } order
            && attempt.SenderCompleted
        )
        {
            using var budget = new CancellationTokenSource(
                remaining < TimeSpan.FromSeconds(10) ? remaining : TimeSpan.FromSeconds(10),
                time
            );
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, budget.Token);
            try
            {
                var facts = await providers
                    .Single()
                    .ReadOrderForBookingAsync(
                        order,
                        BookingCreationWriter.Offer(booking, attempt.Accepted, time),
                        booking.QuoteBinding!,
                        attempt.Accepted,
                        attempt.Id,
                        linked.Token
                    );
                if (
                    !facts.IsError
                    && facts.Value.AwaitingPayment
                    && !facts.Value.Cancelled
                    && facts.Value.PaymentRequiredBy > time.GetUtcNow()
                )
                    observation = new(
                        facts.Value.Matches(attempt.Accepted)
                            ? BookingCreationOutcome.Matches
                            : BookingCreationOutcome.CreatedWithDifferences,
                        facts.Value,
                        order,
                        true,
                        true,
                        "OrderObserved",
                        time.GetUtcNow()
                    );
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                observation = observation with { Reason = "RecoveryReadFailed" };
            }
        }
        // Use a fresh version after the awaited read; an inline result may have completed meanwhile.
        await using var finalSession = session.DocumentStore.LightweightSession();
        var current = await finalSession.Events.FetchForWriting<BookingAggregate>(
            message.AggregateId,
            ct
        );
        CancellationDecisionWriter.RequireSource(current.Aggregate);
        await BookingCreationWriter.Observe(
            current,
            finalSession,
            outbox,
            message.AttemptId,
            observation,
            time,
            ct
        );
    }
}
