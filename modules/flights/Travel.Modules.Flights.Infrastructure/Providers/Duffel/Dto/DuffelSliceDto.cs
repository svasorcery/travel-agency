using System.Text.Json.Serialization;

namespace Travel.Modules.Flights.Infrastructure.Providers.Duffel.Dto;

public sealed record DuffelSliceDto(
    [property: JsonPropertyName("segments")] DuffelSegmentDto[] Segments,
    [property: JsonPropertyName("fare_brand_name")] string? FareBrandName
);
