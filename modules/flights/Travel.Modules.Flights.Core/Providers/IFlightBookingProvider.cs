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
    Task<ErrorOr<ConfirmedOrder>> ConfirmOrderAsync(
        string providerOrderId,
        PaymentRef payment,
        CancellationToken ct
    );
    Task<ErrorOr<Success>> CancelOrderAsync(string providerOrderId, CancellationToken ct);
    Task<ErrorOr<OrderStatus>> GetOrderStatusAsync(string providerOrderId, CancellationToken ct);
}
