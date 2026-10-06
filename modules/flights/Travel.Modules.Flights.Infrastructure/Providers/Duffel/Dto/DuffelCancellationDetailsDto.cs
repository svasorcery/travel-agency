using System.Text.Json;
using System.Text.Json.Serialization;

namespace Travel.Modules.Flights.Infrastructure.Providers.Duffel.Dto;

public sealed record DuffelCancellationDetailsDto
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("order_id")]
    public string? OrderId { get; init; }

    [JsonPropertyName("created_at")]
    public JsonElement CreatedAt { get; init; }

    [JsonPropertyName("expires_at")]
    public JsonElement ExpiresAt { get; init; }

    [JsonPropertyName("confirmed_at")]
    public JsonElement ConfirmedAt { get; init; }

    [JsonPropertyName("refund_amount")]
    public JsonElement RefundAmount { get; init; }

    [JsonPropertyName("refund_currency")]
    public JsonElement RefundCurrency { get; init; }

    [JsonPropertyName("refund_to")]
    public JsonElement RefundTo { get; init; }

    [JsonPropertyName("airline_credits")]
    public JsonElement AirlineCredits { get; init; }

    public override string ToString() => nameof(DuffelCancellationDetailsDto);
}

public sealed record DuffelCancellationDetailsResponseDto(
    [property: JsonPropertyName("data")] DuffelCancellationDetailsDto? Data
);

public sealed record DuffelCancellationErrorDto(
    [property: JsonPropertyName("code")] string? Code,
    [property: JsonPropertyName("type")] string? Type
);

public sealed record DuffelCancellationErrorsDto(
    [property: JsonPropertyName("errors")] DuffelCancellationErrorDto[]? Errors
);
