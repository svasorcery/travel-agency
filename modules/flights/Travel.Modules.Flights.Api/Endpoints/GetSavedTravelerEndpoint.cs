using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Travel.Modules.Flights.Api.Contracts;
using Travel.Modules.Flights.Application.SavedTravelers;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Travel.Shared.Web;
using Wolverine.Http;

namespace Travel.Modules.Flights.Api.Endpoints;

public sealed class GetSavedTravelerEndpoint
{
    [WolverineGet("/api/flights/travelers/{travelerId:guid}")]
    [Authorize("flights:book")]
    [ProducesResponseType(typeof(SavedTravelerViewDto), StatusCodes.Status200OK)]
    public static async Task<IResult> Get(
        Guid travelerId,
        HttpContext httpContext,
        [FromServices] ISavedTravelerService service,
        CancellationToken ct
    )
    {
        if (!httpContext.User.TryGetUserId(out var owner))
            return Results.Problem(IdentityProblemDetails.InvalidUserIdentity());
        var id = SavedTravelerId.Create(travelerId);
        if (id.IsError)
            return Results.Problem(
                SavedTravelerProblemDetails.From([SavedTravelerErrors.IdInvalid])
            );
        var result = await service.GetAsync(owner, id.Value, ct);
        if (result.IsError)
            return Results.Problem(SavedTravelerProblemDetails.From(result.Errors));
        httpContext.Response.Headers.ETag = SavedTravelerPreconditions.ETag(result.Value.Revision);
        return Results.Ok(SavedTravelerViewDto.From(result.Value));
    }
}
