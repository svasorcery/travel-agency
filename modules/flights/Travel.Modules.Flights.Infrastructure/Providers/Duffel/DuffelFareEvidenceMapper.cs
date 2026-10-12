using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Infrastructure.Providers.Duffel.Dto;

namespace Travel.Modules.Flights.Infrastructure.Providers.Duffel;

/// <summary>Informational facts only. Invalid evidence never invalidates a purchasable offer.</summary>
public static class DuffelFareEvidenceMapper
{
    public static int? KnownAllowance(DuffelBaggageDto[]? baggage, string type)
    {
        if (
            baggage is null
            || baggage.Length > 128
            || baggage.Any(b => b is null || b.Type is null)
        )
            return null;
        var entries = baggage.Where(b => b is not null && b.Type == type).ToArray();
        return entries.Length == 1 && !entries[0].InvalidData && entries[0].Quantity is >= 0
            ? entries[0].Quantity
            : null;
    }

    public static FareRuleEvidence? Map(
        DuffelOfferDto dto,
        Itinerary itinerary,
        DateTimeOffset observedAt
    )
    {
        var segmentCount = dto.Slices.Sum(s => (long)s.Segments.Length);
        if (
            dto.Slices.Length > 4
            || segmentCount > 64
            || dto.Passengers is not { Length: >= 1 and <= 9 }
        )
            return null;
        var slots = dto
            .Passengers.Select((p, index) => (p.Id, index))
            .ToDictionary(x => x.Id, x => x.index, StringComparer.Ordinal);
        var legs = new List<FareLegFact>();
        var facts = new List<FareSegmentFact>();
        var invalid = dto.Conditions?.InvalidData == true;
        var change = Condition(dto.Conditions?.ChangeBeforeDeparture, ref invalid);
        var refund = Condition(dto.Conditions?.RefundBeforeDeparture, ref invalid);
        for (var leg = 0; leg < dto.Slices.Length; leg++)
        {
            var slice = dto.Slices[leg];
            invalid |= slice.InvalidData || slice.Conditions?.InvalidData == true;
            var brand = Label(slice.FareBrandName, 256, ref invalid);
            legs.Add(
                new(leg, brand, Condition(slice.Conditions?.ChangeBeforeDeparture, ref invalid))
            );
            for (var segment = 0; segment < slice.Segments.Length; segment++)
                foreach (var passenger in slice.Segments[segment].Passengers)
                {
                    invalid |= passenger.InvalidData || InvalidBaggage(passenger.Baggages);
                    facts.Add(
                        new(
                            leg,
                            segment,
                            slots[passenger.PassengerId!],
                            Label(passenger.FareBasisCode, 64, ref invalid),
                            Label(passenger.CabinClassMarketingName, 256, ref invalid),
                            KnownAllowance(passenger.Baggages, "checked"),
                            KnownAllowance(passenger.Baggages, "carry_on")
                        )
                    );
                }
        }
        var created = FareRuleEvidence.Create(
            observedAt,
            dto.ExpiresAt,
            itinerary.Slices.SelectMany(s => s.Segments).Min(s => s.DepartAt),
            dto.Passengers.Length,
            dto.Slices.Select(s => s.Segments.Length),
            change,
            refund,
            legs,
            facts,
            invalid
        );
        return created.IsError ? null : created.Value;
    }

    private static bool InvalidBaggage(DuffelBaggageDto[]? baggage) =>
        baggage is not null
        && (
            baggage.Length > 128
            || baggage.Any(b =>
                b is null
                || b.InvalidData
                || (b.Type is not null && b.Type is not ("checked" or "carry_on"))
                || b.Quantity is < 0
            )
            || baggage.Where(b => b?.Type is not null).GroupBy(b => b.Type).Any(g => g.Count() > 1)
        );

    private static string? Label(string? value, int maximum, ref bool invalid)
    {
        if (value is null)
            return null;
        if (
            !string.IsNullOrWhiteSpace(value)
            && value.Length <= maximum
            && !value.Any(c =>
                char.IsControl(c)
                || char.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.Format
            )
        )
            return value;
        invalid = true;
        return null;
    }

    private static FareCondition Condition(DuffelConditionDto? dto, ref bool invalid)
    {
        if (dto is null)
            return FareCondition.NotProvided;
        invalid |= dto.InvalidData;
        var permission =
            dto.Allowed is true ? FarePermission.Allowed
            : dto.Allowed is false ? FarePermission.NotAllowed
            : FarePermission.Unknown;
        var known = DuffelAncillaryMapper.TryMoney(
            dto.PenaltyAmount,
            dto.PenaltyCurrency,
            out var money
        );
        var invalidMoney =
            (
                dto.PenaltyAmount is not null
                && !DuffelAncillaryMapper.TryMoney(dto.PenaltyAmount, "EUR", out _)
            )
            || (
                dto.PenaltyCurrency is not null
                && !DuffelAncillaryMapper.TryMoney("0", dto.PenaltyCurrency, out _)
            );
        invalid |= invalidMoney;
        return FareCondition
            .Create(
                permission,
                known ? money : null,
                permission == FarePermission.Unknown
                    ? dto.InvalidData
                        ? "InvalidSupplierData"
                        : "NotProvided"
                    : "Known",
                known ? "Known"
                    : invalidMoney || dto.InvalidData ? "InvalidSupplierData"
                    : "NotProvided"
            )
            .Value;
    }
}
