using System.Text.Json.Serialization;

namespace Travel.Modules.Flights.Infrastructure.Providers.Duffel.Dto;

public sealed record DuffelOrderDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("booking_reference")] string? BookingReference,
    [property: JsonPropertyName("documents")] DuffelDocumentDto[]? Documents,
    [property: JsonPropertyName("cancelled_at")] DateTimeOffset? CancelledAt,
    [property: JsonPropertyName("total_amount")] string? TotalAmount,
    [property: JsonPropertyName("total_currency")] string? TotalCurrency,
    [property: JsonPropertyName("payment_status")] DuffelPaymentStatusDto? PaymentStatus
);

public sealed record DuffelPaymentStatusDto(
    [property: JsonPropertyName("payment_required_by")] DateTimeOffset? PaymentRequiredBy
);

public sealed record DuffelOrderResponseDto(
    [property: JsonPropertyName("data")] DuffelOrderDto Data
);

public sealed record DuffelCancellationDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("order_id")] string OrderId
);

public sealed record DuffelCancellationResponseDto(
    [property: JsonPropertyName("data")] DuffelCancellationDto Data
);
