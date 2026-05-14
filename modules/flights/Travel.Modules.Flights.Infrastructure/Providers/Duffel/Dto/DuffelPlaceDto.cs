using System.Text.Json.Serialization;

namespace Travel.Modules.Flights.Infrastructure.Providers.Duffel.Dto;

public sealed record DuffelPlaceDto([property: JsonPropertyName("iata_code")] string IataCode);
