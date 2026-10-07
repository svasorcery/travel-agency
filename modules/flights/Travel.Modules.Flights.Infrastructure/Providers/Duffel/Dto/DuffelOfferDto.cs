using System.Text.Json.Serialization;

namespace Travel.Modules.Flights.Infrastructure.Providers.Duffel.Dto;

public sealed record DuffelOfferDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("total_amount")] string TotalAmount,
    [property: JsonPropertyName("total_currency")] string TotalCurrency,
    [property: JsonPropertyName("expires_at")] DateTimeOffset ExpiresAt,
    [property: JsonPropertyName("slices")] DuffelSliceDto[] Slices,
    [property: JsonPropertyName("conditions")] DuffelConditionsDto? Conditions,
    [property: JsonPropertyName("passengers")] DuffelOfferPassengerDto[]? Passengers = null,
    [property: JsonPropertyName("payment_requirements")]
        DuffelPaymentRequirementsDto? PaymentRequirements = null,
    [property: JsonPropertyName("passenger_identity_documents_required")]
        bool? PassengerIdentityDocumentsRequired = null,
    [property: JsonPropertyName("available_services")]
        DuffelAncillaryServiceDto[]? AvailableServices = null,
    [property: JsonPropertyName("intended_services")]
        System.Text.Json.JsonElement? IntendedServices = null,
    [property: JsonPropertyName("intended_payment_methods")]
        System.Text.Json.JsonElement? IntendedPaymentMethods = null
);

/// <summary>Wraps a single offer response from GET /air/offers/{id}.</summary>
public sealed record DuffelOfferResponseDto(
    [property: JsonPropertyName("data")] DuffelOfferDto Data
);
