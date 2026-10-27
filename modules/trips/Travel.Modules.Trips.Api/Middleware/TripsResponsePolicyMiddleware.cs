using Microsoft.AspNetCore.Http;
using Travel.Modules.Trips.Api.Composition;
using Travel.Modules.Trips.Api.Contracts;

namespace Travel.Modules.Trips.Api.Middleware;

public sealed class TripsResponsePolicyMiddleware(
    RequestDelegate next,
    TripsFeature feature,
    TripsItineraryFeature itinerary,
    TripsBriefExtractionFeature briefExtraction
)
{
    public static bool IsTrips(PathString path) =>
        path.StartsWithSegments("/api/trips", StringComparison.OrdinalIgnoreCase);

    public async Task InvokeAsync(HttpContext context)
    {
        if (!IsTrips(context.Request.Path))
        {
            await next(context);
            return;
        }
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.Pragma = "no-cache";
        context.Response.OnStarting(() =>
        {
            context.Response.Headers.CacheControl = "no-store";
            return Task.CompletedTask;
        });
        if (
            !feature.Enabled
            || (
                !briefExtraction.Enabled
                && TripsBriefExtractionRoutes.Classify(context.Request)
                    != TripsBriefExtractionRoute.None
            )
            || (
                !itinerary.Enabled
                && TripsItineraryRoutes.Classify(context.Request) != TripsItineraryRoute.None
            )
        )
        {
            await TripsProblems.Problem("Trips.NotFound", 404).ExecuteAsync(context);
            return;
        }
        await next(context);
        if (!context.Response.HasStarted && context.Response.StatusCode is 401 or 403)
            await TripsProblems
                .Problem(
                    context.Response.StatusCode == 401
                        ? "Trips.Unauthenticated"
                        : "Trips.Forbidden",
                    context.Response.StatusCode
                )
                .ExecuteAsync(context);
    }
}
