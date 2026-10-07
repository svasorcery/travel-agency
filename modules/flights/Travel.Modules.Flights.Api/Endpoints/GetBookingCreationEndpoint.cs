using ErrorOr;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Travel.Modules.Flights.Api.Contracts;
using Travel.Modules.Flights.Application.Queries;
using Travel.Shared.Web;
using Wolverine;
using Wolverine.Http;

namespace Travel.Modules.Flights.Api.Endpoints;

public sealed class GetBookingCreationEndpoint
{
    [WolverineGet("/api/flights/orders/{aggregateId:guid}/creation")]
    [Authorize]
    public static async Task<IResult> Get(
        Guid aggregateId,
        HttpContext context,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        context.Response.Headers.CacheControl = "no-store";
        if (!context.User.TryGetUserId(out var owner))
            return Results.Problem(IdentityProblemDetails.InvalidUserIdentity());
        var result = await bus.InvokeAsync<ErrorOr<BookingCreationStatusResult>>(
            new GetBookingCreationQuery(aggregateId, owner),
            ct
        );
        return result.IsError
            ? Results.Problem(result.Errors.ToProblemDetails())
            : Results.Ok(BookingCreationResponse.From(result.Value));
    }
}
