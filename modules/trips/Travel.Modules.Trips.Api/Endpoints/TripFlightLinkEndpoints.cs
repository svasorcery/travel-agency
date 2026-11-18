using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Travel.Modules.Trips.Api.Composition;
using Travel.Modules.Trips.Api.Contracts;
using Travel.Modules.Trips.Api.Middleware;
using Travel.Modules.Trips.Application.FlightLinks;
using Wolverine.Attributes;
using Wolverine.Http;

namespace Travel.Modules.Trips.Api.Endpoints;

public sealed class TripFlightLinkAvailabilityEndpoint
{
    [Authorize, WolverineGet("/api/trips/flight-links/availability")]
    public static IResult Get(HttpContext ctx, [FromServices] TripFlightLinksFeature feature)
    {
        var owner = TripsEndpoint.Owner(ctx);
        return owner.IsError
            ? TripsEndpoint.Failure(owner.FirstError)
            : TripFlightLinkResponses.Json(new LinkAvailabilityResponse(feature.Enabled), 2048);
    }
}

public sealed class TripFlightLinkMetadataEndpoint
{
    [Authorize, WolverineGet("/api/trips/{tripId:guid}/flight-links")]
    public static async Task<IResult> Get(
        Guid tripId,
        HttpContext ctx,
        [FromServices] TripFlightLinkService service,
        CancellationToken ct
    )
    {
        var owner = TripsEndpoint.Owner(ctx);
        return owner.IsError
            ? TripsEndpoint.Failure(owner.FirstError)
            : TripFlightLinkResponses.Metadata(
                ctx,
                await service.GetAsync(owner.Value, tripId, ct)
            );
    }
}

public sealed class TripFlightLinkPatchEndpoint
{
    [Authorize, NonTransactional, WolverinePatch("/api/trips/{tripId:guid}/flight-links")]
    public static async Task<IResult> Patch(
        Guid tripId,
        HttpContext ctx,
        [FromServices] TripFlightLinkService service,
        CancellationToken ct
    )
    {
        var owner = TripsEndpoint.Owner(ctx);
        if (owner.IsError)
            return TripsEndpoint.Failure(owner.FirstError);
        if (ctx.Items[TripFlightLinkGuard.PatchKey] is not LinkPatch patch)
            return TripsProblems.Problem("Trips.InvalidInput", 400);
        return TripFlightLinkResponses.Metadata(
            ctx,
            await service.PatchAsync(
                owner.Value,
                tripId,
                patch.Revision,
                patch.Add,
                patch.OrderId,
                ct
            )
        );
    }
}

public sealed class TripFlightLinkStatusEndpoint
{
    [Authorize, WolverineGet("/api/trips/{tripId:guid}/flight-links/status")]
    public static async Task<IResult> Get(
        Guid tripId,
        HttpContext ctx,
        [FromServices] TripFlightLinkService service,
        CancellationToken ct
    )
    {
        var owner = TripsEndpoint.Owner(ctx);
        if (owner.IsError)
            return TripsEndpoint.Failure(owner.FirstError);
        var result = await service.StatusAsync(owner.Value, tripId, ct);
        return result.IsError
            ? TripsEndpoint.Failure(result.FirstError)
            : TripFlightLinkResponses.Status(result.Value);
    }
}

public sealed class TripFlightCandidatesEndpoint
{
    [Authorize, WolverineGet("/api/trips/{tripId:guid}/flight-order-candidates")]
    public static async Task<IResult> Get(
        Guid tripId,
        HttpContext ctx,
        [FromServices] TripFlightLinkService service,
        CancellationToken ct
    )
    {
        var owner = TripsEndpoint.Owner(ctx);
        if (owner.IsError)
            return TripsEndpoint.Failure(owner.FirstError);
        var result = await service.CandidatesAsync(
            owner.Value,
            tripId,
            (int)ctx.Items[TripFlightLinkGuard.OffsetKey]!,
            ct
        );
        return result.IsError
            ? TripsEndpoint.Failure(result.FirstError)
            : TripFlightLinkResponses.Json(result.Value, 64 * 1024);
    }
}
