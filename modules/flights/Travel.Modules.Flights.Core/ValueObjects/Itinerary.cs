using ErrorOr;

namespace Travel.Modules.Flights.Core.ValueObjects;

public sealed record Itinerary
{
    public IReadOnlyList<Slice> Slices { get; }
    public Duration TotalDuration { get; }

    private Itinerary(IReadOnlyList<Slice> slices, Duration totalDuration)
    {
        Slices = slices;
        TotalDuration = totalDuration;
    }

    public static ErrorOr<Itinerary> Create(IReadOnlyList<Slice> slices)
    {
        if (slices is null || slices.Count == 0)
            return Error.Validation("Itinerary.NoSlices", "Itinerary requires at least one slice.");
        if (slices.Count > 2)
            return Error.Validation(
                "Itinerary.TooManySlices",
                "M1 supports one-way (1 slice) and round-trip (2 slices) only."
            );

        var total = TimeSpan.Zero;
        foreach (var s in slices)
            total += s.Duration.Value;

        var dur = Duration.Create(total);
        if (dur.IsError)
            return dur.FirstError;

        return new Itinerary(slices, dur.Value);
    }

    public bool IsOneWay => Slices.Count == 1;
    public bool IsRoundTrip => Slices.Count == 2;
}
