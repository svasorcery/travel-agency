using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Travel.Modules.Trips.Api.Composition;
using Travel.Modules.Trips.Api.Contracts;
using Travel.Modules.Trips.Api.Middleware;
using Travel.Modules.Trips.Application;
using Travel.Modules.Trips.Application.Options;
using Travel.Modules.Trips.Application.SavedOptions;
using Travel.Modules.Trips.Core;
using Travel.Modules.Trips.Core.Options;
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

public sealed record NegotiatedTripOption(
    TripOptionPreview Preview,
    SavedOptionSelection? Selection
)
{
    public override string ToString() => nameof(NegotiatedTripOption);
}

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
        if (result.IsError)
            return TripsProblems.Error(result.FirstError);
        var preview = result.Value;
        if (!context.Request.Headers.ContainsKey(TripOptionRoutes.FormatHeader))
            return Results.Json(preview);

        var feature = context.RequestServices.GetService<TripOptionFeature>();
        if (
            preview.Option is not null
            && feature?.SaveEnabled == true
            && feature.CanCreate(preview)
            && await context
                .RequestServices.GetRequiredService<SavedOptionPreviewReadiness>()
                .IsReadyAsync(ct)
        )
        {
            var protection = context.RequestServices.GetRequiredService<ISavedOptionProtection>();
            var clock = context.RequestServices.GetRequiredService<TimeProvider>();
            var issued = protection.Issue(owner.Value, body.Brief, preview, clock.GetUtcNow());
            if (!issued.IsError)
            {
                var bytes = JsonSerializer.SerializeToUtf8Bytes(
                    new NegotiatedTripOption(preview, issued.Value),
                    context
                        .RequestServices.GetRequiredService<Microsoft.Extensions.Options.IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>()
                        .Value.SerializerOptions
                );
                if (bytes.Length <= TripPlanService.MaximumBodyBytes)
                    return Results.Bytes(bytes, "application/json");
            }
        }
        return Results.Json(new NegotiatedTripOption(preview, null));
    }
}
