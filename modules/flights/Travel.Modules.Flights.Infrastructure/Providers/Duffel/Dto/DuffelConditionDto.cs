using System.Text.Json.Serialization;

namespace Travel.Modules.Flights.Infrastructure.Providers.Duffel.Dto;

[JsonConverter(typeof(DuffelConditionJsonConverter))]
public sealed record DuffelConditionDto(
    [property: JsonPropertyName("allowed")] bool? Allowed,
    [property: JsonPropertyName("penalty_amount")] string? PenaltyAmount = null,
    [property: JsonPropertyName("penalty_currency")] string? PenaltyCurrency = null,
    [property: JsonIgnore] bool InvalidData = false
);
