using ErrorOr;
using Travel.Modules.Flights.Core.Providers.Dtos;
using Travel.Modules.Flights.Core.ValueObjects;

namespace Travel.Modules.Flights.Core.Booking;

public static class BookingPurchaseFactory
{
    public static ErrorOr<BookingPurchase> Select(
        AncillaryCatalogFacts catalog,
        QuoteBinding binding,
        Guid? owner,
        IEnumerable<AncillarySelection> selections
    )
    {
        var invalid = Error.Validation(
            "Flights.ServiceUnavailable",
            "Selected services are unavailable; review current choices."
        );
        if (
            catalog is null
            || binding is null
            || binding.Validate().IsError
            || selections is null
            || catalog.Offer.Party != binding.Party
            || catalog.UnsupportedPricing
        )
            return invalid;
        var requested = selections.Take(257).ToArray();
        if (
            requested.Length > 256
            || requested.Any(s => s is null || string.IsNullOrWhiteSpace(s.SelectionKey))
            || requested.Select(s => s.SelectionKey).Distinct(StringComparer.Ordinal).Count()
                != requested.Length
        )
            return invalid;
        if (
            requested.Length > 0
            && (owner is null || owner == Guid.Empty || catalog.UnsupportedPricing)
        )
            return invalid;
        var slots = binding.Slots.ToDictionary(
            s => s.SupplierReference.Value,
            StringComparer.Ordinal
        );
        var services = catalog.Services.ToDictionary(s => s.Key, StringComparer.Ordinal);
        var lines = new List<BookingService>();
        try
        {
            foreach (var selection in requested)
            {
                if (
                    !services.TryGetValue(selection.SelectionKey, out var service)
                    || !service.Selectable
                    || service.Kind is null
                    || service.UnitPrice is null
                    || selection.Quantity < 1
                    || selection.Quantity > service.MaximumQuantity
                    || service.PassengerReferences.Count != 1
                    || !slots.TryGetValue(service.PassengerReferences[0], out var slot)
                    || service.Segments.Any(s =>
                        s.Leg < 0
                        || s.Leg >= catalog.Offer.Itinerary.Slices.Count
                        || s.Segment < 0
                        || s.Segment >= catalog.Offer.Itinerary.Slices[s.Leg].Segments.Count
                    )
                )
                    return invalid;
                var lineTotal = Money.Create(
                    checked(service.UnitPrice.Amount * selection.Quantity),
                    service.UnitPrice.Currency
                );
                if (lineTotal.IsError)
                    return invalid;
                lines.Add(
                    new(
                        service.Key,
                        service.Kind.Value,
                        slot.Id.Value,
                        service.Segments,
                        selection.Quantity,
                        lineTotal.Value,
                        service.UnitPrice,
                        service.SeatDesignator,
                        service.PhysicalSeat,
                        service.Name,
                        service.Disclosures,
                        service.Baggage
                    )
                );
            }
        }
        catch (OverflowException)
        {
            return invalid;
        }
        return BookingPurchase.Create(
            binding.Revision,
            owner,
            catalog.Offer.TotalAmount,
            catalog.Offer.ExpiresAt,
            lines
        );
    }
}
