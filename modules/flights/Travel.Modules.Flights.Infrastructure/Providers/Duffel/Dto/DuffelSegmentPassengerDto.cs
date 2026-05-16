using System.Text.Json.Serialization;

namespace Travel.Modules.Flights.Infrastructure.Providers.Duffel.Dto;

public sealed record DuffelSegmentPassengerDto(
    [property: JsonPropertyName("cabin_class")] string CabinClass,
    [property: JsonPropertyName("cabin_class_marketing_name")] string? CabinClassMarketingName,
    [property: JsonPropertyName("baggages")] DuffelBaggageDto[] Baggages
);
