using Marten;
using Travel.Modules.Flights.Application.Booking;
using Travel.Modules.Flights.Application.ReadModels;
using Travel.Modules.Flights.Application.Refunds;
using Travel.Modules.Flights.Core.Aggregates;
using Wolverine.Attributes;
using Wolverine.Marten;

namespace Travel.Modules.Flights.Application.Handlers.Refunds;

public static class CustomerRefundDeadlineHandler
{
    [NonTransactional]
    [WolverineHandler]
    public static async Task Handle(
        CustomerRefundDeadline message,
        IDocumentSession session,
        IMartenOutbox outbox,
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
        var decision = b.DecideCustomerRefundDeadline(
            message.RefundId,
            message.DeadlineAt,
            time.GetUtcNow()
        );
        if (decision.IsError)
            throw new BookingProjectionTerminalException("CustomerRefundDeadlineUnbound");
        await CustomerRefundWriter.Persist(decision.Value, stream, session, outbox, ct);
    }
}
