using ErrorOr;
using Marten;
using Travel.Modules.Flights.Application.Cancellation;
using Travel.Modules.Flights.Core.Aggregates;
using Travel.Modules.Flights.Core.Cancellation;
using Travel.Modules.Flights.Core.DomainEvents;
using Travel.Modules.Flights.Core.Providers;
using Travel.Modules.Flights.Core.Providers.Dtos;
using Wolverine.Attributes;
using Wolverine.Marten;

namespace Travel.Modules.Flights.Application.Handlers.Cancellation;

public static class ObserveCancellationHandler
{
    [NonTransactional]
    [WolverineHandler]
    public static async Task Handle(
        ObserveCancellation message,
        IDocumentSession session,
        IDocumentStore store,
        IMartenOutbox outbox,
        IFlightCancellationProvider provider,
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
        if (!booking.CancellationOperations.TryGetValue(message.OperationId, out var op))
            return;
        var readId = message.Slot == -1 ? message.RefreshRequestId ?? Guid.Empty : Guid.NewGuid();
        var decision = booking.DecideCancellationObserve(
            op.Id,
            op.Revision,
            message.RecoveryEpoch,
            message.Slot,
            readId,
            time.GetUtcNow()
        );
        var reservation = decision
            .Events.OfType<CancellationObservationStarted>()
            .SingleOrDefault();
        await CancellationDecisionWriter.Persist(decision, stream, session, outbox, [], ct);
        if (reservation is not { ReadAllowed: true } || reservation.ReadId == Guid.Empty)
            return;
        var remaining = reservation.Recovery.ReadWindowUntil - time.GetUtcNow();
        if (remaining is null || remaining <= TimeSpan.Zero)
            return;
        using var roundBudget = new CancellationTokenSource(remaining.Value, time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, roundBudget.Token);
        ErrorOr<CancellationObservation> result;
        var correlation = new CancellationCorrelation(
            op.ProviderOrderRef,
            op.ProviderCancellationRef,
            op.Terms,
            op.ItineraryPartyHash
        );
        try
        {
            result = await provider.ObserveAsync(correlation, linked.Token);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            result = Error.Failure(
                "Flights.CancellationObservationUnavailable",
                "Supplier observation unavailable."
            );
        }
        await using var fresh = store.LightweightSession();
        var current = await fresh.Events.FetchForWriting<BookingAggregate>(message.AggregateId, ct);
        CancellationDecisionWriter.RequireSource(current.Aggregate);
        if (!current.Aggregate!.CancellationOperations.TryGetValue(message.OperationId, out op))
            return;
        var now = time.GetUtcNow();
        var observation = result.IsError
            ? new CancellationObservation(
                op.ProviderOrderRef,
                op.ProviderCancellationRef,
                CancellationObservationState.Unknown,
                null,
                null,
                now,
                CancellationResolutionSource.SupplierApi,
                CancellationReason.ProviderUnavailable
            )
            : result.Value;
        var completion = current.Aggregate.DecideCancellationObservation(
            op.Id,
            op.Revision,
            reservation.ReadId,
            observation,
            now
        );
        await CancellationDecisionWriter.Persist(completion, current, fresh, outbox, [], ct);
    }
}
