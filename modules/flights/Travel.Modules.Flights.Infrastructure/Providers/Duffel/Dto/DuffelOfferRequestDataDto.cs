using System.Text.Json.Serialization;

namespace Travel.Modules.Flights.Infrastructure.Providers.Duffel.Dto;

public sealed record DuffelOfferRequestDataDto(
    [property: JsonPropertyName("offers")] DuffelOfferDto[] Offers
);
