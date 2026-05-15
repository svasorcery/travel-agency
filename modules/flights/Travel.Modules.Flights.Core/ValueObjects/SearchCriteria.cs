using ErrorOr;

namespace Travel.Modules.Flights.Core.ValueObjects;

public sealed record SearchCriteria
{
    public IataCode Origin { get; }
    public IataCode Destination { get; }
    public DateOnly DepartureDate { get; }
    public DateOnly? ReturnDate { get; }
    public int PassengerCount { get; }
    public CabinClass CabinClass { get; }
    public CurrencyCode Currency { get; }

    /// <summary>
    /// UI locale for provider responses (e.g. airline names, airport labels).
    /// Two-letter IETF language tag: <c>"ru"</c> or <c>"en"</c>. Defaults to <c>"ru"</c>.
    /// </summary>
    public string Locale { get; }

    public bool IsRoundTrip => ReturnDate.HasValue;

    private SearchCriteria(
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
        Origin = origin;
        Destination = destination;
        DepartureDate = departureDate;
        ReturnDate = returnDate;
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

        // M1 constraint: single passenger only
        if (passengerCount != 1)
            return Error.Validation(
                "SearchCriteria.PassengerCount",
                "Passenger count must be exactly 1 for M1."
            );

        return new SearchCriteria(
            origin,
            destination,
            departureDate,
            returnDate,
            passengerCount,
            cabinClass,
            currency,
            locale
        );
    }
}
