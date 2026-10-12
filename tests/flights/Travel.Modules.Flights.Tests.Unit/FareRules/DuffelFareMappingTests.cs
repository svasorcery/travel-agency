using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Infrastructure.Providers.Duffel;
using Travel.Modules.Flights.Infrastructure.Providers.Duffel.Dto;
using Xunit;

namespace Travel.Modules.Flights.Tests.Unit.FareRules;

public sealed class DuffelFareMappingTests
{
    internal static readonly DateTimeOffset Now = new(2026, 10, 12, 0, 0, 0, TimeSpan.Zero);

    internal static DuffelOfferDto Offer(DuffelBaggageDto[]? bags = null, string? code = null) =>
        new(
            "off_fictional",
            "100",
            "EUR",
            Now.AddMinutes(20),
            [
                new(
                    [
                        new(
                            new("LED", "Europe/Moscow"),
                            new("SVO", "Europe/Moscow"),
                            "2026-10-13T10:00:00+03:00",
                            "2026-10-13T12:00:00+03:00",
                            new("SU"),
                            "100",
                            [new("economy", "Economy", bags, "pas_one", code)],
                            "seg_one"
                        ),
                    ],
                    "Flex",
                    "sli_one",
                    new(new(true, "0.00", "GBP"), null)
                ),
            ],
            null,
            [new("pas_one", "adult")],
            new(false),
            false,
            []
        );

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Duplicate_root_conditions_are_unknown_without_breaking_offer(bool same)
    {
        var raw = JsonSerializer
            .Serialize(Offer())
            .Replace(
                "\"conditions\":null,",
                "\"conditions\":{\"change_before_departure\":{\"allowed\":true}},\"conditions\":{\"change_before_departure\":{\"allowed\":"
                    + (same ? "true" : "false")
                    + "}},"
            );
        var dto = JsonSerializer.Deserialize<DuffelOfferDto>(raw)!;
        var mapped = DuffelOfferMapper.Map(dto, new FakeTimeProvider(Now));
        mapped.IsError.ShouldBeFalse();
        mapped.Value.FareRuleEvidence!.ChangeBeforeDeparture.Permission.ShouldBe(
            FarePermission.Unknown
        );
        mapped.Value.FareRuleEvidence.SupplierDataInvalid.ShouldBeTrue();
    }

    [Theory]
    [InlineData("total_amount", "[]")]
    [InlineData("expires_at", "false")]
    [InlineData("passengers", "false")]
    [InlineData("slices", "42")]
    [InlineData("available_services", "false")]
    public void A_later_valid_duplicate_does_not_hide_malformed_mandatory_data(
        string property,
        string malformed
    )
    {
        var raw = JsonSerializer.Serialize(Offer());
        raw = raw.Replace(
            "\"" + property + "\":",
            "\"" + property + "\":" + malformed + ",\"" + property + "\":"
        );
        Should.Throw<JsonException>(() => JsonSerializer.Deserialize<DuffelOfferDto>(raw));
    }

    [Fact]
    public void Brand_is_not_a_code_and_root_is_not_derived_from_leg()
    {
        var result = DuffelOfferMapper.Map(Offer(), new FakeTimeProvider(Now));
        result.IsError.ShouldBeFalse();
        result.Value.FareConditions.FareBasisCode.ShouldBeNull();
        var facts = result.Value.FareRuleEvidence.ShouldNotBeNull();
        facts.ChangeBeforeDeparture.Permission.ShouldBe(FarePermission.Unknown);
        facts.Legs[0].ChangeBeforeDeparture.Permission.ShouldBe(FarePermission.Allowed);
        facts.Legs[0].ChangeBeforeDeparture.Penalty!.Amount.ShouldBe(0m);
        facts.Legs[0].ChangeBeforeDeparture.Penalty!.Currency.Value.ShouldBe("GBP");
        facts.ObservedAt.ShouldBe(Now);
        facts.OfferExpiresAt.ShouldBe(Now.AddMinutes(20));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(100)]
    public void Included_allowances_share_nullable_semantics_with_catalog(int? quantity)
    {
        var dto = Offer([new("checked", quantity)], "Y26");
        var result = DuffelOfferMapper.Map(dto, new FakeTimeProvider(Now));
        result.IsError.ShouldBeFalse();
        result
            .Value.FareRuleEvidence.ShouldNotBeNull()
            .Passengers[0]
            .CheckedBaggageQuantity.ShouldBe(quantity);
        result.Value.FareRuleEvidence!.Passengers[0].CarryOnBaggageQuantity.ShouldBeNull();
        result.Value.FareConditions.FareBasisCode.ShouldBe("Y26");
        var catalog = DuffelAncillaryMapper.Map(dto, null, false, new FakeTimeProvider(Now));
        catalog.IsError.ShouldBeFalse();
        catalog.Value.Allowances[0].CheckedQuantity.ShouldBe(quantity);
    }

    [Fact]
    public void An_unassignable_baggage_type_does_not_confirm_an_allowance()
    {
        var mapped = DuffelOfferMapper
            .Map(Offer([new("checked", 1), new(null, 2)]), new FakeTimeProvider(Now))
            .Value;
        mapped.FareRuleEvidence!.Passengers[0].CheckedBaggageQuantity.ShouldBeNull();
    }

