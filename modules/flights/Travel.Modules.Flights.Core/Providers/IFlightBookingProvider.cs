using ErrorOr;
using Travel.Modules.Flights.Core.Booking;
using Travel.Modules.Flights.Core.Providers.Dtos;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Travel.Modules.Flights.Core.ValueObjects.Offer;
using Travel.Shared.Abstractions;

namespace Travel.Modules.Flights.Core.Providers;

public interface IFlightBookingProvider
{
    ProviderId Id { get; }
    Task<ErrorOr<BookableOffer>> RefreshOfferAsync(string providerOfferRef, CancellationToken ct);
    Task<ErrorOr<BookingCreationObservation>> HoldOfferAsync(
        BookableOffer offer,
        QuoteBinding binding,
        EquatableArray<BookingPassenger> passengers,
        BookingPurchase purchase,
        Guid attemptId,
        CancellationToken ct
    );
    Task<ErrorOr<BookedOrderFacts>> ReadOrderForBookingAsync(
        string orderId,
        BookableOffer offer,
        QuoteBinding binding,
        BookingPurchase purchase,
        Guid attemptId,
        CancellationToken ct
    ) =>
        Task.FromResult<ErrorOr<BookedOrderFacts>>(
            Error.Failure("Flights.OrderReadUnsupported", "Order facts unavailable.")
        );
    Task<ErrorOr<Success>> ValidateConfirmationAsync(
        string providerOrderId,
        Money expectedTotal,
        CancellationToken ct
    );
    Task<ErrorOr<ConfirmedOrder>> ConfirmOrderAsync(
        string providerOrderId,
        PaymentRef payment,
        Money expectedTotal,
        CancellationToken ct
    );

    /// <summary>The provider rechecks this fence after its final awaited read, before payment mutation.</summary>
    Task<ErrorOr<ConfirmedOrder>> ConfirmOrderAsync(
        string providerOrderId,
        PaymentRef payment,
        Money expectedTotal,
        Func<CancellationToken, Task<bool>> canDispatch,
        CancellationToken ct
    ) =>
        Task.FromResult<ErrorOr<ConfirmedOrder>>(
            Error.Failure(
                "Flights.ConfirmationFenceNotSupported",
                "Guarded supplier confirmation is unsupported."
            )
        );

    Task<ErrorOr<ConfirmedOrder>> ConfirmOrderAsync(
        string providerOrderId,
        PaymentRef payment,
        Money expectedTotal,
        BookingOrderContext context,
        Func<CancellationToken, Task<bool>> canDispatch,
        CancellationToken ct
    ) =>
        Task.FromResult<ErrorOr<ConfirmedOrder>>(
            Error.Failure(
                "Flights.ServiceProofUnsupported",
                "Service-aware confirmation is unsupported."
            )
        );

    /// <summary>Success means a terminal cancellation confirmed by the provider, not acceptance of a pending request.</summary>
    Task<ErrorOr<Success>> CancelOrderAsync(string providerOrderId, CancellationToken ct);
    Task<ErrorOr<OrderStatus>> GetOrderStatusAsync(string providerOrderId, CancellationToken ct);
}
