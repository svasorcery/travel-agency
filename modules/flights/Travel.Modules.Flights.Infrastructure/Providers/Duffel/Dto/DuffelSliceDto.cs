using System.Text.Json.Serialization;

namespace Travel.Modules.Flights.Infrastructure.Providers.Duffel.Dto;

[JsonConverter(typeof(DuffelFareSliceJsonConverter))]
public sealed record DuffelSliceDto(
    [property: JsonPropertyName("segments")] DuffelSegmentDto[] Segments,
    [property:
        JsonPropertyName("fare_brand_name"),
        JsonConverter(typeof(DuffelFareLabelJsonConverter))
    ]
        string? FareBrandName,
    [property: JsonPropertyName("id")] string? Id = null,
    [property: JsonPropertyName("conditions")] DuffelConditionsDto? Conditions = null,
    [property: JsonIgnore] bool InvalidData = false
);
