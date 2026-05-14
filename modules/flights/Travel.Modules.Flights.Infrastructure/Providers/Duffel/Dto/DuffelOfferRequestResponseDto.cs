using System.Text.Json.Serialization;

namespace Travel.Modules.Flights.Infrastructure.Providers.Duffel.Dto;

public sealed record DuffelOfferRequestResponseDto(
    [property: JsonPropertyName("data")] DuffelOfferRequestDataDto Data
);
