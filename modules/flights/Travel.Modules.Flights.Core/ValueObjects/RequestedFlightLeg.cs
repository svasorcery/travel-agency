using ErrorOr;

namespace Travel.Modules.Flights.Core.ValueObjects;

public sealed record RequestedFlightLeg
{
    public IataCode Origin { get; }
    public IataCode Destination { get; }
    public DateOnly DepartureDate { get; }

    private RequestedFlightLeg(IataCode origin, IataCode destination, DateOnly departureDate)
    {
        Origin = origin;
        Destination = destination;
        DepartureDate = departureDate;
    }

    public static ErrorOr<RequestedFlightLeg> Create(
        IataCode origin,
        IataCode destination,
        DateOnly departureDate
    )
    {
        if (
            origin is null
            || destination is null
            || IataCode.Create(origin.Value).IsError
            || IataCode.Create(destination.Value).IsError
        )
            return Error.Validation(
                "RequestedFlightLeg.AirportInvalid",
                "Valid airport codes are required."
            );
        if (origin == destination)
            return Error.Validation(
                "RequestedFlightLeg.SameOriginDestination",
                "Origin and destination must differ."
            );
        if (departureDate == DateOnly.MinValue)
            return Error.Validation(
                "RequestedFlightLeg.DepartureDateRequired",
                "Departure date is required."
            );
        return new RequestedFlightLeg(origin, destination, departureDate);
    }
}
