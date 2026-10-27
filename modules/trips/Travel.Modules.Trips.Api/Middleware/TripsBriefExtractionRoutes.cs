using Microsoft.AspNetCore.Http;

namespace Travel.Modules.Trips.Api.Middleware;

public enum TripsBriefExtractionRoute
{
    None,
    Capability,
    Candidate,
}

public static class TripsBriefExtractionRoutes
{
    public static TripsBriefExtractionRoute Classify(HttpRequest request)
    {
        var path = (request.Path.Value ?? "").TrimEnd('/');
        return HttpMethods.IsGet(request.Method)
            && string.Equals(
                path,
                "/api/trips/brief-extraction-capability",
                StringComparison.OrdinalIgnoreCase
            )
                ? TripsBriefExtractionRoute.Capability
            : HttpMethods.IsPost(request.Method)
            && string.Equals(path, "/api/trips/brief-candidate", StringComparison.OrdinalIgnoreCase)
                ? TripsBriefExtractionRoute.Candidate
            : TripsBriefExtractionRoute.None;
    }
}
