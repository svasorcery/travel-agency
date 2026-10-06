using ErrorOr;
using Marten;
using Travel.Modules.Flights.Application.Cancellation;
using Travel.Modules.Flights.Core.Aggregates;
using Wolverine.Attributes;

namespace Travel.Modules.Flights.Application.Handlers.Cancellation;

public static class GetCancellationStatusHandler
{
    [NonTransactional]
    [WolverineHandler]
    public static async Task<ErrorOr<CancellationStatusResult>> Handle(
        GetCancellationStatusQuery command,
        IDocumentSession session,
        TimeProvider time,
        CancellationToken ct
    )
    {
        if (command.AggregateId == Guid.Empty || command.UserId == Guid.Empty)
            return CancellationDecisionWriter.InvalidCommand;
        var stream = await session.Events.FetchForWriting<BookingAggregate>(
            command.AggregateId,
            ct
        );
        if (!CancellationDecisionWriter.Owned(stream.Aggregate, command.UserId))
            return CancellationDecisionWriter.NotFound;
        return CancellationStatusFactory.Create(
            stream.Aggregate!,
            stream.CurrentVersion ?? 0,
            null,
            time.GetUtcNow()
        );
    }
}
