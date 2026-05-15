using ErrorOr;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Travel.Modules.Flights.Api.Contracts;
using Travel.Modules.Flights.Application.Commands;
using Travel.Shared.Web;
using Wolverine;
using Wolverine.Http;

namespace Travel.Modules.Flights.Api.Endpoints;

public sealed class ConfirmOrderEndpoint
{
    [WolverinePost("/api/flights/orders/confirm")]
    [Authorize("flights:book")]
    public static async Task<IResult> Post(
        ConfirmOrderRequest req,
        HttpContext httpContext,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var userId = httpContext.User.GetUserId();

        var result = await bus.InvokeAsync<ErrorOr<ConfirmedOrderResult>>(
            new ConfirmOrderCommand(req.AggregateId, userId),
            ct
        );
        if (result.IsError)
            return Results.Problem(result.Errors.ToProblemDetails());

        return Results.Ok(
            new ConfirmedOrderResponse(
                result.Value.AggregateId,
                result.Value.Status,
                result.Value.PaymentRef
            )
        );
    }
}
