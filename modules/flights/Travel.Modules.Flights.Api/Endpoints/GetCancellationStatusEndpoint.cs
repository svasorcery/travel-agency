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

public sealed class GetCancellationStatusEndpoint
{
    [WolverineGet("/api/flights/orders/{aggregateId}/cancellation")]
    [Authorize("flights:book")]
    [ProducesResponseType(typeof(CancellationStatusResponse), StatusCodes.Status200OK)]
    public static async Task<IResult> Get(
        Guid aggregateId,
        HttpContext context,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        if (!context.User.TryGetUserId(out var userId))
            return Results.Problem(IdentityProblemDetails.InvalidUserIdentity());
        var result = await bus.InvokeAsync<ErrorOr<CancellationStatusResult>>(
            new GetCancellationStatusQuery(aggregateId, userId),
            ct
        );
        return CancellationEndpointResults.Reply(result, context, false);
    }
}
