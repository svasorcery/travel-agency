using System.Text.Json.Serialization;
using ErrorOr;

namespace Travel.Modules.Flights.Core.ValueObjects;

public sealed record Slice
{
    public IataCode Origin { get; }
    public IataCode Destination { get; }
    public IReadOnlyList<Segment> Segments { get; }
    public Duration Duration { get; }

    [JsonConstructor]
    private Slice(
        IataCode origin,
        IataCode destination,
        IReadOnlyList<Segment> segments,
        Duration duration
    )
    {
        Origin = origin;
        Destination = destination;
        Segments = segments;
        Duration = duration;
    }

    public static ErrorOr<Slice> Create(IReadOnlyList<Segment> segments)
    {
        if (segments is null || segments.Count == 0)
            return Error.Validation("Slice.NoSegments", "Slice requires at least one segment.");

        segments = Array.AsReadOnly(segments.ToArray());
        foreach (var segment in segments)
        {
            if (
                segment is null
                || segment.Origin is null
                || segment.Destination is null
                || segment.Cabin is null
                || CabinClass.Parse(segment.Cabin.Code).IsError
                || IataCode.Create(segment.Origin.Value).IsError
                || IataCode.Create(segment.Destination.Value).IsError
            )
                return Error.Validation(
                    "Slice.InvalidSegment",
                    "Every segment requires valid airports and cabin."
                );
            var validated = Segment.Create(
                segment.Origin,
                segment.Destination,
                segment.DepartAt,
                segment.ArriveAt,
                segment.CarrierCode,
                segment.FlightNumber,
                segment.Cabin
            );
            if (validated.IsError)
                return validated.Errors;
        }

        for (var i = 1; i < segments.Count; i++)
        {
            if (segments[i - 1].Destination != segments[i].Origin)
                return Error.Validation(
                    "Slice.Discontinuous",
                    $"Segment {i} origin does not match previous destination."
                );
            if (segments[i].DepartAt < segments[i - 1].ArriveAt)
                return Error.Validation(
                    "Slice.TimeInversion",
                    $"Segment {i} departs before previous arrival."
                );
        }

        var totalDuration = segments[^1].ArriveAt - segments[0].DepartAt;
        var dur = Duration.Create(totalDuration);
        if (dur.IsError)
            return dur.FirstError;

        return new Slice(segments[0].Origin, segments[^1].Destination, segments, dur.Value);
    }

    public DateTimeOffset DepartAt => Segments[0].DepartAt;
    public DateTimeOffset ArriveAt => Segments[^1].ArriveAt;
}
