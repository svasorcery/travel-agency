namespace Travel.Modules.Trips.Core.Options;

/// <summary>Each quote is for exactly the stated party and the whole flight or room stay.</summary>
public sealed record OptionCatalogue(
    string Version,
    IReadOnlyList<FlightOffer> Flights,
    IReadOnlyList<StayOffer> Hotels
);

public sealed record FlightOffer(
    string Id,
    int Adults,
    OptionLeg Outbound,
    OptionLeg? Inbound,
    IReadOnlyList<OptionCharge> Charges
);

public sealed record StayOffer(
    string Id,
    string Name,
    string CityId,
    int Adults,
    int Capacity,
    IReadOnlyList<DateOnly> Nights,
    TimeOnly EarliestCheckIn,
    TimeOnly LatestCheckIn,
    TimeOnly EarliestCheckOut,
    TimeOnly LatestCheckOut,
    IReadOnlyList<OptionCharge> Charges
);

public enum OptionChargeKind
{
    TravelerMandatory,
    Included,
    UnknownMandatory,
    SupplierNet,
}

public sealed record OptionCharge(string Component, OptionChargeKind Kind, PlanningMoney? Money);

/// <summary>Authored historical demo inventory. All timestamps are airport-local and offset-qualified.</summary>
public static class FictionalOptionCatalogue
{
    public static OptionCatalogue For(string version, int adults)
    {
        if (version is not ("A" or "B"))
            throw new ArgumentOutOfRangeException(nameof(version));
        if (adults is not (1 or 2))
            return new(version, [], []);
        var first = new DateOnly(2026, 10, 28);
        var second = first.AddDays(1);
        var third = second.AddDays(1);
        var late = version == "B";
        var firstFlight = new FlightOffer(
            "RT1",
            adults,
            Out(first, late ? 18 : 7),
            Back(third),
            [Fare(adults == 2 ? "18000" : "9000")]
        );
        var secondFlight = new FlightOffer("RT2", 1, Out(first, 12), Back(third), [Fare("7000")]);
        var shortFlight = new FlightOffer(
            "RT3",
            adults,
            Out(first, 7),
            Back(second),
            [Fare(adults == 2 ? "18000" : "9000")]
        );
        var flightWithoutRoom = shortFlight with
        {
            Id = "RT4",
            Outbound = Out(second, 7),
            Inbound = Back(third),
        };

        var hotel = new StayOffer(
            "H1",
            "Невский двор",
            "saint-petersburg",
            adults,
            2,
            [first, second],
            new(14, 0),
            new(22, 0),
            new(6, 0),
            new(12, 0),
            [Room("12000"), new("includedTax", OptionChargeKind.Included, null)]
        );
        var competitor = hotel with
        {
            Id = "H2",
            Name = "Дом у канала",
            Charges = [Room("14000"), new("includedTax", OptionChargeKind.Included, null)],
        };
        var shortStay = hotel with
        {
            Id = "H3",
            Name = "Северный сад",
            Nights = [first],
            Charges = [new("hotelBase", OptionChargeKind.SupplierNet, Rub("5000"))],
        };
        return new(
            version,
            [firstFlight, secondFlight, shortFlight, flightWithoutRoom],
            [hotel, competitor, shortStay]
        );
    }

    private static OptionLeg Out(DateOnly day, int departsHour) =>
        new("SVO", "LED", Local(day, departsHour, 30), Local(day, departsHour + 2, 0));

    private static OptionLeg Back(DateOnly day) =>
        new("LED", "SVO", Local(day, 11, 0), Local(day, 12, 30));

    private static DateTimeOffset Local(DateOnly date, int hour, int minute) =>
        new(date.ToDateTime(new(hour, minute)), TimeSpan.FromHours(3));

    private static OptionCharge Fare(string amount) =>
        new("flightBase", OptionChargeKind.TravelerMandatory, Rub(amount));

    private static OptionCharge Room(string amount) =>
        new("hotelBase", OptionChargeKind.TravelerMandatory, Rub(amount));

    private static PlanningMoney Rub(string amount) => PlanningMoney.Create(amount, "RUB").Value;
}
