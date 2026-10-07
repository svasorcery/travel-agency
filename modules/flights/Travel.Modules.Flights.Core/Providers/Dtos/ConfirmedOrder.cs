using Travel.Modules.Flights.Core.Booking;

namespace Travel.Modules.Flights.Core.Providers.Dtos;

public sealed record ConfirmedOrder(
    string ProviderOrderId,
    DateTimeOffset ConfirmedAt,
    SupplierPaymentEvidence? PaymentEvidence = null,
    BookingServiceProof? ServiceProof = null
);
