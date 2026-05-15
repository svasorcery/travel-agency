using System.Text.Json.Serialization;

namespace Travel.Modules.Flights.Infrastructure.Providers.Duffel.Dto;

public sealed record DuffelBaggageDto(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("quantity")] int Quantity
);
