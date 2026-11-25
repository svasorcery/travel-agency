using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Travel.Modules.Trips.Api.Contracts;
using Travel.Modules.Trips.Application;
using Travel.Modules.Trips.Core;
using Wolverine.Attributes;
using Wolverine.Http;

namespace Travel.Modules.Trips.Api.Endpoints;

public sealed class TripsDestinationsEndpoint
{
    [WolverineGet("/api/trips/destinations")]
    [Authorize]
    public static IResult Get(HttpContext context)
    {
        var owner = TripsEndpoint.Owner(context);
        if (owner.IsError)
            return TripsEndpoint.Failure(owner.FirstError);
        var json = JsonSerializer.Serialize(
            new { catalogueVersion = CityCatalogue.Version, items = CityCatalogue.Cities },
            new JsonSerializerOptions(JsonSerializerDefaults.Web)
        );
        return System.Text.Encoding.UTF8.GetByteCount(json) > 10 * 1024
            ? TripsProblems.Problem("Trips.Unavailable", 503)
            : TripsEndpoint.Json(json);
    }
}

public sealed class ListTripPlansEndpoint
{
    [WolverineGet("/api/trips")]
    [Authorize]
    public static async Task<IResult> Get(
        HttpContext context,
        [FromServices] TripPlanService service,
        [FromServices] ITripPlanCodec codec,
        CancellationToken ct
    )
    {
        var owner = TripsEndpoint.Owner(context);
        if (owner.IsError)
            return TripsEndpoint.Failure(owner.FirstError);
        var result = await service.ListAsync(
            owner.Value,
            context.Request.Query["cursor"].FirstOrDefault(),
            ct
        );
        if (result.IsError)
            return TripsEndpoint.Failure(result.FirstError);
        var json = codec.SerializePage(result.Value);
        return json.IsError
            ? TripsEndpoint.Failure(json.FirstError)
            : TripsEndpoint.Json(json.Value);
    }
}

public sealed class CreateTripPlanEndpoint
{
    [WolverinePost("/api/trips")]
    [Authorize]
    // The store owns the capacity-lock transaction and commits before returning a result.
    [NonTransactional]
    public static async Task<IResult> Post(
        CreateTripPlanDto body,
        HttpContext context,
        [FromServices] TripPlanService service,
        [FromServices] ITripPlanCodec codec,
        CancellationToken ct
    )
    {
        var owner = TripsEndpoint.Owner(context);
        if (owner.IsError)
            return TripsEndpoint.Failure(owner.FirstError);
        var result = await service.CreateAsync(
            owner.Value,
            new CreateTripPlan(body.Title, body.Brief),
            ct
        );
        if (result.IsError)
            return TripsEndpoint.Failure(result.FirstError);
        var json = codec.Serialize(result.Value);
        if (json.IsError)
            return TripsEndpoint.Failure(json.FirstError);
        context.Response.Headers.ETag =
            "\"" + result.Value.Revision.ToString(CultureInfo.InvariantCulture) + "\"";
        context.Response.Headers.Location = "/api/trips/" + result.Value.TripId.ToString("D");
        return TripsEndpoint.Json(json.Value, 201);
    }
}

public sealed class GetTripPlanEndpoint
{
    [WolverineGet("/api/trips/{tripId:guid}")]
    [Authorize]
    public static Task<IResult> Get(
        Guid tripId,
        HttpContext context,
        [FromServices] TripPlanService service,
        [FromServices] ITripPlanCodec codec,
        CancellationToken ct
    ) => Read(tripId, context, service, codec, ct, legacyWorkspace: true);

    [WolverineGet("/api/trips/{tripId:guid}/workspace-v2")]
    [Authorize]
    public static Task<IResult> GetV2(
        Guid tripId,
        HttpContext context,
        [FromServices] TripPlanService service,
        [FromServices] ITripPlanCodec codec,
        CancellationToken ct
    ) => Read(tripId, context, service, codec, ct, legacyWorkspace: false);

