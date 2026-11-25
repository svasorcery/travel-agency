using System.Globalization;

namespace Travel.Modules.Trips.Core.Options;

internal readonly record struct StopWindow(int Start, int End)
{
    public bool IsEmpty => Start >= End;
    public string? StartText => IsEmpty ? null : Clock(Start);
    public string? EndText => IsEmpty ? null : Clock(End);

    public static string Clock(int minute) =>
        new TimeOnly(minute / 60, minute % 60).ToString("HH:mm", CultureInfo.InvariantCulture);
}

internal readonly record struct WalkWindows(StopWindow First, StopWindow Middle, StopWindow Last)
{
    public StopWindow For(int index, int count) =>
        index == 0 ? First
        : index == count - 1 ? Last
        : Middle;
}

internal static class OptionSchedule
{
    internal const int ArrivalTransfer = 90;
    internal const int DepartureTransfer = 180;
    internal const int ToFirstStop = 20;
    internal const int BackToHotel = 20;
    internal static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById(
        "Europe/Moscow"
    );

    internal static (int Stops, int Walk, int Duration) Pace(string pace) =>
        pace switch
        {
            "relaxed" => (2, 60, 240),
            "balanced" => (3, 90, 360),
            "active" => (4, 120, 480),
            _ => throw new ArgumentOutOfRangeException(nameof(pace)),
        };

    internal static bool Matches(FlightOffer flight, TripBrief brief) =>
        brief.OriginId == "moscow"
        && brief.DestinationId == "saint-petersburg"
        && flight.Adults == brief.Adults
        && flight.Inbound is { } inbound
        && LegMatches(flight.Outbound, "SVO", "LED", brief.StartDate)
        && LegMatches(inbound, "LED", "SVO", brief.EndDate)
        && flight.Outbound.ArrivesAt < inbound.DepartsAt;

    private static bool LegMatches(OptionLeg leg, string from, string to, DateOnly date) =>
        leg.FromAirport == from
        && leg.ToAirport == to
        && leg.DepartsAt < leg.ArrivesAt
        && LocalDateMatches(leg.DepartsAt, date)
        && LocalDateMatches(leg.ArrivesAt, date);

    private static bool LocalDateMatches(DateTimeOffset instant, DateOnly date) =>
        instant.Offset == Zone.GetUtcOffset(instant)
        && DateOnly.FromDateTime(instant.DateTime) == date
        && instant.Ticks % TimeSpan.TicksPerMinute == 0;

    internal static bool Matches(StayOffer hotel, TripBrief brief)
    {
        var nights = brief.EndDate.DayNumber - brief.StartDate.DayNumber;
        return hotel.CityId == brief.DestinationId
            && hotel.Adults == brief.Adults
            && hotel.Capacity >= brief.Adults
            && hotel.Nights.Count == nights
            && Enumerable
                .Range(0, nights)
                .All(i => hotel.Nights.Contains(brief.StartDate.AddDays(i)));
    }

    internal static DateTimeOffset At(DateOnly date, TimeOnly time)
    {
        var local = date.ToDateTime(time);
        return new(local, Zone.GetUtcOffset(local));
    }

    internal static OptionHotel? Stay(FlightOffer flight, StayOffer offer, TripBrief brief)
    {
        var arrival = flight.Outbound.ArrivesAt.AddMinutes(ArrivalTransfer);
        var departure = flight.Inbound!.DepartsAt.AddMinutes(-DepartureTransfer);
        var opens = At(brief.StartDate, offer.EarliestCheckIn);
        var closes = At(brief.EndDate, offer.LatestCheckOut);
        var checkIn = arrival > opens ? arrival : opens;
        var checkOut = departure < closes ? departure : closes;
        if (
            checkIn > At(brief.StartDate, offer.LatestCheckIn)
            || checkOut < At(brief.EndDate, offer.EarliestCheckOut)
            || checkIn >= checkOut
        )
            return null;
        return new(offer.Id, offer.Name, offer.Adults, offer.Nights.Count, checkIn, checkOut);
    }

    internal static WalkWindows Windows(OptionHotel stay) =>
        new(
            Window(Math.Max(600, Minute(stay.CheckInAt)), 1080),
            Window(600, 1080),
            Window(600, Math.Min(1080, Minute(stay.CheckOutAt)))
        );

    private static int Minute(DateTimeOffset time) => time.Hour * 60 + time.Minute;

    private static StopWindow Window(int start, int end) =>
        start + ToFirstStop < end - BackToHotel
            ? new(start + ToFirstStop, end - BackToHotel)
            : default;
}
