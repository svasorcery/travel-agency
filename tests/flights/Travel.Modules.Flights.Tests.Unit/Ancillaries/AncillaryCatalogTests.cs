using System.Text.Json;
using System.Text.Json.Nodes;
using Shouldly;
using Travel.Modules.Flights.Application.Booking;
using Travel.Modules.Flights.Core.Booking;
using Travel.Modules.Flights.Core.Providers.Dtos;
using Travel.Modules.Flights.Infrastructure.Providers.Duffel;
using Travel.Modules.Flights.Infrastructure.Providers.Duffel.Dto;

namespace Travel.Modules.Flights.Tests.Unit.Ancillaries;

public sealed class AncillaryCatalogTests
{
    [Fact]
    public void Final_purchase_counts_multisegment_bag_once_and_binds_local_passenger()
    {
        var catalog = DuffelAncillaryMapper.Map(Offer(), null, false, Clock).Value;
        var binding = QuoteBindingFactory.Create(catalog.Offer.Party, 2, null).Value;
        var purchase = BookingPurchaseFactory.Select(
            catalog,
            binding,
            Guid.NewGuid(),
            [new AncillarySelection(catalog.Services[0].Key, 2)]
        );
        purchase.IsError.ShouldBeFalse();
        purchase.Value.Total.Amount.ShouldBe(70);
        purchase.Value.Extras.Amount.ShouldBe(20);
        purchase.Value.Services.Single().PassengerId.ShouldBe(binding.Slots[0].Id.Value);
        purchase.Value.Services.Single().Segments.Count.ShouldBe(2);
    }

    [Fact]
    public void Empty_purchase_is_compatible_but_anonymous_services_and_pricing_intent_are_rejected()
    {
        var catalog = DuffelAncillaryMapper.Map(Offer(), null, false, Clock).Value;
        var binding = QuoteBindingFactory.Create(catalog.Offer.Party, 2, null).Value;
        BookingPurchaseFactory.Select(catalog, binding, null, []).IsError.ShouldBeFalse();
        BookingPurchaseFactory
            .Select(catalog, binding, null, [new(catalog.Services[0].Key, 1)])
            .IsError.ShouldBeTrue();
        BookingPurchaseFactory
            .Select(
                catalog with
                {
                    UnsupportedPricing = true,
                },
                binding,
                Guid.NewGuid(),
                [new(catalog.Services[0].Key, 1)]
            )
            .IsError.ShouldBeTrue();
        BookingPurchaseFactory
            .Select(catalog, binding, Guid.NewGuid(), [new(catalog.Services[0].Key, 3)])
            .IsError.ShouldBeTrue();
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Unsupported_intended_pricing_cannot_be_cleared_by_empty_selection()
    {
        var catalog = DuffelAncillaryMapper.Map(Offer(), null, false, Clock).Value;
        var binding = QuoteBindingFactory.Create(catalog.Offer.Party, 2, null).Value;
        BookingPurchaseFactory
            .Select(catalog with { UnsupportedPricing = true }, binding, null, [])
            .IsError.ShouldBeTrue();
    }

    internal static DuffelOfferDto Offer() =>
        JsonSerializer
            .Deserialize<DuffelOfferResponseDto>(
                File.ReadAllText(
                    Path.Combine(AppContext.BaseDirectory, "flights-ancillaries.json")
                ),
                Json
            )!
            .Data;

    internal static readonly TimeProvider Clock = new FixedClock();

    [Fact]
    public void Baggage_scope_and_missing_included_allowance_are_distinct()
    {
        var mapped = DuffelAncillaryMapper.Map(Offer(), null, false, Clock);
        mapped.IsError.ShouldBeFalse();
        var bag = mapped.Value.Services.Single();
        bag.Kind.ShouldBe(BookingServiceKind.CheckedBaggage);
        bag.UnitPrice!.Amount.ShouldBe(10);
        bag.MaximumQuantity.ShouldBe(2);
        bag.Segments.Count.ShouldBe(2);
        bag.Baggage!.MaximumDepthCm.ShouldBe(40);
        mapped
            .Value.Allowances.Single(a => a.PassengerReference == "pas_1" && a.Segment.Segment == 0)
            .CheckedQuantity.ShouldBe(1);
        mapped
            .Value.Allowances.Single(a => a.PassengerReference == "pas_1" && a.Segment.Segment == 1)
            .CheckedQuantity.ShouldBeNull();
    }

    [Theory]
    [InlineData("Extra legroom")]
    [InlineData("")]
    public void Seat_services_keep_passenger_specific_prices_and_irregular_layout(string name)
    {
        var maps = new[]
        {
            new DuffelSeatMapDto(
                "map_fictional",
                "sli_quote",
                "seg_quote_1",
                [
                    new(
                        0,
                        "economy",
                        [
                            new([
                                new([
                                    new(
                                        "seat",
                                        "12A",
                                        ["Exit row restrictions apply"],
                                        name,
                                        [
                                            new("ase_seat_p1", "pas_1", "3.50", "GBP"),
                                            new("ase_seat_p2", "pas_2", "0.00", "GBP"),
                                        ]
                                    ),
                                    new("empty"),
                                    new("seat", "12C", [], null, []),
                                ]),
                            ]),
                        ]
                    ),
                ]
            ),
        };
        var mapped = DuffelAncillaryMapper.Map(Offer(), maps, true, Clock);
        mapped.IsError.ShouldBeFalse();
        var seats = mapped.Value.Services.Where(s => s.Kind == BookingServiceKind.Seat).ToArray();
        seats.Length.ShouldBe(2);
        seats[0].UnitPrice!.Amount.ShouldBe(3.5m);
        seats[1].UnitPrice!.Amount.ShouldBe(0);
        seats[0].PhysicalSeat.ShouldBe(seats[1].PhysicalSeat);
        seats[0].Name.ShouldBe(string.IsNullOrWhiteSpace(name) ? null : name);
        mapped.Value.SeatMaps.Single().Cabins[0].Rows[0].Sections[0].Elements.Count.ShouldBe(3);
        mapped.Value.SeatMaps.Count.ShouldBe(1);
    }

    [Fact]
    public void Foreign_segment_and_malformed_inventory_do_not_become_empty_success()
    {
        var offer = Offer();
        var invalid = offer with
        {
            AvailableServices = [offer.AvailableServices![0] with { SegmentIds = ["seg_foreign"] }],
        };
        DuffelAncillaryMapper.Map(invalid, null, false, Clock).IsError.ShouldBeTrue();
        DuffelAncillaryMapper
            .Map(offer with { AvailableServices = null }, null, false, Clock)
            .IsError.ShouldBeTrue();
        DuffelAncillaryMapper
            .Map(offer with { AvailableServices = [] }, null, false, Clock)
            .IsError.ShouldBeFalse();
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2030, 1, 1, 10, 0, 0, TimeSpan.Zero);
    }
}
