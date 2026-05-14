using Travel.Modules.Flights.Application.Queries;
using Wolverine.Attributes;

namespace Travel.Modules.Flights.Application.Handlers.Booking;

public static class ListOrdersHandler
{
    [WolverineHandler]
    public static Task<OrderListView> Handle(
        ListOrdersQuery query,
        IOrderReadModelQueries queries,
        CancellationToken ct
    ) => queries.ListAsync(query.UserId, query.Limit, query.Offset, ct);
}
