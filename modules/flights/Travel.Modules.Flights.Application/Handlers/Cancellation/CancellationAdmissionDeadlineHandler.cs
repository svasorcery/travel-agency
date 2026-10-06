using Marten;
using Travel.Modules.Flights.Application.Cancellation;
using Travel.Modules.Flights.Core.Aggregates;
using Travel.Modules.Flights.Core.Cancellation;
using Wolverine.Attributes;
using Wolverine.Marten;

namespace Travel.Modules.Flights.Application.Handlers.Cancellation;

public static class CancellationAdmissionDeadlineHandler
{
    [NonTransactional]
    [WolverineHandler]
    public static async Task Handle(
        AdmissionDeadline message,
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
        var stage =
            message.ActionStage == CancellationActionStage.Prepare
                ? CancellationUnknownStage.Preparation
            : message.ActionStage == CancellationActionStage.Confirm
                ? CancellationUnknownStage.Confirmation
            : CancellationUnknownStage.None;
        var decision = stream.Aggregate!.DecideCancellationAdmissionDeadline(
            message.OperationId,
            message.AdmissionId,
            stage,
            time.GetUtcNow()
        );
        await CancellationDecisionWriter.Persist(decision, stream, session, outbox, [], ct);
    }
}
