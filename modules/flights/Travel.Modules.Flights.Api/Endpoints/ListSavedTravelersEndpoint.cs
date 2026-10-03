using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Travel.Modules.Flights.Api.Contracts;
using Travel.Modules.Flights.Application.SavedTravelers;
using Travel.Shared.Web;
using Wolverine.Http;

namespace Travel.Modules.Flights.Api.Endpoints;

public sealed class ListSavedTravelersEndpoint
{
    [WolverineGet("/api/flights/travelers")]
    [Authorize("flights:book")]
    [ProducesResponseType(typeof(SavedTravelerPageDto), StatusCodes.Status200OK)]
    public static async Task<IResult> Get(
        HttpContext httpContext,
        [FromServices] ISavedTravelerService service,
        CancellationToken ct,
        int offset = 0
    )
    {
        if (!httpContext.User.TryGetUserId(out var owner))
            return Results.Problem(IdentityProblemDetails.InvalidUserIdentity());
        // Wolverine 6 binds malformed numeric queries to their default. Validate the raw transport
        // value here so production and the ordinary ASP.NET binding fixture both reject it.
        if (
            httpContext.Request.Query.TryGetValue("offset", out var raw)
            && (
                raw.Count != 1
                || !int.TryParse(
                    raw[0],
                    System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out offset
                )
            )
        )
            return Results.Problem(
                SavedTravelerProblemDetails.From([SavedTravelerErrors.PageInvalid])
            );
        var result = await service.ListAsync(owner, offset, ct);
        return result.IsError
            ? Results.Problem(SavedTravelerProblemDetails.From(result.Errors))
            : Results.Ok(SavedTravelerPageDto.From(result.Value));
    }
}
