using ErrorOr;
using Travel.Modules.Flights.Core.Booking;
using Travel.Modules.Flights.Core.Cancellation;
using Travel.Modules.Flights.Core.Providers.Dtos;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Shared.Abstractions;

namespace Travel.Modules.Flights.Api.Contracts;

public sealed record BookingServiceProofRequest(
    Guid QuoteRevision,
    string ProviderOrderRef,
    PurchaseServiceDto[] Services
)
{
    internal ErrorOr<BookingServiceProof> ToProof()
    {
        var lines = BookingEvidenceRequests.Services(Services);
        return lines.IsError
            ? lines.Errors
            : new BookingServiceProof(QuoteRevision, ProviderOrderRef, lines.Value);
    }
}

public sealed record BookingCreationProofRequest(
    Guid AttemptId,
    Guid OwnerId,
    Guid QuoteRevision,
    bool SupplierOperationCompleted,
    bool NoCreatedOrPendingOrderConfirmed,
    BookedOrderEvidenceRequest? Order = null
)
{
    internal ErrorOr<BookingCreationProof> ToProof()
    {
        BookedOrderFacts? facts = null;
        if (Order is not null)
        {
            var mapped = Order.ToFacts();
            if (mapped.IsError)
                return mapped.Errors;
            facts = mapped.Value;
        }
        return new BookingCreationProof(
            AttemptId,
            OwnerId,
            QuoteRevision,
            SupplierOperationCompleted,
            NoCreatedOrPendingOrderConfirmed,
            facts
        );
    }
}

public sealed record BookedOrderEvidenceRequest(
    string ProviderOrderRef,
    string ProviderOfferRef,
    ItineraryDto Itinerary,
    Guid[] PassengerIds,
    PurchaseServiceDto[] Services,
    CancellationMoneyRequest Total,
    bool AwaitingPayment,
    DateTimeOffset? PaymentRequiredBy,
    bool Cancelled = false
)
{
    internal ErrorOr<BookedOrderFacts> ToFacts()
    {
        if (
            PassengerIds is not { Length: >= 1 and <= 9 }
            || PassengerIds.Any(p => p == Guid.Empty)
            || PassengerIds.Distinct().Count() != PassengerIds.Length
            || Total is null
            || Itinerary?.Slices is not { Length: >= 1 and <= 4 }
        )
            return CancellationRequestValidation.Invalid;
        var money = Total.ToMoney();
        var services = BookingEvidenceRequests.Services(Services);
        if (money.IsError || services.IsError)
            return CancellationRequestValidation.Invalid;
        var slices = new List<Slice>();
        foreach (var slice in Itinerary.Slices)
        {
            if (slice?.Segments is not { Length: > 0 and <= 64 })
                return CancellationRequestValidation.Invalid;
            var segments = new List<Segment>();
            foreach (var segment in slice.Segments)
            {
                if (segment is null || segment.DepartAt == default || segment.ArriveAt == default)
                    return CancellationRequestValidation.Invalid;
                var origin = IataCode.Create(segment.Origin);
                var destination = IataCode.Create(segment.Destination);
                var cabin = CabinClass.Parse(segment.CabinClass);
                if (origin.IsError || destination.IsError || cabin.IsError)
                    return CancellationRequestValidation.Invalid;
                var created = Segment.Create(
                    origin.Value,
                    destination.Value,
                    segment.DepartAt,
                    segment.ArriveAt,
                    segment.CarrierCode,
                    segment.FlightNumber,
                    cabin.Value
                );
                if (created.IsError)
                    return CancellationRequestValidation.Invalid;
                segments.Add(created.Value);
            }
            var createdSlice = Slice.Create(segments);
            if (
                createdSlice.IsError
                || createdSlice.Value.Origin.Value != slice.Origin
                || createdSlice.Value.Destination.Value != slice.Destination
            )
                return CancellationRequestValidation.Invalid;
            slices.Add(createdSlice.Value);
        }
        var itinerary = Core.ValueObjects.Itinerary.Create(slices);
        if (itinerary.IsError)
            return CancellationRequestValidation.Invalid;
        return new BookedOrderFacts(
            ProviderOrderRef,
            ProviderOfferRef,
            itinerary.Value,
            new(PassengerIds.ToArray()),
            services.Value,
            money.Value,
            AwaitingPayment,
            PaymentRequiredBy ?? default,
            Cancelled
        );
    }
}

internal static class BookingEvidenceRequests
{
    internal static ErrorOr<EquatableArray<BookingService>> Services(PurchaseServiceDto[]? services)
    {
        if (services is null || services.Length > 256 || services.Any(s => s is null))
            return CancellationRequestValidation.Invalid;
        var lines = new List<BookingService>();
        foreach (var service in services)
        {
            if (
                service.LineTotal is null
                || service.Segments is null
                || service.Kind is not ("seat" or "checked-baggage")
            )
                return CancellationRequestValidation.Invalid;
            var money = new CancellationMoneyRequest(
                service.LineTotal.Amount,
                service.LineTotal.Currency
            ).ToMoney();
            if (money.IsError)
                return CancellationRequestValidation.Invalid;
            lines.Add(
                new(
                    service.SelectionKey,
                    service.Kind == "seat"
                        ? BookingServiceKind.Seat
                        : BookingServiceKind.CheckedBaggage,
                    service.BookingPassengerId,
                    new(service.Segments.ToArray()),
                    service.Quantity,
                    money.Value,
                    SeatDesignator: service.SeatDesignator,
                    Disclosures: service.Disclosures is { } disclosures
                        ? new(disclosures.ToArray())
                        : null,
                    Baggage: service.Baggage
                )
            );
        }
        var result = new EquatableArray<BookingService>(lines.ToArray());
        return BookingServiceProof.ValidLines(result)
            ? result
            : CancellationRequestValidation.Invalid;
    }
}
