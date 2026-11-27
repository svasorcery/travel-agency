using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Travel.Modules.Trips.Api.Composition;
using Travel.Modules.Trips.Api.Contracts;
using Travel.Modules.Trips.Application.SavedOptions;
using Wolverine.Attributes;
using Wolverine.Http;

namespace Travel.Modules.Trips.Api.Endpoints;

public sealed record SaveSelectionRequest(string SelectionToken, string? Title = null)
{
    public override string ToString() => nameof(SaveSelectionRequest);
}

public sealed record SavedTripLocation(Guid TripId);

public sealed class SavedOptionEndpoints
{
    [WolverinePost("/api/trips/saved-options")]
    [Authorize]
    [NonTransactional]
    public static async Task<IResult> Post(
        SaveSelectionRequest body,
        HttpContext context,
        [FromServices] TripOptionFeature feature,
        [FromServices] SavedOptionService service,
        CancellationToken ct
    )
    {
        var owner = TripsEndpoint.Owner(context);
        if (owner.IsError)
            return TripsEndpoint.Failure(owner.FirstError);
        var result = await service.SaveAsync(
            owner.Value,
            body.SelectionToken,
            body.Title,
            feature.CanCreate,
            ct
        );
        if (result.IsError)
            return TripsProblems.Error(result.FirstError);
        var id = result.Value.TripId;
        context.Response.Headers.Location = "/api/trips/" + id.ToString("D");
        return Results.Json(
            new SavedTripLocation(id),
            statusCode: result.Value.Created ? 201 : 200
        );
    }

    [WolverineGet("/api/trips/{tripId:guid}/saved-option")]
    [Authorize]
    public static async Task<IResult> Get(
        Guid tripId,
        HttpContext context,
        [FromServices] SavedOptionService service,
        CancellationToken ct
    )
    {
        var owner = TripsEndpoint.Owner(context);
        if (owner.IsError)
            return TripsEndpoint.Failure(owner.FirstError);
        var result = await service.ReadAsync(owner.Value, tripId, ct);
        return result.IsError ? TripsProblems.Error(result.FirstError) : Results.Json(result.Value);
    }
}
