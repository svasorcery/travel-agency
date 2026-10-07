using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using ErrorOr;
using Travel.Modules.Flights.Core.Booking;
using Travel.Modules.Flights.Core.Providers.Dtos;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Infrastructure.Providers.Duffel.Dto;
using Travel.Shared.Abstractions;

namespace Travel.Modules.Flights.Infrastructure.Providers.Duffel;

public static partial class DuffelAncillaryMapper
{
    internal static bool HasUnsupportedPricing(DuffelOfferDto offer) =>
        HasIntent(offer.IntendedServices) || HasIntent(offer.IntendedPaymentMethods);

    [GeneratedRegex(@"^[A-Za-z0-9_-]{1,256}$")]
    private static partial Regex ReferencePattern();

    [GeneratedRegex(@"^[0-9]+(?:\.[0-9]{1,28})?$")]
    private static partial Regex AmountPattern();

    public static bool Reference(string? value) =>
        value is not null && ReferencePattern().IsMatch(value);

    public static bool TryMoney(string? value, string? currency, out Money money)
    {
        money = null!;
        if (
            value is null
            || value.Length > 70
            || !AmountPattern().IsMatch(value)
            || currency is null
            || !decimal.TryParse(
                value,
                NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out var amount
            )
        )
            return false;
        var code = CurrencyCode.Create(currency);
        if (
            code.IsError
            || Canonical(value)
                != Canonical(
                    amount.ToString("0.############################", CultureInfo.InvariantCulture)
                )
        )
            return false;
        var created = Money.Create(amount, code.Value);
        if (created.IsError)
            return false;
        money = created.Value;
        return true;
    }

    private static string Canonical(string value)
    {
        var parts = value.Split('.');
        var whole = parts[0].TrimStart('0');
        if (whole.Length == 0)
            whole = "0";
        var fraction = parts.Length == 2 ? parts[1].TrimEnd('0') : "";
        return whole + (fraction.Length > 0 ? "." + fraction : "");
    }

