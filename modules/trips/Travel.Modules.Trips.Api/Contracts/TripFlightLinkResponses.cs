using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using ErrorOr;
using Microsoft.AspNetCore.Http;
using Travel.Modules.Trips.Api.Endpoints;
using Travel.Modules.Trips.Application.FlightLinks;
using Travel.Modules.Trips.Core;

namespace Travel.Modules.Trips.Api.Contracts;

public sealed record LinkAvailabilityResponse(bool Enabled);

public sealed record LinkMetadataResponse(
    Guid TripId,
    string LinksRevision,
    IReadOnlyList<TripFlightLink> Links
);

public sealed record LinkStatusResponse(
    Guid TripId,
    string LinksRevision,
    IReadOnlyList<LinkStatusRow> Items
);

[JsonDerivedType(typeof(AvailableLinkStatus))]
public record LinkStatusRow(Guid OrderId, string State);

public sealed record AvailableLinkStatus(
    Guid OrderId,
    string BookingStatus,
    int PassengerCount,
    IReadOnlyList<TripFlightLeg>? Route,
    bool ReviewRequired,
    DateTimeOffset ObservedAt,
    string StreamVersion
) : LinkStatusRow(OrderId, "available");

public sealed record FlightLinkPatchRequest(string Op, Guid OrderId);

internal static class TripFlightLinkResponses
{
    internal static IResult Metadata(HttpContext ctx, ErrorOr<TripFlightLinks> response)
    {
        if (response.IsError)
            return TripsEndpoint.Failure(response.FirstError);
        var value = response.Value;
        var revision = Number(value.Revision);
        return Json(
            new LinkMetadataResponse(value.TripId, revision, value.Links),
            16 * 1024,
            ctx,
            "\"fl-" + revision + "\""
        );
    }

    internal static IResult Status(TripFlightStatusPage page)
    {
        var rows = page
            .Items.Select(i =>
                i.Observation is { } o
                    ? new AvailableLinkStatus(
                        i.OrderId,
                        o.BookingStatus,
                        o.PassengerCount,
                        o.Route,
                        o.ReviewRequired,
                        o.ObservedAt,
                        Number(o.StreamVersion)
                    )
                    : new LinkStatusRow(i.OrderId, i.State)
            )
            .ToArray();
        return Json(
            new LinkStatusResponse(page.TripId, Number(page.LinksRevision), rows),
            32 * 1024
        );
    }

    internal static IResult Json<T>(
        T data,
        int maxBytes,
        HttpContext? ctx = null,
        string? etag = null
    )
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(data, JsonSerializerOptions.Web);
        if (bytes.Length > maxBytes)
            return TripsProblems.Problem("Trips.Unavailable", 503);
        if (ctx != null && etag != null)
            ctx.Response.Headers.ETag = etag;
        return Results.Bytes(bytes, "application/json; charset=utf-8");
    }

    private static string Number(long n) => n.ToString(CultureInfo.InvariantCulture);
}
