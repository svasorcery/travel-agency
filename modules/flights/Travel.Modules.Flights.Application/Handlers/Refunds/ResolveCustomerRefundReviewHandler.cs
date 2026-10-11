using ErrorOr;
using Marten;
using Travel.Modules.Flights.Application.Booking;
using Travel.Modules.Flights.Application.Refunds;
using Travel.Modules.Flights.Core.Aggregates;
using Wolverine.Attributes;
using Wolverine.Marten;

namespace Travel.Modules.Flights.Application.Handlers.Refunds;

public static class ResolveCustomerRefundReviewHandler
{
    [NonTransactional]
    [WolverineHandler]
    public static async Task<ErrorOr<CustomerRefundStatusResult>> Handle(
        ResolveCustomerRefundReviewCommand command,
        IDocumentSession session,
        IMartenOutbox outbox,
        TimeProvider time,
        CancellationToken ct
    )
    {
        if (
            command.AggregateId == Guid.Empty
            || command.ActorId == Guid.Empty
            || command.Input is null
        )
            return Error.Validation(
                "Flights.CustomerRefundRequestInvalid",
                "Customer refund request invalid."
            );
        var stream = await session.Events.FetchForWriting<BookingAggregate>(
            command.AggregateId,
            ct
        );
        var b = stream.Aggregate;
        if (b is null || !b.HasConsistentMutationOwner)
            return Error.NotFound("Flights.BookingNotFound", "Booking not found.");
        var now = time.GetUtcNow();
        var decision = b.DecideCustomerRefundReview(command.ActorId, command.Input, now);
        if (decision.IsError)
            return decision.Errors;
        var version = (stream.CurrentVersion ?? 0) + decision.Value.Events.Count;
        try
        {
            await CustomerRefundWriter.Persist(decision.Value, stream, session, outbox, ct);
        }
        catch (BookingWriteConflictException)
        {
            return Error.Conflict("Flights.ConcurrencyConflict", "Booking changed concurrently.");
        }
        return CustomerRefundStatusFactory.Create(b, version, now);
    }
}
