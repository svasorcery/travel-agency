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

public sealed class ResolveCancellationReviewEndpoint
{
    [WolverinePost("/api/flights/cancellations/review/resolve")]
    [Authorize("flights:cancellation-review")]
    [ProducesResponseType(typeof(CancellationStatusResponse), StatusCodes.Status200OK)]
    public static async Task<IResult> Post(
        ResolveCancellationReviewRequest req,
        HttpContext context,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        if (!context.User.TryGetUserId(out var userId))
            return Results.Problem(IdentityProblemDetails.InvalidUserIdentity());
        var input = req.ToInput();
        if (input.IsError)
            return Results.Problem(input.Errors.ToProblemDetails());
        var result = await bus.InvokeAsync<ErrorOr<CancellationStatusResult>>(
            new ResolveCancellationReviewCommand(new OperatorActor(userId), input.Value),
            ct
        );
        return CancellationEndpointResults.Reply(result, context, false);
    }
}
