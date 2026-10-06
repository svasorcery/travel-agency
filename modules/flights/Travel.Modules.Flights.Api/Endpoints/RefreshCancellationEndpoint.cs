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

public sealed class RefreshCancellationEndpoint
{
    [WolverinePost("/api/flights/cancellations/refresh")]
    [Authorize("flights:book")]
    [ProducesResponseType(typeof(CancellationStatusResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(CancellationStatusResponse), StatusCodes.Status202Accepted)]
    public static async Task<IResult> Post(
        RefreshCancellationRequest req,
        HttpContext context,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        if (!context.User.TryGetUserId(out var userId))
            return Results.Problem(IdentityProblemDetails.InvalidUserIdentity());
        var result = await bus.InvokeAsync<ErrorOr<CancellationStatusResult>>(
            new RefreshCancellationCommand(
                req.AggregateId,
                userId,
                req.OperationId,
                req.ExpectedOperationRevision,
                req.RefreshRequestId,
                false
            ),
            ct
        );
        return CancellationEndpointResults.Reply(result, context, true);
    }
}
