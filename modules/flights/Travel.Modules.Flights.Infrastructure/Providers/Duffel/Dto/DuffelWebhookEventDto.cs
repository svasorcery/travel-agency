using System.Text.Json;
using System.Text.Json.Serialization;

namespace Travel.Modules.Flights.Infrastructure.Providers.Duffel.Dto;

public sealed record DuffelWebhookEventDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("object")] JsonElement Object,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt
);
