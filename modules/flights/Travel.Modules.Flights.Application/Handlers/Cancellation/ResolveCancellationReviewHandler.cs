using ErrorOr;
using Marten;
using Travel.Modules.Flights.Application.Cancellation;
using Travel.Modules.Flights.Core.Aggregates;
using Wolverine.Attributes;
using Wolverine.Marten;

namespace Travel.Modules.Flights.Application.Handlers.Cancellation;

public static class ResolveCancellationReviewHandler
{
    [NonTransactional]
    [WolverineHandler]
    public static async Task<ErrorOr<CancellationStatusResult>> Handle(
        ResolveCancellationReviewCommand command,
        IDocumentSession session,
        IMartenOutbox outbox,
        TimeProvider time,
        CancellationToken ct
    )
    {
        if (
            command.Actor.UserId == Guid.Empty
            || command.Input is null
            || command.Input.AggregateId == Guid.Empty
        )
            return CancellationDecisionWriter.InvalidCommand;
        var stream = await session.Events.FetchForWriting<BookingAggregate>(
            command.Input.AggregateId,
            ct
        );
        if (stream.Aggregate is null || !stream.Aggregate.HasConsistentMutationOwner)
            return CancellationDecisionWriter.NotFound;
        var now = time.GetUtcNow();
        var decision = stream.Aggregate.DecideManualResolution(
            command.Actor.UserId,
            command.Input,
            now
        );
        if (
            command.Input.TargetKind
            != Travel.Modules.Flights.Core.Cancellation.ManualResolutionTargetKind.Cancellation
        )
            decision = decision with { ResolvedOperationId = null };
        return await CancellationDecisionWriter.Command(
            decision,
            stream,
            session,
            outbox,
            [],
            now,
            ct
        );
    }
}
