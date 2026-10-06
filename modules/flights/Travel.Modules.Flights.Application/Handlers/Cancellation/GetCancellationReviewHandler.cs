using ErrorOr;
using Marten;
using Travel.Modules.Flights.Application.Cancellation;
using Travel.Modules.Flights.Core.Aggregates;
using Wolverine.Attributes;

namespace Travel.Modules.Flights.Application.Handlers.Cancellation;

public static class GetCancellationReviewHandler
{
    [NonTransactional]
    [WolverineHandler]
    public static async Task<ErrorOr<CancellationReviewResult>> Handle(
        GetCancellationReviewQuery query,
        IDocumentSession session,
        TimeProvider time,
        CancellationToken ct
    )
    {
        if (query.AggregateId == Guid.Empty || query.Actor.UserId == Guid.Empty)
            return CancellationDecisionWriter.InvalidCommand;
        var stream = await session.Events.FetchForWriting<BookingAggregate>(query.AggregateId, ct);
        var booking = stream.Aggregate;
        if (booking is null || !booking.HasConsistentMutationOwner)
            return CancellationDecisionWriter.NotFound;
        return CancellationReviewFactory.Create(
            booking,
            stream.CurrentVersion ?? 0,
            time.GetUtcNow()
        );
    }
}