    [Fact]
    public void Malformed_included_data_keeps_offer_usable_with_invalid_notice()
    {
        var dto = Offer([new("checked", 1), new("checked", 2), new(null, null, true)]);
        var result = DuffelOfferMapper.Map(dto, new FakeTimeProvider(Now));
        result.IsError.ShouldBeFalse();
        var facts = result.Value.FareRuleEvidence.ShouldNotBeNull();
        facts.SupplierDataInvalid.ShouldBeTrue();
        facts.Passengers[0].CheckedBaggageQuantity.ShouldBeNull();
        DuffelAncillaryMapper
            .Map(dto, null, false, new FakeTimeProvider(Now))
            .IsError.ShouldBeFalse();
    }

    [Fact]
    public void Supplier_segment_order_is_mapped_to_canonical_party_slots_without_generalizing_codes()
    {
        var dto = Offer();
        var segment = dto.Slices[0].Segments[0] with
        {
            Passengers =
            [
                new("economy", "Economy", [new("checked", 2)], "pas_two", "B2"),
                new("economy", "Economy", [new("checked", 0)], "pas_one", "Y26"),
            ],
        };
        dto = dto with
        {
            Passengers = [new("pas_one", "adult"), new("pas_two", "adult")],
            Slices = [dto.Slices[0] with { Segments = [segment] }],
        };
        var mapped = DuffelOfferMapper.Map(dto, new FakeTimeProvider(Now));
        mapped.IsError.ShouldBeFalse();
        mapped.Value.FareConditions.FareBasisCode.ShouldBeNull();
        var facts = mapped.Value.FareRuleEvidence.ShouldNotBeNull();
        facts.Passengers.Single(p => p.Adult == 0).CheckedBaggageQuantity.ShouldBe(0);
        facts.Passengers.Single(p => p.Adult == 0).FareBasisCode.ShouldBe("Y26");
        facts.Passengers.Single(p => p.Adult == 1).CheckedBaggageQuantity.ShouldBe(2);
        facts.Passengers.Single(p => p.Adult == 1).FareBasisCode.ShouldBe("B2");
    }

    [Fact]
    public void Empty_allowance_array_and_invalid_money_stay_unknown()
    {
        var dto = Offer([]) with { Conditions = new(new(true, "-1", "EUR"), null) };
        var mapped = DuffelOfferMapper.Map(dto, new FakeTimeProvider(Now));
        mapped.IsError.ShouldBeFalse();
        var facts = mapped.Value.FareRuleEvidence.ShouldNotBeNull();
        facts.ChangeBeforeDeparture.Permission.ShouldBe(FarePermission.Allowed);
        facts.ChangeBeforeDeparture.Penalty.ShouldBeNull();
        facts.ChangeBeforeDeparture.PenaltyReason.ShouldBe("InvalidSupplierData");
        facts.Passengers[0].CheckedBaggageQuantity.ShouldBeNull();
        facts.Passengers[0].CarryOnBaggageQuantity.ShouldBeNull();
        facts.SupplierDataInvalid.ShouldBeTrue();
    }

    [Fact]
    public void Excessive_optional_labels_are_unavailable_without_rejecting_ordinary_offer()
    {
        var dto = Offer();
        var segment = dto.Slices[0].Segments[0];
        dto = dto with
        {
            Slices =
            [
                dto.Slices[0] with
                {
                    Segments =
                    [
                        segment with
                        {
                            Passengers =
                            [
                                segment.Passengers[0] with
                                {
                                    FareBasisCode = new string('Y', 65),
                                },
                            ],
                        },
                    ],
                },
            ],
        };
        var mapped = DuffelOfferMapper.Map(dto, new FakeTimeProvider(Now));
        mapped.IsError.ShouldBeFalse();
        mapped.Value.FareRuleEvidence.ShouldNotBeNull().SupplierDataInvalid.ShouldBeTrue();
        mapped.Value.FareRuleEvidence!.Passengers[0].FareBasisCode.ShouldBeNull();
    }

    [Fact]
    public void Four_legs_and_nine_adults_retain_all_scoped_pairs()
    {
        var dto = Offer();
        var source = dto.Slices[0].Segments[0];
        var adults = Enumerable
            .Range(0, 9)
            .Select(a => new DuffelOfferPassengerDto($"pas_{a}", "adult"))
            .ToArray();
        dto = dto with
        {
            Passengers = adults,
            Slices = Enumerable
                .Range(0, 4)
                .Select(leg => new DuffelSliceDto(
                    [
                        source with
                        {
                            DepartingAt = $"2026-10-{13 + leg}T10:00:00+03:00",
                            ArrivingAt = $"2026-10-{13 + leg}T12:00:00+03:00",
                            Passengers = adults
                                .Reverse()
                                .Select(
                                    (p, a) =>
                                        new DuffelSegmentPassengerDto(
                                            "economy",
                                            "Economy",
                                            [new("checked", (a + leg) % 3)],
                                            p.Id,
                                            "Y26"
                                        )
                                )
                                .ToArray(),
                        },
                    ],
                    $"Brand {leg}"
                ))
                .ToArray(),
        };
        var mapped = DuffelOfferMapper.Map(dto, new FakeTimeProvider(Now));
        mapped.IsError.ShouldBeFalse();
        var facts = mapped.Value.FareRuleEvidence.ShouldNotBeNull();
        facts.PassengerCount.ShouldBe(9);
        facts.Legs.Count.ShouldBe(4);
        facts.Passengers.Count.ShouldBe(36);
        facts.Passengers.Select(p => (p.Leg, p.Segment, p.Adult)).Distinct().Count().ShouldBe(36);
        mapped.Value.FareConditions.FareBasisCode.ShouldBe("Y26");
        facts.FirstDepartureAt.ShouldBe(mapped.Value.Itinerary.Slices[0].DepartAt);
    }
}
