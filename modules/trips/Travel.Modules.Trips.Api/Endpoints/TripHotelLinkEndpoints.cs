using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using ErrorOr;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Travel.Modules.Trips.Api.Contracts;
using Travel.Modules.Trips.Api.Middleware;
using Travel.Modules.Trips.Application.HotelLinks;
using Wolverine.Attributes;
using Wolverine.Http;

namespace Travel.Modules.Trips.Api.Endpoints;

public sealed class TripHotelLinkAvailabilityEndpoint
{
    [Authorize, WolverineGet("/api/trips/hotel-link/availability")]
    public static async Task<IResult> Get(
        HttpContext context,
        [FromServices] TripHotelLinkService service,
        CancellationToken ct
    )
    {
        if (!await service.IsReadyAsync(ct))
            return TripsProblems.Problem("Trips.Unavailable", 503);
        var canAttach =
            await TripHotelLinkResults.CanReadHotelsAsync(context) && service.IsHotelsEnabled;
        return TripHotelLinkResults.Json(new { enabled = true, canAttach });
    }
}

public sealed class TripHotelLinkMetadataEndpoint
{
    [Authorize, WolverineGet("/api/trips/{tripId:guid}/hotel-link")]
    public static async Task<IResult> Get(
        Guid tripId,
        HttpContext context,
        [FromServices] TripHotelLinkService service,
        CancellationToken ct
    )
    {
        var owner = TripsEndpoint.Owner(context);
        return owner.IsError
            ? TripsEndpoint.Failure(owner.FirstError)
            : TripHotelLinkResults.Metadata(
                context,
                await service.MetadataAsync(owner.Value, tripId, ct)
            );
    }
}

public sealed class TripHotelLinkPatchEndpoint
{
    [Authorize, NonTransactional, WolverinePatch("/api/trips/{tripId:guid}/hotel-link")]
    public static async Task<IResult> Patch(
        Guid tripId,
        HttpContext context,
        [FromServices] TripHotelLinkService service,
        CancellationToken ct
    )
    {
        var owner = TripsEndpoint.Owner(context);
        if (owner.IsError)
            return TripsEndpoint.Failure(owner.FirstError);
        if (
            !context.Items.TryGetValue(TripHotelLinkGuard.PatchKey, out var body)
            || body is not HotelLinkPatch patch
        )
            return TripsProblems.Problem("Trips.InvalidInput", 400);
        var canRead =
            patch.ReservationId.HasValue && await TripHotelLinkResults.CanReadHotelsAsync(context);
        return TripHotelLinkResults.Metadata(
            context,
            await service.PatchAsync(
                owner.Value,
                tripId,
                patch.Revision,
                patch.ReservationId,
                canRead,
                ct
            )
        );
    }
}

public sealed class TripHotelLinkStatusEndpoint
{
    [Authorize, WolverineGet("/api/trips/{tripId:guid}/hotel-link/status")]
    public static async Task<IResult> Get(
        Guid tripId,
        HttpContext context,
        [FromServices] TripHotelLinkService service,
        CancellationToken ct
    )
    {
        var owner = TripsEndpoint.Owner(context);
        if (owner.IsError)
            return TripsEndpoint.Failure(owner.FirstError);
        var result = await service.StatusAsync(
            owner.Value,
            tripId,
            await TripHotelLinkResults.CanReadHotelsAsync(context),
            ct
        );
        if (result.IsError)
            return TripsEndpoint.Failure(result.FirstError);
        var status = result.Value;
        return TripHotelLinkResults.Json(
            new
            {
                revision = status.Revision.ToString(CultureInfo.InvariantCulture),
                status.State,
                status.ReservationId,
                status.PropertyName,
                status.CheckIn,
                status.CheckOut,
            }
        );
    }
}

internal static class TripHotelLinkResults
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    internal static async Task<bool> CanReadHotelsAsync(HttpContext context)
    {
        var policies = context.RequestServices.GetRequiredService<IAuthorizationPolicyProvider>();
        var policy = await policies.GetPolicyAsync("hotels:book");
        return policy is not null
            && (
                await context
                    .RequestServices.GetRequiredService<IAuthorizationService>()
                    .AuthorizeAsync(context.User, context, policy.Requirements)
            ).Succeeded;
    }

    internal static IResult Metadata(HttpContext context, ErrorOr<TripHotelLinkMetadata> result)
    {
        if (result.IsError)
            return TripsEndpoint.Failure(result.FirstError);
        context.Response.Headers.ETag = TripHotelLinkGuard.Tag(result.Value.Revision);
        return Json(
            new
            {
                result.Value.HasLink,
                revision = result.Value.Revision.ToString(CultureInfo.InvariantCulture),
            }
        );
    }

    internal static IResult Json<T>(T value) =>
        TripsEndpoint.Json(JsonSerializer.Serialize(value, Options));
}
