using System.Text.Json.Serialization;

namespace Travel.Modules.Flights.Infrastructure.Providers.Travelpayouts.Dto;

public sealed record PricesForDatesResponseDto(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("data")] PriceEntryDto[] Data,
    [property: JsonPropertyName("currency")] string Currency
);

public sealed record PriceEntryDto(
    [property: JsonPropertyName("origin")] string Origin,
    [property: JsonPropertyName("destination")] string Destination,
    [property: JsonPropertyName("price")] decimal Price,
    [property: JsonPropertyName("airline")] string Airline,
    [property: JsonPropertyName("flight_number")] string FlightNumber,
    [property: JsonPropertyName("departure_at")] DateTimeOffset DepartureAt,
    [property: JsonPropertyName("return_at")] DateTimeOffset? ReturnAt,
    [property: JsonPropertyName("transfers")] int Transfers,
    [property: JsonPropertyName("duration")] int Duration,
    [property: JsonPropertyName("link")] string Link
);
