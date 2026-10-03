using System.Globalization;
using ErrorOr;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Travel.Modules.Flights.Core.ValueObjects.Offer;
using Travel.Modules.Flights.Infrastructure.Providers.Duffel.Dto;

namespace Travel.Modules.Flights.Infrastructure.Providers.Duffel;

public static class DuffelOfferMapper
{
    public static ErrorOr<BookableOffer> Map(DuffelOfferDto dto, TimeProvider time)
    {
        if (dto is null)
            return Error.Validation("DuffelOffer.InvalidOffer", "Supplier offer is invalid.");
        // --- currency & money ---
        var currencyResult = CurrencyCode.Create(dto.TotalCurrency);
        if (currencyResult.IsError)
            return currencyResult.FirstError;

        if (
            !decimal.TryParse(
                dto.TotalAmount,
                NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out var amount
            )
        )
            return Error.Validation(
                "DuffelOffer.InvalidAmount",
                "Supplier total amount is invalid."
            );

        var moneyResult = Money.Create(amount, currencyResult.Value);
        if (moneyResult.IsError)
            return moneyResult.FirstError;

        if (
            dto.Passengers is not { Length: >= 1 and <= 9 }
            || dto.Slices is not { Length: > 0 }
            || dto.Slices[0] is null
            || dto.Slices[0].Segments is not { Length: > 0 }
            || dto.Slices[0].Segments[0] is null
        )
            return Error.Validation(
                "DuffelOffer.InvalidParty",
                "Offer passenger binding is invalid."
            );
        var slots = new List<SupplierPassengerSlot>();
        foreach (var passenger in dto.Passengers)
        {
            if (passenger is null || passenger.Type != "adult")
                return Error.Validation(
                    "DuffelOffer.InvalidParty",
                    "Only adult passenger bindings are supported."
                );
            var reference = SupplierPassengerReference.Create(passenger.Id);
            if (reference.IsError)
                return reference.Errors;
            slots.Add(new SupplierPassengerSlot(reference.Value, BookingPassengerKind.Adult));
        }
        var refs = slots.Select(p => p.Reference.Value).ToHashSet(StringComparer.Ordinal);
        if (refs.Count != slots.Count)
            return Error.Validation(
                "DuffelOffer.InvalidParty",
                "Offer passenger references must be unique."
            );
        // --- slices ---
        var slices = new List<Slice>(dto.Slices.Length);
        foreach (var sliceDto in dto.Slices)
        {
            if (sliceDto is null || sliceDto.Segments is not { Length: > 0 })
                return Error.Validation("DuffelOffer.InvalidParty", "Offer itinerary is invalid.");
            var segments = new List<Segment>(sliceDto.Segments.Length);
            foreach (var seg in sliceDto.Segments)
            {
                if (
                    seg is null
                    || seg.Origin is null
                    || seg.Destination is null
                    || seg.MarketingCarrier is null
                )
                    return Error.Validation(
                        "DuffelOffer.InvalidParty",
                        "Offer segment is invalid."
                    );
                var origin = IataCode.Create(seg.Origin.IataCode);
                if (origin.IsError)
                    return origin.FirstError;

                var destination = IataCode.Create(seg.Destination.IataCode);
                if (destination.IsError)
                    return destination.FirstError;

                if (
                    seg.Passengers is null
                    || seg.Passengers.Length != refs.Count
                    || seg.Passengers.Any(p =>
                        p is null || p.PassengerId is null || !refs.Contains(p.PassengerId)
                    )
                    || seg.Passengers.Select(p => p.PassengerId)
                        .Distinct(StringComparer.Ordinal)
                        .Count() != refs.Count
                    || seg.Passengers.Select(p => p.CabinClass)
                        .Distinct(StringComparer.Ordinal)
                        .Count() != 1
                )
                    return Error.Validation(
                        "DuffelOffer.InvalidParty",
                        "Segment passenger bindings or cabins are inconsistent."
                    );
                var cabinClassStr = seg.Passengers[0].CabinClass;
                var cabinClass = CabinClass.Parse(cabinClassStr);
                if (cabinClass.IsError)
                    return Error.Validation(
                        "DuffelOffer.InvalidCabin",
                        "Supplier cabin is invalid."
                    );

                var departure = DuffelAirportTimeResolver.Resolve(
                    seg.DepartingAt,
                    seg.Origin.TimeZone
                );
                if (departure.IsError)
                    return departure.Errors;
                var arrival = DuffelAirportTimeResolver.Resolve(
                    seg.ArrivingAt,
                    seg.Destination.TimeZone
                );
                if (arrival.IsError)
                    return arrival.Errors;
                if (seg.Passengers.Any(p => p.Baggages?.Any(b => b is null) == true))
                    return Error.Validation(
                        "DuffelOffer.InvalidBaggage",
                        "Supplier baggage is invalid."
                    );

                var segment = Segment.Create(
                    origin.Value,
                    destination.Value,
                    departure.Value,
                    arrival.Value,
                    seg.MarketingCarrier.IataCode,
                    seg.MarketingCarrierFlightNumber,
                    cabinClass.Value
                );
                if (segment.IsError)
                    return segment.FirstError;

                segments.Add(segment.Value);
            }

            var slice = Slice.Create(segments);
            if (slice.IsError)
                return slice.FirstError;

            slices.Add(slice.Value);
        }

        var itinerary = Itinerary.Create(slices);
        if (itinerary.IsError)
            return itinerary.FirstError;

        var party = BookableOfferParty.Create(
            slots,
            DateOnly.FromDateTime(slices[0].DepartAt.Date),
            dto.PaymentRequirements?.RequiresInstantPayment is { } instant ? !instant : null,
            dto.PassengerIdentityDocumentsRequired
        );
        if (party.IsError)
            return party.Errors;

        // --- fare conditions ---
        var changeAllowed = dto.Conditions?.ChangeBeforeDeparture?.Allowed ?? false;
        var refundAllowed = dto.Conditions?.RefundBeforeDeparture?.Allowed ?? false;

        // Read the first non-null fare brand across ALL slices (not just the first),
        // so multi-leg offers where only the return slice carries the brand are handled.
        var fareBasisCode = dto.Slices.Select(s => s.FareBrandName).FirstOrDefault(n => n != null);

        var firstSlice = dto.Slices.Length > 0 ? dto.Slices[0] : null;
        var firstSegment = firstSlice?.Segments.Length > 0 ? firstSlice.Segments[0] : null;
        var cabinClassMarketing =
            firstSegment?.Passengers.Length > 0
                ? firstSegment.Passengers[0].CabinClassMarketingName
                : null;

        // We aggregate baggage across slices/segments/passengers with Max.
        // This is only the maximum on an individual segment, not a guaranteed
        // allowance for every leg of the itinerary. The frontend labels it as such.
        // Per-direction baggage rendering requires a richer contract.
        // Guard against missing baggages array (null-coalesce).
        var allBaggages = dto
            .Slices.SelectMany(s => s.Segments)
            .SelectMany(seg => seg.Passengers)
            .SelectMany(p => p.Baggages ?? [])
            .ToList();

        var checkedBags = allBaggages
            .Where(b => b.Type == "checked")
            .Select(b => b.Quantity)
            .DefaultIfEmpty(0)
            .Max();

        var carryOnBags = allBaggages
            .Where(b => b.Type == "carry_on")
            .Select(b => b.Quantity)
            .DefaultIfEmpty(0)
            .Max();

        var fareConditions = new FareConditions(
            changeAllowed,
            refundAllowed,
            fareBasisCode,
            cabinClassMarketing,
            CheckedBaggageQuantity: checkedBags,
            CarryOnBaggageQuantity: carryOnBags
        );

        return new BookableOffer(
            OfferId.New(),
            itinerary.Value,
            moneyResult.Value,
            ProviderId.Duffel,
            time.GetUtcNow(),
            dto.ExpiresAt,
            fareConditions,
            dto.Id,
            party.Value
        );
    }
}
