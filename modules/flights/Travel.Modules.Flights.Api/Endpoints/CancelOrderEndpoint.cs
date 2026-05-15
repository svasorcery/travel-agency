using ErrorOr;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Travel.Modules.Flights.Api.Contracts;
using Travel.Modules.Flights.Application.Commands;
using Travel.Modules.Flights.Application.Queries;
using Travel.Shared.Web;
using Wolverine;
using Wolverine.Http;

namespace Travel.Modules.Flights.Api.Endpoints;

public sealed class CancelOrderEndpoint
{
    [WolverinePost("/api/flights/orders/{aggregateId:guid}/cancel")]
    [Authorize("flights:book")]
    public static async Task<IResult> Post(
        Guid aggregateId,
        HttpContext httpContext,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var userId = httpContext.User.GetUserId();

        var cancelResult = await bus.InvokeAsync<ErrorOr<CancelledOrderResult>>(
            new CancelOrderCommand(aggregateId, userId),
            ct
        );
        if (cancelResult.IsError)
            return Results.Problem(cancelResult.Errors.ToProblemDetails());

        // Fetch the updated order to build a full OrderResponse
        var orderResult = await bus.InvokeAsync<ErrorOr<OrderView>>(
            new GetOrderQuery(aggregateId, userId),
            ct
        );
        if (orderResult.IsError)
            return Results.Problem(orderResult.Errors.ToProblemDetails());

        return Results.Ok(OrderResponseMapper.From(orderResult.Value));
    }
}
