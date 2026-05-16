using System.Text.Json.Serialization;

namespace Travel.Modules.Flights.Infrastructure.Providers.Duffel.Dto;

public sealed record DuffelConditionDto([property: JsonPropertyName("allowed")] bool Allowed);
