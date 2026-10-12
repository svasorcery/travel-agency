using System.Text.Json.Serialization;

namespace Travel.Modules.Flights.Infrastructure.Providers.Duffel.Dto;

[JsonConverter(typeof(DuffelFarePassengerJsonConverter))]
public sealed record DuffelSegmentPassengerDto(
    [property: JsonPropertyName("cabin_class")] string CabinClass,
    [property:
        JsonPropertyName("cabin_class_marketing_name"),
        JsonConverter(typeof(DuffelFareLabelJsonConverter))
    ]
        string? CabinClassMarketingName,
    [property:
        JsonPropertyName("baggages"),
        JsonConverter(typeof(DuffelIncludedBaggageJsonConverter))
    ]
        DuffelBaggageDto[]? Baggages,
    [property: JsonPropertyName("passenger_id")] string? PassengerId = null,
    [property:
        JsonPropertyName("fare_basis_code"),
        JsonConverter(typeof(DuffelFareCodeJsonConverter))
    ]
        string? FareBasisCode = null,
    [property: JsonIgnore] bool InvalidData = false
);
