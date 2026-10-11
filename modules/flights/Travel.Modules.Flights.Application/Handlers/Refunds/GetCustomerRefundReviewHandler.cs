using ErrorOr;
using Marten;
using Travel.Modules.Flights.Application.Refunds;
using Travel.Modules.Flights.Core.Aggregates;
using Wolverine.Attributes;

namespace Travel.Modules.Flights.Application.Handlers.Refunds;

public static class GetCustomerRefundReviewHandler
{
    [NonTransactional]
    [WolverineHandler]
    public static async Task<ErrorOr<CustomerRefundReviewResult>> Handle(
        GetCustomerRefundReviewQuery query,
        IDocumentSession session,
        TimeProvider time,
        CancellationToken ct
    )
    {
        if (query.AggregateId == Guid.Empty || query.ActorId == Guid.Empty)
            return Error.Validation(
                "Flights.CustomerRefundRequestInvalid",
                "Customer refund request invalid."
            );
        var stream = await session.Events.FetchForWriting<BookingAggregate>(query.AggregateId, ct);
        var b = stream.Aggregate;
        if (b is null || !b.HasConsistentMutationOwner)
            return Error.NotFound("Flights.BookingNotFound", "Booking not found.");
        return new CustomerRefundReviewResult(
            CustomerRefundStatusFactory.Create(b, stream.CurrentVersion ?? 0, time.GetUtcNow()),
            b.CustomerRefund,
            b.CustomerRefundReviewHistory.Select(x => new CustomerRefundReviewAudit(
                    x.ResolutionId,
                    x.ActorId,
                    x.Decision.ToString(),
                    x.EvidenceReference,
                    x.OccurredAt
                ))
                .ToArray()
        );
    }
}
