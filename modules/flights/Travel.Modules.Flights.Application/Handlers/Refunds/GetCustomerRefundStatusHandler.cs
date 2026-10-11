using ErrorOr;
using Marten;
using Travel.Modules.Flights.Application.Refunds;
using Travel.Modules.Flights.Core.Aggregates;
using Wolverine.Attributes;

namespace Travel.Modules.Flights.Application.Handlers.Refunds;

public static class GetCustomerRefundStatusHandler
{
    [NonTransactional]
    [WolverineHandler]
    public static async Task<ErrorOr<CustomerRefundStatusResult>> Handle(
        GetCustomerRefundStatusQuery query,
        IDocumentSession session,
        TimeProvider time,
        CancellationToken ct
    )
    {
        if (query.AggregateId == Guid.Empty || query.UserId == Guid.Empty)
            return Error.Validation(
                "Flights.CustomerRefundRequestInvalid",
                "Customer refund request invalid."
            );
        var stream = await session.Events.FetchForWriting<BookingAggregate>(query.AggregateId, ct);
        var b = stream.Aggregate;
        if (b is null || !b.HasConsistentMutationOwner || b.OwnerUserId != query.UserId)
            return Error.NotFound("Flights.BookingNotFound", "Booking not found.");
        return CustomerRefundStatusFactory.Create(b, stream.CurrentVersion ?? 0, time.GetUtcNow());
    }
}
