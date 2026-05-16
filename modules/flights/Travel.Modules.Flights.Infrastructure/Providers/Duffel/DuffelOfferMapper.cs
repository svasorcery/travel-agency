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
                $"Cannot parse total_amount '{dto.TotalAmount}' as decimal."
            );

        var moneyResult = Money.Create(amount, currencyResult.Value);
        if (moneyResult.IsError)
            return moneyResult.FirstError;

        // --- slices ---
        var slices = new List<Slice>(dto.Slices.Length);
        foreach (var sliceDto in dto.Slices)
        {
            var segments = new List<Segment>(sliceDto.Segments.Length);
            foreach (var seg in sliceDto.Segments)
            {
                var origin = IataCode.Create(seg.Origin.IataCode);
                if (origin.IsError)
                    return origin.FirstError;

                var destination = IataCode.Create(seg.Destination.IataCode);
                if (destination.IsError)
                    return destination.FirstError;

                var cabinClassStr =
                    seg.Passengers.Length > 0 ? seg.Passengers[0].CabinClass : "economy";
                var cabinClass = CabinClass.Parse(cabinClassStr);
                if (cabinClass.IsError)
                    return cabinClass.FirstError;

                var segment = Segment.Create(
                    origin.Value,
                    destination.Value,
                    seg.DepartingAt,
                    seg.ArrivingAt,
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
        // Rationale: M1 displays a single allowance number on the offer card —
        // not per-direction. For a round-trip with asymmetric allowances
        // (outbound 2 checked, return 1), Max favours the conservative user-facing
        // number; we over-state return-leg allowance rather than under-state outbound.
        // Per-direction baggage rendering is deferred to Subproject 2.
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
            dto.Id
        );
    }
}
