using ErrorOr;
using Marten;
using Travel.Modules.Flights.Application.Cancellation;
using Travel.Modules.Flights.Core.Aggregates;
using Wolverine.Attributes;
using Wolverine.Marten;

namespace Travel.Modules.Flights.Application.Handlers.Cancellation;

public static class AbandonCancellationHandler
{
    [NonTransactional]
    [WolverineHandler]
    public static async Task<ErrorOr<CancellationStatusResult>> Handle(
        AbandonCancellationCommand command,
        IDocumentSession session,
        IMartenOutbox outbox,
        TimeProvider time,
        CancellationToken ct
    )
    {
        if (
            command.AggregateId == Guid.Empty
            || command.UserId == Guid.Empty
            || command.OperationId == Guid.Empty
            || command.ExpectedOperationRevision <= 0
        )
            return CancellationDecisionWriter.InvalidCommand;
        var stream = await session.Events.FetchForWriting<BookingAggregate>(
            command.AggregateId,
            ct
        );
        if (!CancellationDecisionWriter.Owned(stream.Aggregate, command.UserId))
            return CancellationDecisionWriter.NotFound;
        var now = time.GetUtcNow();
        return await CancellationDecisionWriter.Command(
            stream.Aggregate!.DecideCancellationAbandon(
                command.UserId,
                command.OperationId,
                command.ExpectedOperationRevision,
                now
            ),
            stream,
            session,
            outbox,
            [],
            now,
            ct
        );
    }
}
