using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Travel.Modules.Flights.Application.SavedTravelers;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Travel.Shared.Web;
using Wolverine.Http;

namespace Travel.Modules.Flights.Api.Endpoints;

public sealed class DeleteSavedTravelerEndpoint
{
    [WolverineDelete("/api/flights/travelers/{travelerId:guid}")]
    [Authorize("flights:book")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public static async Task<Results<NoContent, ProblemHttpResult>> Delete(
        Guid travelerId,
        HttpContext httpContext,
        [FromServices] ISavedTravelerService service,
        CancellationToken ct,
        [FromHeader(Name = "If-Match")] string? ifMatch = null,
        [FromHeader(Name = "If-None-Match")] string? ifNoneMatch = null
    )
    {
        if (!httpContext.User.TryGetUserId(out var owner))
            return TypedResults.Problem(IdentityProblemDetails.InvalidUserIdentity());
        var id = SavedTravelerId.Create(travelerId);
        if (id.IsError)
            return TypedResults.Problem(
                SavedTravelerProblemDetails.From([SavedTravelerErrors.IdInvalid])
            );
        var condition = SavedTravelerPreconditions.Parse(httpContext.Request, allowCreate: false);
        if (condition.IsError)
            return TypedResults.Problem(SavedTravelerProblemDetails.From(condition.Errors));
        var result = await service.DeleteAsync(owner, id.Value, condition.Value.Revision, ct);
        return result.IsError
            ? TypedResults.Problem(SavedTravelerProblemDetails.From(result.Errors))
            : TypedResults.NoContent();
    }
}
