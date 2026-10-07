using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using ErrorOr;
using Travel.Modules.Flights.Core.Booking;
using Travel.Modules.Flights.Core.Providers.Dtos;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Offer;
using Travel.Modules.Flights.Infrastructure.Providers.Duffel.Dto;

namespace Travel.Modules.Flights.Infrastructure.Providers.Duffel;

public static class DuffelBookedServicesMapper
{
    public static ErrorOr<BookedOrderFacts> Map(
        DuffelOrderDto order,
        BookableOffer offer,
        QuoteBinding binding,
        BookingPurchase purchase,
        Guid attemptId,
        TimeProvider time
    )
    {
        var invalid = Error.Validation(
            "Flights.OrderFactsInvalid",
            "Complete attributable order facts are unavailable."
        );
        try
        {
            Require(
                order is not null
                    && DuffelAncillaryMapper.Reference(order.Id)
                    && order.Metadata is not null
                    && attemptId != Guid.Empty
                    && order.Metadata.TryGetValue("travel_creation", out var receipt)
                    && receipt == attemptId.ToString("N")
                    && binding.Validate().IsError == false
                    && purchase.QuoteRevision == binding.Revision
                    && order.Slices is { Length: >= 1 and <= 4 }
                    && order.Passengers is { Length: >= 1 and <= 9 }
                    && order.Services is { Length: <= 256 }
                    && order.PaymentStatus?.AwaitingPayment is not null
                    && (
                        order.PaymentStatus.AwaitingPayment == false
                        || order.PaymentStatus.PaymentRequiredBy is not null
                    )
                    && DuffelAncillaryMapper.TryMoney(order.TotalAmount, order.TotalCurrency, out _)
            );
            var total = Money
                .Create(
                    decimal.Parse(
                        order.TotalAmount!,
                        System.Globalization.CultureInfo.InvariantCulture
                    ),
                    CurrencyCode.Create(order.TotalCurrency!).Value
                )
                .Value;
            var slots = binding.Slots.ToDictionary(
                s => s.SupplierReference.Value,
                s => s.Id.Value,
                StringComparer.Ordinal
            );
            Require(
                order.Passengers.All(p =>
                    p is not null && p.Id is not null && slots.ContainsKey(p.Id)
                )
                    && order.Passengers.Select(p => p.Id).Distinct(StringComparer.Ordinal).Count()
                        == slots.Count
                    && order.Passengers.Length == slots.Count
            );
            var dto = new DuffelOfferDto(
                offer.ProviderOfferRef,
                order.TotalAmount!,
                order.TotalCurrency!,
                offer.ExpiresAt,
                order.Slices,
                null,
                order.Passengers.Select(p => new DuffelOfferPassengerDto(p.Id!, "adult")).ToArray(),
                new(false),
                false
            );
            var normalized = DuffelOfferMapper.Map(dto, time);
            Require(
                !normalized.IsError
                    && BookedOrderFacts.SameItinerary(offer.Itinerary, normalized.Value.Itinerary)
            );
            // A unique complete fingerprint proves membership before the parent-scoped order IDs are mapped.
            var addresses = new Dictionary<string, BookingSegmentAddress>(StringComparer.Ordinal);
            var fingerprints = new Dictionary<string, BookingSegmentAddress>(
                StringComparer.Ordinal
            );
            for (var leg = 0; leg < offer.Itinerary.Slices.Count; leg++)
            for (var segment = 0; segment < offer.Itinerary.Slices[leg].Segments.Count; segment++)
                Require(
                    fingerprints.TryAdd(
                        JsonSerializer.Serialize(offer.Itinerary.Slices[leg].Segments[segment]),
                        new(leg, segment)
                    )
                );
            var sliceIds = new HashSet<string>(StringComparer.Ordinal);
            for (var leg = 0; leg < order.Slices.Length; leg++)
            {
                Require(
                    DuffelAncillaryMapper.Reference(order.Slices[leg].Id)
                        && sliceIds.Add(order.Slices[leg].Id!)
                );
                for (var segment = 0; segment < order.Slices[leg].Segments.Length; segment++)
                {
                    var actual = normalized.Value.Itinerary.Slices[leg].Segments[segment];
                    Require(
                        fingerprints.TryGetValue(JsonSerializer.Serialize(actual), out var address)
                            && address == new BookingSegmentAddress(leg, segment)
                            && DuffelAncillaryMapper.Reference(
                                order.Slices[leg].Segments[segment].Id
                            )
                            && addresses.TryAdd(order.Slices[leg].Segments[segment].Id!, address)
                    );
                }
            }
            var lines = new List<BookingService>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var service in order.Services)
            {
                Require(
                    service is not null
                        && DuffelAncillaryMapper.Reference(service.Id)
                        && ids.Add(service.Id!)
                        && service.PassengerIds is { Length: 1 }
                        && slots.ContainsKey(service.PassengerIds[0])
                        && service.SegmentIds is { Length: > 0 and <= 64 }
                        && service.SegmentIds.All(addresses.ContainsKey)
                        && service.SegmentIds.Distinct(StringComparer.Ordinal).Count()
                            == service.SegmentIds.Length
                        && service.Quantity is >= 1 and <= 99
                        && DuffelAncillaryMapper.TryMoney(
                            service.TotalAmount,
                            service.TotalCurrency,
                            out _
                        )
                        && service.TotalCurrency == total.Currency.Value
                        && service.Metadata is not null
                );
                DuffelAncillaryMapper.TryMoney(
                    service.TotalAmount,
                    service.TotalCurrency,
                    out var lineMoney
                );
                var baggage = service.Type == "baggage" && service.Metadata.Type == "checked";
                var seat =
                    service.Type == "seat"
                    && service.Quantity == 1
                    && service.SegmentIds.Length == 1
                    && !string.IsNullOrWhiteSpace(service.Metadata.Designator)
                    && service.Metadata.Designator.Length <= 16
                    && service.Metadata.Disclosures is { Length: <= 32 }
                    && service.Metadata.Disclosures.All(s =>
                        !string.IsNullOrWhiteSpace(s)
                        && s.Length <= 2000
                        && !s.Any(c => char.IsControl(c) && c != '\n')
                    );
                Require(baggage || seat);
                var limits = baggage ? DuffelAncillaryMapper.Limits(service.Metadata) : null;
                Require(
                    limits is null
                        || (limits.MaximumWeightKg is null or >= 0)
                            && (limits.MaximumHeightCm is null or >= 0)
                            && (limits.MaximumDepthCm is null or >= 0)
                            && (limits.MaximumLengthCm is null or >= 0)
                );
                lines.Add(
                    new(
                        service.Id!,
                        baggage ? BookingServiceKind.CheckedBaggage : BookingServiceKind.Seat,
                        slots[service.PassengerIds[0]],
                        new(service.SegmentIds.Select(s => addresses[s]).ToArray()),
                        service.Quantity!.Value,
                        lineMoney,
                        SeatDesignator: seat ? service.Metadata.Designator : null,
                        Name: seat ? service.Metadata.Name : null,
                        Disclosures: seat ? new(service.Metadata.Disclosures!.ToArray()) : null,
                        Baggage: limits
                    )
                );
            }
            Require(
                lines
                    .Where(s => s.Kind == BookingServiceKind.Seat)
                    .GroupBy(s => (s.PassengerId, s.Segments[0]))
                    .All(g => g.Count() == 1)
                    && lines
                        .Where(s => s.Kind == BookingServiceKind.Seat)
                        .GroupBy(s => (s.Segments[0], s.SeatDesignator))
                        .All(g => g.Count() == 1)
            );
            return new BookedOrderFacts(
                order.Id,
                offer.ProviderOfferRef,
                normalized.Value.Itinerary,
                new(order.Passengers.Select(p => slots[p.Id!]).ToArray()),
                new(lines.ToArray()),
                total,
                order.PaymentStatus.AwaitingPayment!.Value,
                order.PaymentStatus.PaymentRequiredBy ?? default,
                order.CancelledAt is not null
            );
        }
        catch (Exception error)
            when (error
                    is FormatException
                        or ArgumentException
                        or NullReferenceException
                        or InvalidOperationException
                        or OverflowException
            )
        {
            return invalid;
        }
    }

    private static void Require([DoesNotReturnIf(false)] bool condition)
    {
        if (!condition)
            throw new FormatException("Incomplete order facts.");
    }
}
