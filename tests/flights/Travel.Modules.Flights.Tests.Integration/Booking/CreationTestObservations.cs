using Travel.Modules.Flights.Application.Cancellation;
using Travel.Modules.Flights.Core.Booking;
using Travel.Modules.Flights.Core.Providers.Dtos;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Offer;

namespace Travel.Modules.Flights.Tests.Integration.Booking;

internal static class CreationTestObservations
{
    internal static IDispatchInstanceIdentity Instance { get; } = new Sender();

    internal static BookingCreationObservation Matches(
        BookableOffer offer,
        QuoteBinding binding,
        BookingPurchase purchase,
        string orderId,
        DateTimeOffset deadline
    ) =>
        new(
            BookingCreationOutcome.Matches,
            new(
                orderId,
                offer.ProviderOfferRef,
                offer.Itinerary,
                new(binding.Slots.Select(s => s.Id.Value).ToArray()),
                BookingServiceProof.Copy(purchase.Services),
                purchase.Total,
                true,
                deadline
            ),
            orderId,
            true,
            true,
            "OrderObserved",
            offer.FetchedAt
        );

    private sealed class Sender : IDispatchInstanceIdentity
    {
        public Guid Id { get; } = Guid.NewGuid();
    }
}
