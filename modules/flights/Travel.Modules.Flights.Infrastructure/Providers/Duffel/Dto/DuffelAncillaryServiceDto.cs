using System.Text.Json.Serialization;

namespace Travel.Modules.Flights.Infrastructure.Providers.Duffel.Dto;

public sealed record DuffelAncillaryServiceDto(
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("total_amount")] string? TotalAmount,
    [property: JsonPropertyName("total_currency")] string? TotalCurrency,
    [property: JsonPropertyName("passenger_ids")] string[]? PassengerIds,
    [property: JsonPropertyName("segment_ids")] string[]? SegmentIds,
    [property: JsonPropertyName("maximum_quantity")] int? MaximumQuantity = null,
    [property: JsonPropertyName("quantity")] int? Quantity = null,
    [property: JsonPropertyName("metadata")] DuffelServiceMetadataDto? Metadata = null
);

public sealed record DuffelServiceMetadataDto(
    [property: JsonPropertyName("type")] string? Type = null,
    [property: JsonPropertyName("maximum_weight_kg")] decimal? MaximumWeightKg = null,
    [property: JsonPropertyName("maximum_height_cm")] decimal? MaximumHeightCm = null,
    [property: JsonPropertyName("maximum_depth_cm")] decimal? MaximumDepthCm = null,
    [property: JsonPropertyName("maximum_length_cm")] decimal? MaximumLengthCm = null,
    [property: JsonPropertyName("designator")] string? Designator = null,
    [property: JsonPropertyName("disclosures")] string[]? Disclosures = null,
    [property: JsonPropertyName("name")] string? Name = null
);
