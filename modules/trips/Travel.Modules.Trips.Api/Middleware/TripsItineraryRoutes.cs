using Microsoft.AspNetCore.Http;

namespace Travel.Modules.Trips.Api.Middleware;

public enum TripsItineraryRoute
{
    None,
    Availability,
    Candidate,
}

public static class TripsItineraryRoutes
{
    public static TripsItineraryRoute Classify(HttpRequest request)
    {
        var path = request.Path.Value ?? "";
        if (path.EndsWith('/'))
            path = path[..^1];
        if (
            HttpMethods.IsGet(request.Method)
            && string.Equals(
                path,
                "/api/trips/itinerary-availability",
                StringComparison.OrdinalIgnoreCase
            )
        )
            return TripsItineraryRoute.Availability;
        var parts = path.Split('/');
        return
            HttpMethods.IsPost(request.Method)
            && parts.Length == 5
            && string.Equals(parts[1], "api", StringComparison.OrdinalIgnoreCase)
            && string.Equals(parts[2], "trips", StringComparison.OrdinalIgnoreCase)
            && Guid.TryParse(parts[3], out _)
            && string.Equals(parts[4], "itinerary-candidate", StringComparison.OrdinalIgnoreCase)
            ? TripsItineraryRoute.Candidate
            : TripsItineraryRoute.None;
    }
}
