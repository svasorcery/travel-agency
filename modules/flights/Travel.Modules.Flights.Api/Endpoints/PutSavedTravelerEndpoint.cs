using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Travel.Modules.Flights.Api.Contracts;
using Travel.Modules.Flights.Application.SavedTravelers;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Travel.Shared.Web;
using Wolverine.Http;

namespace Travel.Modules.Flights.Api.Endpoints;

public sealed class PutSavedTravelerEndpoint
{
    [WolverinePut("/api/flights/travelers/{travelerId:guid}")]
    [Authorize("flights:book")]
    [ProducesResponseType(typeof(SavedTravelerReceiptDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(SavedTravelerReceiptDto), StatusCodes.Status201Created)]
    public static async Task<IResult> Put(
        Guid travelerId,
        SavedTravelerDetailsDto request,
        HttpContext httpContext,
        [FromServices] ISavedTravelerService service,
        TimeProvider timeProvider,
        CancellationToken ct,
        [FromHeader(Name = "If-Match")] string? ifMatch = null,
        [FromHeader(Name = "If-None-Match")] string? ifNoneMatch = null
    )
    {
        if (!httpContext.User.TryGetUserId(out var owner))
            return Results.Problem(IdentityProblemDetails.InvalidUserIdentity());
        var id = SavedTravelerId.Create(travelerId);
        if (id.IsError)
            return Results.Problem(
                SavedTravelerProblemDetails.From([SavedTravelerErrors.IdInvalid])
            );
        var condition = SavedTravelerPreconditions.Parse(httpContext.Request, allowCreate: true);
        if (condition.IsError)
            return Results.Problem(SavedTravelerProblemDetails.From(condition.Errors));
        var details = SavedTravelerDetails.CreateRaw(
            request.Title ?? string.Empty,
            request.GivenName ?? string.Empty,
            request.FamilyName ?? string.Empty,
            request.DateOfBirth,
            request.Gender ?? string.Empty,
            request.Email ?? string.Empty,
            request.Phone ?? string.Empty,
            DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime)
        );
        if (details.IsError)
            return Results.Problem(SavedTravelerProblemDetails.From(details.Errors));
        var result = condition.Value.Create
            ? await service.CreateAsync(owner, id.Value, details.Value, ct)
            : await service.UpdateAsync(
                owner,
                id.Value,
                condition.Value.Revision,
                details.Value,
                ct
            );
        if (result.IsError)
            return Results.Problem(SavedTravelerProblemDetails.From(result.Errors));
        httpContext.Response.Headers.ETag = SavedTravelerPreconditions.ETag(result.Value.Revision);
        return Results.Json(
            new SavedTravelerReceiptDto(result.Value.Id.Value, result.Value.Revision),
            statusCode: condition.Value.Create ? 201 : 200
        );
    }
}
