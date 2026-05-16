using System.Text.Json.Serialization;
using ErrorOr;

namespace Travel.Modules.Flights.Core.ValueObjects;

public sealed record Duration
{
    public TimeSpan Value { get; }

    [JsonConstructor]
    private Duration(TimeSpan value) => Value = value;

    public static ErrorOr<Duration> Create(TimeSpan value)
    {
        if (value <= TimeSpan.Zero)
            return Error.Validation("Duration.NonPositive", "Duration must be positive.");
        if (value >= TimeSpan.FromDays(2))
            return Error.Validation("Duration.TooLong", "Duration must be less than 48 hours.");
        return new Duration(value);
    }

    public override string ToString() => Value.ToString();
}