    public static ErrorOr<AncillaryCatalogFacts> Map(
        DuffelOfferDto offer,
        DuffelSeatMapDto[]? maps,
        bool seatsRequested,
        TimeProvider time
    )
    {
        try
        {
            Require(
                offer is not null && Reference(offer.Id) && offer.AvailableServices is not null
            );
            Require(TryMoney(offer.TotalAmount, offer.TotalCurrency, out _));
            var mapped = DuffelOfferMapper.Map(offer, time);
            if (mapped.IsError)
                return Error.Validation(
                    "Flights.AncillaryCatalogInvalid",
                    "Offer facts are unavailable."
                );
            var passengers = offer.Passengers!.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
            var segmentRefs = new Dictionary<string, BookingSegmentAddress>(StringComparer.Ordinal);
            var sliceRefs = new Dictionary<string, int>(StringComparer.Ordinal);
            var allowances = new List<IncludedBaggageFact>();
            for (var leg = 0; leg < offer.Slices.Length; leg++)
            {
                var slice = offer.Slices[leg];
                Require(Reference(slice.Id) && sliceRefs.TryAdd(slice.Id!, leg));
                for (var segment = 0; segment < slice.Segments.Length; segment++)
                {
                    var dto = slice.Segments[segment];
                    Require(Reference(dto.Id) && segmentRefs.TryAdd(dto.Id!, new(leg, segment)));
                    foreach (var adult in dto.Passengers)
                    {
                        Require(
                            adult.PassengerId is not null && passengers.Contains(adult.PassengerId)
                        );
                        if (adult.Baggages is null)
                            allowances.Add(new(adult.PassengerId!, new(leg, segment), null, null));
                        else
                        {
                            Require(
                                adult.Baggages.All(b =>
                                    b is not null && b.Quantity is >= 0 and <= 99
                                )
                            );
                            allowances.Add(
                                new(
                                    adult.PassengerId!,
                                    new(leg, segment),
                                    adult
                                        .Baggages.Where(b => b.Type == "checked")
                                        .Sum(b => b.Quantity),
                                    adult
                                        .Baggages.Where(b => b.Type == "carry_on")
                                        .Sum(b => b.Quantity)
                                )
                            );
                        }
                    }
                }
            }
            Require(segmentRefs.Count <= 64 && offer.AvailableServices!.Length <= 512);
            var services = new List<AncillaryService>();
            foreach (var service in offer.AvailableServices!)
            {
                Require(
                    service is not null
                        && Reference(service.Id)
                        && service.PassengerIds is { Length: > 0 }
                        && service.SegmentIds is { Length: > 0 }
                );
                Require(
                    service.PassengerIds!.All(passengers.Contains)
                        && service.PassengerIds.Distinct(StringComparer.Ordinal).Count()
                            == service.PassengerIds.Length
                        && service.SegmentIds!.All(segmentRefs.ContainsKey)
                        && service.SegmentIds.Distinct(StringComparer.Ordinal).Count()
                            == service.SegmentIds.Length
                );
                var priceKnown = TryMoney(
                    service.TotalAmount,
                    service.TotalCurrency,
                    out var price
                );
                var supported = service.Type == "baggage" && service.Metadata?.Type == "checked";
                var limits = service.Metadata is { } metadata ? Limits(metadata) : null;
                var quantity = service.MaximumQuantity ?? 0;
                var selectable =
                    supported
                    && service.PassengerIds.Length == 1
                    && priceKnown
                    && price.Currency == mapped.Value.TotalAmount.Currency
                    && quantity is > 0 and <= 99
                    && ValidLimits(limits);
                services.Add(
                    new(
                        service.Id!,
                        supported ? BookingServiceKind.CheckedBaggage : null,
                        new(service.PassengerIds.ToArray()),
                        new(service.SegmentIds!.Select(id => segmentRefs[id]).ToArray()),
                        priceKnown ? price : null,
                        quantity,
                        selectable,
                        selectable ? null : "ServiceUnavailable",
                        Name: "Extra checked baggage",
                        Baggage: limits
                    )
                );
            }

            var seatMaps = new List<AncillarySeatMap>();
            var physicalLocations =
                new Dictionary<(BookingSegmentAddress, string), HashSet<string>>();
            if (maps is not null)
            {
                Require(maps.Length <= segmentRefs.Count);
                var mapSegments = new HashSet<BookingSegmentAddress>();
                foreach (var map in maps)
                {
                    Require(
                        map is not null
                            && Reference(map.Id)
                            && map.SegmentId is not null
                            && segmentRefs.TryGetValue(map.SegmentId, out _)
                            && map.SliceId is not null
                            && sliceRefs.ContainsKey(map.SliceId)
                            && map.Cabins is { Length: > 0 and <= 16 }
                    );
                    var address = segmentRefs[map.SegmentId!];
                    Require(sliceRefs[map.SliceId!] == address.Leg && mapSegments.Add(address));
                    var cabins = new List<AncillarySeatCabin>();
                    var elementCount = 0;
                    for (var cabinIndex = 0; cabinIndex < map.Cabins!.Length; cabinIndex++)
                    {
                        var cabin = map.Cabins[cabinIndex];
                        Require(
                            cabin is not null
                                && cabin.Deck is >= 0 and <= 1
                                && cabin.Rows is not null
                        );
                        var cabinClass = CabinClass.Parse(cabin.CabinClass ?? "");
                        Require(!cabinClass.IsError);
                        var rows = new List<AncillarySeatRow>();
                        foreach (var row in cabin.Rows!)
                        {
                            Require(row is not null && row.Sections is not null);
                            var sections = new List<AncillarySeatSection>();
                            foreach (var section in row.Sections!)
                            {
                                Require(section is not null && section.Elements is not null);
                                var elements = new List<AncillarySeatElement>();
                                foreach (var element in section.Elements!)
                                {
                                    Require(element is not null && ++elementCount <= 4096);
                                    var keys = new List<string>();
                                    if (element.Type == "seat")
                                    {
                                        Require(
                                            !string.IsNullOrWhiteSpace(element.Designator)
                                                && element.Designator.Length <= 16
                                        );
                                        var physical =
                                            $"deck{cabin.Deck}-cabin{cabinIndex}:{element.Designator}";
                                        var locationKey = (
                                            address,
                                            element.Designator!.ToUpperInvariant()
                                        );
                                        if (
                                            !physicalLocations.TryGetValue(
                                                locationKey,
                                                out var locations
                                            )
                                        )
                                            physicalLocations[locationKey] = locations = new(
                                                StringComparer.Ordinal
                                            );
                                        locations.Add(physical);
                                        var disclosuresKnown =
                                            element.Disclosures is { Length: <= 32 }
                                            && element.Disclosures.All(s =>
                                                !string.IsNullOrWhiteSpace(s)
                                                && s.Length <= 2000
                                                && !s.Any(c => char.IsControl(c) && c != '\n')
                                            );
                                        foreach (var service in element.AvailableServices ?? [])
                                        {
                                            Require(
                                                service is not null
                                                    && Reference(service.Id)
                                                    && service.PassengerId is not null
                                                    && passengers.Contains(service.PassengerId)
                                            );
                                            var priceKnown = TryMoney(
                                                service.TotalAmount,
                                                service.TotalCurrency,
                                                out var price
                                            );
                                            var selectable =
                                                disclosuresKnown
                                                && priceKnown
                                                && price.Currency
                                                    == mapped.Value.TotalAmount.Currency
                                                && cabinClass.Value.Code
                                                    == mapped
                                                        .Value
                                                        .Itinerary
                                                        .Slices[address.Leg]
                                                        .Segments[address.Segment]
                                                        .Cabin
                                                        .Code;
                                            services.Add(
                                                new(
                                                    service.Id!,
                                                    BookingServiceKind.Seat,
                                                    new([service.PassengerId!]),
                                                    new([address]),
                                                    priceKnown ? price : null,
                                                    1,
                                                    selectable,
                                                    selectable ? null : "SeatUnavailable",
                                                    string.IsNullOrWhiteSpace(element.Name)
                                                        ? null
                                                        : element.Name,
                                                    element.Designator,
                                                    physical,
                                                    disclosuresKnown
                                                        ? new(element.Disclosures!.ToArray())
                                                        : null
                                                )
                                            );
                                            keys.Add(service.Id!);
                                        }
                                    }
                                    var kind = element.Type
                                        is "seat"
                                            or "empty"
                                            or "bassinet"
                                            or "exit_row"
                                            or "lavatory"
                                            or "galley"
                                            or "closet"
                                            or "stairs"
                                        ? element.Type!
                                        : "unavailable";
                                    elements.Add(
                                        new(
                                            kind,
                                            element.Type == "seat" ? element.Designator : null,
                                            new(keys.ToArray())
                                        )
                                    );
                                }
                                sections.Add(new(new(elements.ToArray())));
                            }
                            rows.Add(new(new(sections.ToArray())));
                        }
                        cabins.Add(new(cabin.Deck, cabinClass.Value.Code, new(rows.ToArray())));
                    }
                    seatMaps.Add(new(address, new(cabins.ToArray())));
                }
            }
            Require(
                services.Count <= 32768
                    && services.Select(s => s.Key).Distinct(StringComparer.Ordinal).Count()
                        == services.Count
            );
            var finalServices = services
                .Select(service =>
                    service.Kind == BookingServiceKind.Seat
                    && physicalLocations[
                        (service.Segments[0], service.SeatDesignator!.ToUpperInvariant())
                    ].Count > 1
                        ? service with
                        {
                            Selectable = false,
                            Reason = "SeatIdentityUnavailable",
                        }
                        : service
                )
                .ToArray();
            return new AncillaryCatalogFacts(
                mapped.Value,
                new(finalServices),
                new(allowances.ToArray()),
                new(seatMaps.ToArray()),
                seatsRequested,
                seatsRequested && maps is null,
                HasIntent(offer.IntendedServices) || HasIntent(offer.IntendedPaymentMethods)
            );
        }
        catch (Exception error)
            when (error
                    is FormatException
                        or InvalidOperationException
                        or ArgumentException
                        or OverflowException
                        or NullReferenceException
            )
        {
            return Error.Validation(
                "Flights.AncillaryCatalogInvalid",
                "Supplier inventory is invalid."
            );
        }
    }

    public static BaggageLimits Limits(DuffelServiceMetadataDto metadata) =>
        new(
            metadata.MaximumWeightKg,
            metadata.MaximumHeightCm,
            metadata.MaximumDepthCm,
            metadata.MaximumLengthCm
        );

    private static bool ValidLimits(BaggageLimits? limits) =>
        limits is not null
        && limits.MaximumWeightKg is null or >= 0
        && limits.MaximumHeightCm is null or >= 0
        && limits.MaximumDepthCm is null or >= 0
        && limits.MaximumLengthCm is null or >= 0;

    private static bool HasIntent(JsonElement? element) =>
        element is { } value
        && value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)
        && (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() > 0);

    private static void Require([DoesNotReturnIf(false)] bool valid)
    {
        if (!valid)
            throw new FormatException("Invalid inventory shape.");
    }
}
