using ErrorOr;

namespace Travel.Modules.Flights.Core.ValueObjects;

public sealed record SearchCriteria
{
    public IReadOnlyList<RequestedFlightLeg> Legs { get; }
    public SearchRouteMode RouteMode { get; }
    public IataCode Origin => Legs[0].Origin;
    public IataCode Destination => Legs[0].Destination;
    public DateOnly DepartureDate => Legs[0].DepartureDate;
    public DateOnly? ReturnDate => IsRoundTrip ? Legs[1].DepartureDate : null;
    public int PassengerCount { get; }
    public CabinClass CabinClass { get; }
    public CurrencyCode Currency { get; }

    /// <summary>
    /// UI locale for provider responses (e.g. airline names, airport labels).
    /// Two-letter IETF language tag: <c>"ru"</c> or <c>"en"</c>. Defaults to <c>"ru"</c>.
    /// </summary>
    public string Locale { get; }

    [System.Text.Json.Serialization.JsonIgnore]
    public JourneyKind JourneyKind =>
        Legs.Count == 1 ? JourneyKind.OneWay
        : Legs.Count == 2
        && Legs[1].Origin == Legs[0].Destination
        && Legs[1].Destination == Legs[0].Origin
            ? JourneyKind.RoundTrip
        : JourneyKind.MultiLeg;
    public bool IsRoundTrip => JourneyKind == JourneyKind.RoundTrip;

    private SearchCriteria(
        IReadOnlyList<RequestedFlightLeg> legs,
        SearchRouteMode routeMode,
        int passengerCount,
        CabinClass cabinClass,
        CurrencyCode currency,
        string locale = "ru"
    )
    {
        Legs = Array.AsReadOnly(legs.ToArray());
        RouteMode = routeMode;
        PassengerCount = passengerCount;
        CabinClass = cabinClass;
        Currency = currency;
        Locale = locale;
    }

    public static ErrorOr<SearchCriteria> Create(
        IataCode origin,
        IataCode destination,
        DateOnly departureDate,
        DateOnly? returnDate,
        int passengerCount,
        CabinClass cabinClass,
        CurrencyCode currency,
        string locale = "ru"
    )
    {
        if (origin == destination)
            return Error.Validation(
                "SearchCriteria.SameOriginDestination",
                "Origin and destination must be different airports."
            );

        if (returnDate.HasValue && returnDate.Value < departureDate)
            return Error.Validation(
                "SearchCriteria.ReturnBeforeDeparture",
                "Return date must not be before departure date."
            );

        // Explicit adult party, one whole-offer booking.
        if (passengerCount is < 1 or > 9)
            return Error.Validation(
                "SearchCriteria.PassengerCount",
                "Passenger count must be between 1 and 9."
            );

        var outbound = RequestedFlightLeg.Create(origin, destination, departureDate);
        if (outbound.IsError)
            return outbound.Errors;
        List<RequestedFlightLeg> legs = [outbound.Value];
        if (returnDate.HasValue)
        {
            var inbound = RequestedFlightLeg.Create(destination, origin, returnDate.Value);
            if (inbound.IsError)
                return inbound.Errors;
            legs.Add(inbound.Value);
        }
        return new SearchCriteria(
            legs,
            SearchRouteMode.LegacyLocations,
            passengerCount,
            cabinClass,
            currency,
            locale
        );
    }

    public static ErrorOr<SearchCriteria> CreateMultiLeg(
        IReadOnlyList<RequestedFlightLeg> legs,
        int passengerCount,
        CabinClass cabinClass,
        CurrencyCode currency,
        string locale = "ru"
    )
    {
        if (legs is null || legs.Count is < 1 or > 4)
            return Error.Validation(
                "SearchCriteria.LegCount",
                "Journey requires one to four legs."
            );
        if (passengerCount is < 1 or > 9)
            return Error.Validation(
                "SearchCriteria.PassengerCount",
                "Passenger count must be between 1 and 9."
            );
        var copy = legs.ToArray();
        for (var i = 0; i < copy.Length; i++)
        {
            if (copy[i] is null)
                return Error.Validation("SearchCriteria.NullLeg", "Every leg is required.");
            if (i > 0 && copy[i].DepartureDate < copy[i - 1].DepartureDate)
                return Error.Validation(
                    "SearchCriteria.LegDateOrder",
                    "Departure dates must be nondecreasing."
                );
        }
        return new SearchCriteria(
            copy,
            SearchRouteMode.ExplicitAirportLegs,
            passengerCount,
            cabinClass,
            currency,
            locale
        );
    }
}
