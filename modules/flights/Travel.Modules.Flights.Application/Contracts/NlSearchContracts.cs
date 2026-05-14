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
    string Currency, // e.g. "RUB"
    // ── model usage (carried back from Travel.AI so we can record flights.nl_search.* metric) ──
    int InputTokens = 0,
    int OutputTokens = 0,
    decimal CostUsd = 0m,
    string ModelId = ""
);
