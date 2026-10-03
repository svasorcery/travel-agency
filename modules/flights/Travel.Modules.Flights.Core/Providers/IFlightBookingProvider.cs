using ErrorOr;
using Travel.Modules.Flights.Core.Providers.Dtos;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Travel.Modules.Flights.Core.ValueObjects.Offer;

namespace Travel.Modules.Flights.Core.Providers;

public interface IFlightBookingProvider
{
    ProviderId Id { get; }
    Task<ErrorOr<BookableOffer>> RefreshOfferAsync(string providerOfferRef, CancellationToken ct);
    Task<ErrorOr<HeldOrder>> HoldOfferAsync(
        BookableOffer offer,
        PassengerInfo passenger,
        CancellationToken ct
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

    /// <summary>Success means a terminal cancellation confirmed by the provider, not acceptance of a pending request.</summary>
    Task<ErrorOr<Success>> CancelOrderAsync(string providerOrderId, CancellationToken ct);
    Task<ErrorOr<OrderStatus>> GetOrderStatusAsync(string providerOrderId, CancellationToken ct);
}
