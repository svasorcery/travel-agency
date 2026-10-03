using System.Text.Json.Serialization;
using ErrorOr;

namespace Travel.Modules.Flights.Core.ValueObjects;

public sealed record Itinerary
{
    public IReadOnlyList<Slice> Slices { get; }
    public Duration TotalDuration { get; }

    [JsonConstructor]
    private Itinerary(IReadOnlyList<Slice> slices, Duration totalDuration)
    {
        Slices = slices;
        TotalDuration = totalDuration;
    }

    public static ErrorOr<Itinerary> Create(IReadOnlyList<Slice> slices)
    {
        if (slices is null || slices.Count == 0)
            return Error.Validation("Itinerary.NoSlices", "Itinerary requires at least one slice.");
        if (slices.Count > 4)
            return Error.Validation(
                "Itinerary.TooManySlices",
                "Journey supports at most four slices."
            );

        var copy = new Slice[slices.Count];
        for (var i = 0; i < slices.Count; i++)
        {
            var slice = slices[i];
            if (slice is null)
                return Error.Validation("Itinerary.InvalidSlice", "Every slice is required.");
            var validated = Slice.Create(slice.Segments);
            if (validated.IsError)
                return validated.Errors;
            if (
                slice.Origin != validated.Value.Origin
                || slice.Destination != validated.Value.Destination
                || slice.Duration is null
                || slice.Duration.Value != validated.Value.Duration.Value
            )
                return Error.Validation(
                    "Itinerary.InvalidSlice",
                    "Slice metadata must match its segments."
                );
            copy[i] = validated.Value;
            if (i > 0 && copy[i].DepartAt < copy[i - 1].ArriveAt)
                return Error.Validation(
                    "Itinerary.TimeInversion",
                    "A leg must not depart before the previous arrival."
                );
        }

        var total = TimeSpan.Zero;
        foreach (var s in copy)
            total += s.Duration.Value;

        var dur = Duration.CreateJourneyTotal(total);
        if (dur.IsError)
            return dur.FirstError;

        return new Itinerary(Array.AsReadOnly(copy), dur.Value);
    }

    public bool IsOneWay => Slices.Count == 1;

    [JsonIgnore]
    public JourneyKind JourneyKind =>
        Slices.Count == 1 ? JourneyKind.OneWay
        : Slices.Count == 2
        && Slices[1].Origin == Slices[0].Destination
        && Slices[1].Destination == Slices[0].Origin
            ? JourneyKind.RoundTrip
        : JourneyKind.MultiLeg;
    public bool IsRoundTrip => JourneyKind == JourneyKind.RoundTrip;
}
