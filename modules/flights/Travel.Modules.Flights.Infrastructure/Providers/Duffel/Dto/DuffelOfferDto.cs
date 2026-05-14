using System.Text.Json.Serialization;

namespace Travel.Modules.Flights.Infrastructure.Providers.Duffel.Dto;

public sealed record DuffelOfferDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("total_amount")] string TotalAmount,
    [property: JsonPropertyName("total_currency")] string TotalCurrency,
    [property: JsonPropertyName("expires_at")] DateTimeOffset ExpiresAt,
    [property: JsonPropertyName("slices")] DuffelSliceDto[] Slices,
    [property: JsonPropertyName("conditions")] DuffelConditionsDto? Conditions
);
