using ErrorOr;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Travel.Modules.Flights.Api.Contracts;
using Travel.Modules.Flights.Application.Cancellation;
using Travel.Shared.Web;
using Wolverine;
using Wolverine.Http;

namespace Travel.Modules.Flights.Api.Endpoints;

public sealed class GetCancellationReviewEndpoint
{
    [WolverineGet("/api/flights/orders/{aggregateId}/cancellation-review")]
    [Authorize("flights:cancellation-review")]
    [ProducesResponseType(typeof(CancellationReviewResponse), StatusCodes.Status200OK)]
    public static async Task<IResult> Get(
        Guid aggregateId,
        HttpContext context,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        if (!context.User.TryGetUserId(out var userId))
            return Results.Problem(IdentityProblemDetails.InvalidUserIdentity());
        var result = await bus.InvokeAsync<ErrorOr<CancellationReviewResult>>(
            new GetCancellationReviewQuery(aggregateId, new OperatorActor(userId)),
            ct
        );
        context.Response.Headers.CacheControl = "no-store";
        return result.IsError
            ? Results.Problem(result.Errors.ToProblemDetails())
            : Results.Ok(CancellationReviewResponse.From(result.Value));
    }
}
