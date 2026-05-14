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
