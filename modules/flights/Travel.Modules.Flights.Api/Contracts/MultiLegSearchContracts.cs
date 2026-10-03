namespace Travel.Modules.Flights.Api.Contracts;

public sealed record RequestedFlightLegDto(
    string Origin,
    string Destination,
    DateOnly DepartureDate
);

public sealed record SearchRequestV2(
    RequestedFlightLegDto[] Legs,
    int PassengerCount = 1,
    string CabinClass = "economy"
);
