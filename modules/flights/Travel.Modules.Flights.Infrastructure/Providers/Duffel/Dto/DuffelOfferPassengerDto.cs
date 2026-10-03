using System.Text.Json.Serialization;

namespace Travel.Modules.Flights.Infrastructure.Providers.Duffel.Dto;

public sealed record DuffelOfferPassengerDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("type")] string Type
);

public sealed record DuffelPaymentRequirementsDto(
    [property: JsonPropertyName("requires_instant_payment")] bool? RequiresInstantPayment
);
