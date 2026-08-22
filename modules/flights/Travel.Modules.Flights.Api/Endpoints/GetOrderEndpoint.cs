using ErrorOr;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Travel.Modules.Flights.Api.Contracts;
using Travel.Modules.Flights.Application.Queries;
using Travel.Shared.Web;
using Wolverine;
using Wolverine.Http;

namespace Travel.Modules.Flights.Api.Endpoints;

public sealed class GetOrderEndpoint
{
    [WolverineGet("/api/flights/orders/{aggregateId:guid}")]
    [Authorize]
    public static async Task<IResult> Get(
        Guid aggregateId,
        HttpContext httpContext,
        IMessageBus bus,
        ILogger<GetOrderEndpoint> logger,
        CancellationToken ct
    )
    {
        if (!httpContext.User.TryGetUserId(out var userId))
            return Results.Problem(IdentityProblemDetails.InvalidUserIdentity());

        var result = await bus.InvokeAsync<ErrorOr<OrderView>>(
            new GetOrderQuery(aggregateId, userId),
            ct
        );
        if (result.IsError)
            return Results.Problem(result.Errors.ToProblemDetails());

        return Results.Ok(OrderResponseMapper.From(result.Value, logger));
    }
}
