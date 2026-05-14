namespace Travel.Modules.Flights.Application.Contracts;

public sealed record NlSearchRequested(string Query, Guid CorrelationId, string Locale = "ru");

public sealed record NlSearchParsed(
    Guid CorrelationId,
    string Origin, // IATA
    string Destination, // IATA
    DateOnly DepartureDate,
    DateOnly? ReturnDate,
    int PassengerCount,
    string CabinClass, // serialized cabin code, e.g. "economy"
    string Currency
); // e.g. "RUB"
