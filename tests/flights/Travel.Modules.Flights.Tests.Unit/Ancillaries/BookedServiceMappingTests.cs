using Shouldly;
using Travel.Modules.Flights.Application.Booking;
using Travel.Modules.Flights.Core.Booking;
using Travel.Modules.Flights.Core.Providers.Dtos;
using Travel.Modules.Flights.Infrastructure.Providers.Duffel;
using Travel.Modules.Flights.Infrastructure.Providers.Duffel.Dto;

namespace Travel.Modules.Flights.Tests.Unit.Ancillaries;

public sealed class BookedServiceMappingTests
{
    [Fact]
    public void Distinct_order_ids_are_mapped_by_full_graph_and_line_price_is_quantity_inclusive()
    {
        var offerDto = AncillaryCatalogTests.Offer();
        var clock = AncillaryCatalogTests.Clock;
        var offer = DuffelOfferMapper.Map(offerDto, clock).Value;
        var binding = QuoteBindingFactory.Create(offer.Party, 2, null).Value;
        var catalog = DuffelAncillaryMapper.Map(offerDto, null, false, clock).Value;
        var purchase = BookingPurchaseFactory
            .Select(catalog, binding, Guid.NewGuid(), [new(catalog.Services[0].Key, 2)])
            .Value;
        var attempt = Guid.NewGuid();
        var order = Order(offerDto, attempt);
        var mapped = DuffelBookedServicesMapper.Map(
            order,
            offer,
            binding,
            purchase,
            attempt,
            clock
        );
        mapped.IsError.ShouldBeFalse();
        mapped.Value.Matches(purchase).ShouldBeTrue();
        mapped.Value.Services[0].LineTotal.Amount.ShouldBe(20);
        mapped.Value.Services[0].Reference.ShouldBe("ser_booked");
        mapped.Value.Services[0].Segments.Count.ShouldBe(2);
        DuffelBookedServicesMapper
            .Map(order with { Services = [] }, offer, binding, purchase, attempt, clock)
            .Value.Matches(purchase)
            .ShouldBeFalse();
    }

    [Fact]
    public void Foreign_party_wrong_attempt_and_equal_price_changed_route_are_unproven()
    {
        var dto = AncillaryCatalogTests.Offer();
        var clock = AncillaryCatalogTests.Clock;
        var offer = DuffelOfferMapper.Map(dto, clock).Value;
        var binding = QuoteBindingFactory.Create(offer.Party, 2, null).Value;
        var purchase = BookingPurchase.Empty(binding.Revision, offer.TotalAmount, offer.ExpiresAt);
        var attempt = Guid.NewGuid();
        var order = Order(dto, attempt);
        DuffelBookedServicesMapper
            .Map(
                order with
                {
                    Passengers = [new("pas_foreign"), new("pas_2")],
                },
                offer,
                binding,
                purchase,
                attempt,
                clock
            )
            .IsError.ShouldBeTrue();
        DuffelBookedServicesMapper
            .Map(order, offer, binding, purchase, Guid.NewGuid(), clock)
            .IsError.ShouldBeTrue();
        var changed = order
            .Slices![0]
            .Segments.Select(s => s with { MarketingCarrierFlightNumber = "999" })
            .ToArray();
        DuffelBookedServicesMapper
            .Map(
                order with
                {
                    Slices = [order.Slices[0] with { Segments = changed }],
                },
                offer,
                binding,
                purchase,
                attempt,
                clock
            )
            .IsError.ShouldBeTrue();
    }

    internal static DuffelOrderDto Order(DuffelOfferDto dto, Guid attempt) =>
        new(
            "ord_fictional",
            null,
            [],
            null,
            "70.00",
            "GBP",
            new(AncillaryCatalogTests.Clock.GetUtcNow().AddHours(2), true),
            ["cancel"],
            null,
            [
                dto.Slices[0] with
                {
                    Id = "sli_order",
                    Segments = dto.Slices[0]
                        .Segments.Select((s, i) => s with { Id = $"seg_order_{i}" })
                        .ToArray(),
                },
            ],
            [new("pas_1"), new("pas_2")],
            [
                dto.AvailableServices![0] with
                {
                    Id = "ser_booked",
                    SegmentIds = ["seg_order_0", "seg_order_1"],
                    Quantity = 2,
                    TotalAmount = "20.00",
                },
            ],
            new() { ["travel_creation"] = attempt.ToString("N") }
        );
}
