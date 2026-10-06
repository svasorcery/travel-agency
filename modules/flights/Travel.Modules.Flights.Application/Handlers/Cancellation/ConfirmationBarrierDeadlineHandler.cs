using Marten;
using Travel.Modules.Flights.Application.Cancellation;
using Travel.Modules.Flights.Core.Aggregates;
using Wolverine.Attributes;
using Wolverine.Marten;

namespace Travel.Modules.Flights.Application.Handlers.Cancellation;

public static class ConfirmationBarrierDeadlineHandler
{
    [NonTransactional]
    [WolverineHandler]
    public static async Task Handle(
        ConfirmationBarrierDeadline message,
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
        CancellationDecisionWriter.RequireSource(stream.Aggregate);
        var decision = stream.Aggregate!.DecideConfirmationDeadline(
            message.AttemptId,
            message.AdmissionId,
            time.GetUtcNow()
        );
        await CancellationDecisionWriter.Persist(decision, stream, session, outbox, [], ct);
    }
}
