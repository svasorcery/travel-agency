using System.Text.Json.Serialization;

namespace Travel.Modules.Flights.Infrastructure.Providers.Duffel.Dto;

public sealed record DuffelSegmentDto(
    [property: JsonPropertyName("origin")] DuffelPlaceDto Origin,
    [property: JsonPropertyName("destination")] DuffelPlaceDto Destination,
    [property: JsonPropertyName("departing_at")] string DepartingAt,
    [property: JsonPropertyName("arriving_at")] string ArrivingAt,
    [property: JsonPropertyName("marketing_carrier")] DuffelCarrierDto MarketingCarrier,
    [property: JsonPropertyName("marketing_carrier_flight_number")]
        string MarketingCarrierFlightNumber,
    [property: JsonPropertyName("passengers")] DuffelSegmentPassengerDto[] Passengers
);
