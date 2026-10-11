using Marten;
using Travel.Modules.Flights.Application.Booking;
using Travel.Modules.Flights.Application.Cancellation;
using Travel.Modules.Flights.Application.ReadModels;
using Travel.Modules.Flights.Application.Refunds;
using Travel.Modules.Flights.Core.Aggregates;
using Travel.Modules.Flights.Core.Providers;
using Travel.Modules.Flights.Core.Refunds;
using Wolverine.Attributes;
using Wolverine.Marten;

namespace Travel.Modules.Flights.Application.Handlers.Refunds;

public static class ExecuteCustomerRefundHandler
{
    [NonTransactional]
    [WolverineHandler]
    public static async Task Handle(
        ExecuteCustomerRefund message,
        IDocumentSession session,
        IDocumentStore store,
        IMartenOutbox outbox,
        ICustomerRefundGateway gateway,
        IDispatchInstanceIdentity instance,
        TimeProvider time,
        CancellationToken ct
    )
    {
        var stream = await session.Events.FetchForWriting<BookingAggregate>(
            message.AggregateId,
            ct
        );
        var b = stream.Aggregate;
        if (b is null || !b.HasConsistentMutationOwner)
            throw new BookingSourceOwnershipMissingException(
                "Customer refund source owner missing."
            );
        if (
            b.CustomerRefund is not { } current
            || current.Id != message.RefundId
            || current.ClaimedAt is not null
            || current.State != CustomerRefundState.Ready
        )
            return;
        var now = time.GetUtcNow();
        if (now >= current.DeadlineAt)
        {
            var deadline = b.DecideCustomerRefundDeadline(current.Id, current.DeadlineAt, now);
            if (!deadline.IsError)
                await CustomerRefundWriter.Persist(deadline.Value, stream, session, outbox, ct);
            return;
        }
        var claim = b.DecideCustomerRefundClaim(current.Id, current.Revision, instance.Id, now);
        if (claim.IsError || claim.Value.Events.Count == 0)
            return;
        await CustomerRefundWriter.Persist(claim.Value, stream, session, outbox, ct);

        var remaining = current.DeadlineAt - time.GetUtcNow();
        if (remaining <= TimeSpan.Zero)
            return;
        using var budget = new CancellationTokenSource(
            remaining < TimeSpan.FromSeconds(120) ? remaining : TimeSpan.FromSeconds(120),
            time
        );
        await using var checkSession = store.LightweightSession();
        var check = await checkSession.Events.FetchForWriting<BookingAggregate>(
            message.AggregateId,
            budget.Token
        );
        if (check.Aggregate?.CanContinueCustomerRefund(message.RefundId, instance.Id) != true)
            return;
        current = check.Aggregate.CustomerRefund!;
        if (time.GetUtcNow() >= current.DeadlineAt)
            return;
        CustomerRefundObservation? observation;
        try
        {
            var result = await gateway.ExecuteAsync(
                new(
                    current.Id,
                    current.Basis!,
                    current.Amount!,
                    CustomerRefundDestination.OriginalFictionalPaymentMethod
                ),
                budget.Token
            );
            observation = result.IsError ? null : result.Value;
        }
        catch (Exception)
        {
            // Never retain provider messages, tokens or raw response bodies.
            observation = null;
        }
        await using var resultSession = store.LightweightSession();
        var resultStream = await resultSession.Events.FetchForWriting<BookingAggregate>(
            message.AggregateId,
            CancellationToken.None
        );
        if (resultStream.Aggregate is null || !resultStream.Aggregate.HasConsistentMutationOwner)
            throw new BookingSourceOwnershipMissingException(
                "Customer refund source owner missing."
            );
        var decision = resultStream.Aggregate.DecideCustomerRefundOutcome(
            message.RefundId,
            instance.Id,
            observation,
            time.GetUtcNow()
        );
        if (decision.IsError)
            throw new BookingProjectionTerminalException("CustomerRefundOutcomeUnbound");
        await CustomerRefundWriter.Persist(
            decision.Value,
            resultStream,
            resultSession,
            outbox,
            CancellationToken.None
        );
    }
}
