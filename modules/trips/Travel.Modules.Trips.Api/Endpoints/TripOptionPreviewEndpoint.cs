using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Travel.Modules.Trips.Api.Contracts;
using Travel.Modules.Trips.Application.Options;
using Travel.Modules.Trips.Core;
using Wolverine.Attributes;
using Wolverine.Http;

namespace Travel.Modules.Trips.Api.Endpoints;

public sealed record TripOptionRequest(TripBriefInput Brief)
{
    public override string ToString() => nameof(TripOptionRequest);
}

public sealed record TripOptionAvailability(
    string ProfileId,
    DateTimeOffset AsOfUtc,
    DateOnly StartDate,
    DateOnly EndDate,
    string DataMode
);

public sealed class TripOptionPreviewEndpoint
{
    [WolverineGet("/api/trips/option-preview/availability")]
    [Authorize]
    public static IResult Get(HttpContext context, [FromServices] TripOptionScenario scenario)
    {
        var owner = TripsEndpoint.Owner(context);
        return owner.IsError
            ? TripsEndpoint.Failure(owner.FirstError)
            : Results.Json(
                new TripOptionAvailability(
                    "spb-option",
                    scenario.GetUtcNow(),
                    new DateOnly(2026, 10, 28),
                    new DateOnly(2026, 10, 30),
                    "fictional"
                )
            );
    }

    [WolverinePost("/api/trips/option-preview")]
    [NonTransactional]
    [Authorize]
    public static async Task<IResult> Post(
        TripOptionRequest body,
        HttpContext context,
        [FromServices] TripOptionService service,
        CancellationToken ct
    )
    {
        var owner = TripsEndpoint.Owner(context);
        if (owner.IsError)
            return TripsEndpoint.Failure(owner.FirstError);
        var result = await service.PreviewAsync(body.Brief, ct);
        return result.IsError ? TripsProblems.Error(result.FirstError) : Results.Json(result.Value);
    }
}
