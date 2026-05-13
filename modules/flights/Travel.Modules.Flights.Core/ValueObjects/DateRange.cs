using ErrorOr;

namespace Travel.Modules.Flights.Core.ValueObjects;

public sealed record DateRange
{
    public DateOnly From { get; }
    public DateOnly To { get; }

    private DateRange(DateOnly from, DateOnly to)
    {
        From = from;
        To = to;
    }

    public static ErrorOr<DateRange> Create(DateOnly from, DateOnly to)
    {
        if (to < from)
            return Error.Validation("DateRange.Inverted", "End date must be on or after start.");
        return new DateRange(from, to);
    }

    public int LengthInDays => To.DayNumber - From.DayNumber;
}
