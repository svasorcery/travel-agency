using System.Text.Json.Serialization;

namespace Travel.Modules.Flights.Infrastructure.Providers.Duffel.Dto;

public sealed record DuffelOrderDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("booking_reference")] string? BookingReference,
    [property: JsonPropertyName("documents")] DuffelDocumentDto[]? Documents,
    [property: JsonPropertyName("cancelled_at")] DateTimeOffset? CancelledAt
);
