using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Travel.Modules.Flights.Api.Contracts;
using Travel.Modules.Flights.Application.Queries;
using Travel.Shared.Web;
using Wolverine;
using Wolverine.Http;

namespace Travel.Modules.Flights.Api.Endpoints;

public sealed class ListOrdersEndpoint
{
    [WolverineGet("/api/flights/orders")]
    [Authorize]
    public static async Task<IResult> Get(
        HttpContext httpContext,
        IMessageBus bus,
        ILogger<ListOrdersEndpoint> logger,
        CancellationToken ct,
        int limit = 50,
        int offset = 0
    )
    {
        if (!httpContext.User.TryGetUserId(out var userId))
            return Results.Problem(IdentityProblemDetails.InvalidUserIdentity());

        var view = await bus.InvokeAsync<OrderListView>(
            new ListOrdersQuery(userId, limit, offset),
            ct
        );

        return Results.Ok(
            new OrderListResponse(
                Items: view.Items.Select(v => OrderResponseMapper.From(v, logger)).ToArray(),
                Limit: view.Limit,
                Offset: view.Offset
            )
        );
    }
}
