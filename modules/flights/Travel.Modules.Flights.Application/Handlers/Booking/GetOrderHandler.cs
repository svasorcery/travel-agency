using ErrorOr;
using Travel.Modules.Flights.Application.Queries;
using Travel.Modules.Flights.Core.Errors;
using Wolverine.Attributes;

namespace Travel.Modules.Flights.Application.Handlers.Booking;

public static class GetOrderHandler
{
    [WolverineHandler]
    public static async Task<ErrorOr<OrderView>> Handle(
        GetOrderQuery query,
        IOrderReadModelQueries queries,
        CancellationToken ct
    )
    {
        var order = await queries.GetAsync(query.AggregateId, query.UserId, ct);
        return order is null ? FlightsErrors.OfferNotFound(query.AggregateId.ToString()) : order;
    }
}
