using ErrorOr;
using Marten;
using Travel.Modules.Flights.Application.Cancellation;
using Travel.Modules.Flights.Core.Aggregates;
using Wolverine.Attributes;
using Wolverine.Marten;

namespace Travel.Modules.Flights.Application.Handlers.Cancellation;

public static class PrepareCancellationHandler
{
    [NonTransactional]
    [WolverineHandler]
    public static async Task<ErrorOr<CancellationStatusResult>> Handle(
        PrepareCancellationCommand cmd,
        IDocumentSession session,
        IMartenOutbox outbox,
        TimeProvider time,
        CancellationToken ct
    )
    {
        if (
            cmd.AggregateId == Guid.Empty
            || cmd.UserId == Guid.Empty
            || cmd.OperationId == Guid.Empty
            || cmd.ExpectedBookingVersion <= 0
        )
            return CancellationDecisionWriter.InvalidCommand;
        var stream = await session.Events.FetchForWriting<BookingAggregate>(cmd.AggregateId, ct);
        var booking = stream.Aggregate;
        if (!CancellationDecisionWriter.Owned(booking, cmd.UserId))
            return CancellationDecisionWriter.NotFound;
        string scope;
        if (booking!.CancellationOperations.TryGetValue(cmd.OperationId, out var prior))
            scope = prior.ItineraryPartyHash;
        else
        {
            var bound = CancellationScope.Create(booking);
            if (bound.IsError)
                return bound.Errors;
            scope = bound.Value;
        }
        var now = time.GetUtcNow();
        var admission = Guid.NewGuid();
        var decision = booking.DecideCancellationPrepare(
            cmd.UserId,
            cmd.OperationId,
            cmd.ExpectedBookingVersion,
            cmd.Fingerprint,
            admission,
            now,
            scope
        );
        return await CancellationDecisionWriter.Command(
            decision,
            stream,
            session,
            outbox,
            CancellationWorkFactory.PreparationAdmission(
                cmd.AggregateId,
                cmd.OperationId,
                admission,
                now
            ),
            now,
            ct
        );
    }
}
