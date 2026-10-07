using System.Text.Json;
using Travel.Modules.Flights.Core.Booking;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Shared.Abstractions;

namespace Travel.Modules.Flights.Core.Providers.Dtos;

public sealed record BookedOrderFacts(
    string ProviderOrderId,
    string ProviderOfferRef,
    Itinerary Itinerary,
    EquatableArray<Guid> PassengerIds,
    EquatableArray<BookingService> Services,
    Money Total,
    bool AwaitingPayment,
    DateTimeOffset PaymentRequiredBy,
    bool Cancelled = false
)
{
    public bool Matches(BookingPurchase purchase) =>
        Total == purchase.Total && ServicesMatch(purchase.Services);

    public bool ServicesMatch(EquatableArray<BookingService> expected) =>
        ServicesMatch(Services, expected);

    public static bool ServicesMatch(
        EquatableArray<BookingService> actualServices,
        EquatableArray<BookingService> expected
    )
    {
        if (
            !BookingServiceProof.ValidLines(actualServices)
            || !BookingServiceProof.ValidLines(expected)
        )
            return false;
        if (expected.Count != actualServices.Count)
            return false;
        var remaining = actualServices.ToList();
        foreach (var selected in expected)
        {
            var matches = remaining.Where(actual => SameService(selected, actual)).ToArray();
            if (matches.Length != 1)
                return false;
            remaining.Remove(matches[0]);
        }
        return remaining.Count == 0;
    }

    public static bool SameItinerary(Itinerary a, Itinerary b) =>
        JsonSerializer.Serialize(a) == JsonSerializer.Serialize(b);

    private static bool SameService(BookingService a, BookingService b) =>
        a.Kind == b.Kind
        && a.PassengerId == b.PassengerId
        && a.Quantity == b.Quantity
        && a.LineTotal == b.LineTotal
        && a.SeatDesignator == b.SeatDesignator
        && a.Segments.OrderBy(s => s.Leg)
            .ThenBy(s => s.Segment)
            .SequenceEqual(b.Segments.OrderBy(s => s.Leg).ThenBy(s => s.Segment))
        && (a.Disclosures?.ToArray() ?? []).SequenceEqual(b.Disclosures?.ToArray() ?? [])
        && a.Baggage == b.Baggage;
}

public enum BookingEvidenceSource
{
    None = 0,
    SupplierApi = 1,
    OperatorVerified = 2,
    TravelAdmission = 3,
}

public sealed record BookingCreationObservation(
    BookingCreationOutcome Outcome,
    BookedOrderFacts? Order,
    string? KnownOrderId,
    bool ReceiptCorrelated,
    bool SenderCompleted,
    string Reason,
    DateTimeOffset ObservedAt,
    BookingEvidenceSource Source = BookingEvidenceSource.SupplierApi,
    bool PositiveNoEffects = false
);