    private static async Task<IResult> Read(
        Guid tripId,
        HttpContext context,
        TripPlanService service,
        ITripPlanCodec codec,
        CancellationToken ct,
        bool legacyWorkspace
    )
    {
        var owner = TripsEndpoint.Owner(context);
        if (owner.IsError)
            return TripsEndpoint.Failure(owner.FirstError);
        var result = await service.GetAsync(owner.Value, tripId, ct, legacyWorkspace);
        if (result.IsError)
            return TripsEndpoint.Failure(result.FirstError);
        var json = codec.Serialize(result.Value);
        if (json.IsError)
            return TripsEndpoint.Failure(json.FirstError);
        context.Response.Headers.ETag =
            "\"" + result.Value.Revision.ToString(CultureInfo.InvariantCulture) + "\"";
        return TripsEndpoint.Json(json.Value);
    }
}

public sealed class ReplaceTripPlanEndpoint
{
    [WolverinePut("/api/trips/{tripId:guid}")]
    [Authorize]
    public static Task<IResult> Put(
        Guid tripId,
        ReplaceTripPlanDto body,
        HttpContext context,
        [FromServices] TripPlanService service,
        [FromServices] ITripPlanCodec codec,
        CancellationToken ct
    ) =>
        Replace(
            tripId,
            new(body.Title, body.Brief, body.Days),
            context,
            service,
            codec,
            ct,
            body.HasManualStay
                ? body.ManualStay is { } stay
                    ? [stay]
                    : []
                : null,
            legacyWorkspace: true
        );

    [WolverinePut("/api/trips/{tripId:guid}/workspace-v2")]
    [Authorize]
    public static Task<IResult> PutV2(
        Guid tripId,
        ReplaceTripPlanV2Dto body,
        HttpContext context,
        [FromServices] TripPlanService service,
        [FromServices] ITripPlanCodec codec,
        CancellationToken ct
    ) =>
        Replace(
            tripId,
            new(body.Title, body.Brief, body.Days),
            context,
            service,
            codec,
            ct,
            body.ManualStays,
            legacyWorkspace: false
        );

    private static async Task<IResult> Replace(
        Guid tripId,
        ReplaceTripPlan command,
        HttpContext context,
        TripPlanService service,
        ITripPlanCodec codec,
        CancellationToken ct,
        IReadOnlyList<TripStayInput>? manualStays,
        bool legacyWorkspace
    )
    {
        var owner = TripsEndpoint.Owner(context);
        if (owner.IsError)
            return TripsEndpoint.Failure(owner.FirstError);
        var revision = TripsEndpoint.Revision(context);
        if (revision.IsError)
            return TripsEndpoint.Failure(revision.FirstError);
        var result = await service.ReplaceAsync(
            owner.Value,
            tripId,
            revision.Value,
            command,
            ct,
            manualStays,
            legacyWorkspace
        );
        if (result.IsError)
            return TripsEndpoint.Failure(result.FirstError);
        var json = codec.Serialize(result.Value);
        if (json.IsError)
            return TripsEndpoint.Failure(json.FirstError);
        context.Response.Headers.ETag =
            "\"" + result.Value.Revision.ToString(CultureInfo.InvariantCulture) + "\"";
        return TripsEndpoint.Json(json.Value);
    }
}

public sealed class DeleteTripPlanEndpoint
{
    [WolverineDelete("/api/trips/{tripId:guid}")]
    [Authorize]
    public static async Task<IResult> Delete(
        Guid tripId,
        HttpContext context,
        [FromServices] TripPlanService service,
        CancellationToken ct
    )
    {
        var owner = TripsEndpoint.Owner(context);
        if (owner.IsError)
            return TripsEndpoint.Failure(owner.FirstError);
        var revision = TripsEndpoint.Revision(context);
        if (revision.IsError)
            return TripsEndpoint.Failure(revision.FirstError);
        var result = await service.DeleteAsync(owner.Value, tripId, revision.Value, ct);
        return result.IsError ? TripsEndpoint.Failure(result.FirstError) : Results.NoContent();
    }
}
