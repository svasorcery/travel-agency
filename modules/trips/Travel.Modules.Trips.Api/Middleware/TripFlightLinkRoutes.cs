using Microsoft.AspNetCore.Http;

namespace Travel.Modules.Trips.Api.Middleware;

internal enum LinkRoute
{
    None,
    Availability,
    Metadata,
    Status,
    Candidates,
}

internal static class TripFlightLinkRoutes
{
    internal static LinkRoute Classify(PathString path)
    {
        if (
            !path.StartsWithSegments("/api/trips", StringComparison.OrdinalIgnoreCase, out var rest)
        )
            return LinkRoute.None;
        var value = rest.Value?.TrimEnd('/') ?? "";
        if (value.Equals("/flight-links/availability", StringComparison.OrdinalIgnoreCase))
            return LinkRoute.Availability;
        if (value.Length < 2)
            return LinkRoute.None;
        var end = value.IndexOf('/', 1);
        if (end <= 1)
            return LinkRoute.None;
        var suffix = value.AsSpan(end);
        if (suffix.Equals("/flight-links", StringComparison.OrdinalIgnoreCase))
            return LinkRoute.Metadata;
        if (suffix.Equals("/flight-links/status", StringComparison.OrdinalIgnoreCase))
            return LinkRoute.Status;
        if (suffix.Equals("/flight-order-candidates", StringComparison.OrdinalIgnoreCase))
            return LinkRoute.Candidates;
        return LinkRoute.None;
    }
}
