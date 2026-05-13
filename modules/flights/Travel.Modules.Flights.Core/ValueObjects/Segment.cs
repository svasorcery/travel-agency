using ErrorOr;

namespace Travel.Modules.Flights.Core.ValueObjects;

public sealed record Segment
{
    public IataCode Origin { get; }
    public IataCode Destination { get; }
    public DateTimeOffset DepartAt { get; }
    public DateTimeOffset ArriveAt { get; }
    public string CarrierCode { get; }
    public string FlightNumber { get; }
    public CabinClass Cabin { get; }

    private Segment(
        IataCode origin,
        IataCode destination,
        DateTimeOffset departAt,
        DateTimeOffset arriveAt,
        string carrierCode,
        string flightNumber,
        CabinClass cabin
    )
    {
        Origin = origin;
        Destination = destination;
        DepartAt = departAt;
        ArriveAt = arriveAt;
        CarrierCode = carrierCode;
        FlightNumber = flightNumber;
        Cabin = cabin;
    }

    public static ErrorOr<Segment> Create(
        IataCode origin,
        IataCode destination,
        DateTimeOffset departAt,
        DateTimeOffset arriveAt,
        string carrierCode,
        string flightNumber,
        CabinClass cabin
    )
    {
        if (arriveAt <= departAt)
            return Error.Validation(
                "Segment.NonPositiveDuration",
                "Arrival must be strictly after departure."
            );
        if (origin == destination)
            return Error.Validation(
                "Segment.SameOriginDestination",
                "Segment origin and destination must differ."
            );
        if (string.IsNullOrWhiteSpace(carrierCode))
            return Error.Validation("Segment.CarrierEmpty", "Carrier code is required.");
        if (string.IsNullOrWhiteSpace(flightNumber))
            return Error.Validation("Segment.FlightNumberEmpty", "Flight number is required.");

        return new Segment(
            origin,
            destination,
            departAt,
            arriveAt,
            carrierCode,
            flightNumber,
            cabin
        );
    }

    public TimeSpan Duration => ArriveAt - DepartAt;
}
