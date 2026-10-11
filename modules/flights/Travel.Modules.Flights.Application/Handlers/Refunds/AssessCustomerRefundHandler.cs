using Marten;
using Travel.Modules.Flights.Application.Booking;
using Travel.Modules.Flights.Application.ReadModels;
using Travel.Modules.Flights.Application.Refunds;
using Travel.Modules.Flights.Core.Aggregates;
using Wolverine.Attributes;
using Wolverine.Marten;

namespace Travel.Modules.Flights.Application.Handlers.Refunds;

public static class AssessCustomerRefundHandler
{
    [NonTransactional]
    [WolverineHandler]
    public static async Task Handle(
        AssessCustomerRefund message,
        IDocumentSession session,
        IMartenOutbox outbox,
        CustomerRefundAvailability availability,
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
        var result = b.DecideCustomerRefundAdmission(
            b.OwnerUserId!.Value,
            message.CancellationId,
            Guid.NewGuid(),
            time.GetUtcNow(),
            availability.CanExecute
        );
        if (result.IsError)
            throw new BookingProjectionTerminalException("CustomerRefundAdmissionRejected");
        await CustomerRefundWriter.Persist(result.Value, stream, session, outbox, ct);
    }
}
