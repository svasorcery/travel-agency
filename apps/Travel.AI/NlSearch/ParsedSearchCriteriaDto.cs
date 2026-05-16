using System.Text.Json.Serialization;

namespace Travel.AI.NlSearch;

/// <summary>
/// Structured-output schema that the LLM fills in response to a flight search query.
/// Property names are camelCase to match the JSON schema produced by the system prompt.
/// </summary>
public sealed record ParsedSearchCriteriaDto(
    [property: JsonPropertyName("origin")] string Origin,
    [property: JsonPropertyName("destination")] string Destination,
    [property: JsonPropertyName("departure_date")] DateOnly DepartureDate,
    [property: JsonPropertyName("return_date")] DateOnly? ReturnDate,
    [property: JsonPropertyName("passenger_count")] int PassengerCount,
    [property: JsonPropertyName("cabin_class")] string CabinClass,
    [property: JsonPropertyName("currency")] string Currency
);
