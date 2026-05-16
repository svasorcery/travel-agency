using System.Text.Json.Serialization;

namespace Travel.Modules.Flights.Infrastructure.Providers.Duffel.Dto;

public sealed record DuffelConditionsDto(
    [property: JsonPropertyName("change_before_departure")]
        DuffelConditionDto? ChangeBeforeDeparture,
    [property: JsonPropertyName("refund_before_departure")]
        DuffelConditionDto? RefundBeforeDeparture
);
