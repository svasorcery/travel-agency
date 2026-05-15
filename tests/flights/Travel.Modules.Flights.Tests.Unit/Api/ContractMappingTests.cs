using Shouldly;
using Travel.Modules.Flights.Api.Contracts;
using Travel.Modules.Flights.Application.Commands;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Travel.Modules.Flights.Core.ValueObjects.Offer;
using Xunit;

namespace Travel.Modules.Flights.Tests.Unit.Api;

public sealed class ContractMappingTests
{
    // ── helpers ─────────────────────────────────────────────────────────────────

    private static readonly IataCode Led = IataCode.Create("LED").Value;
    private static readonly IataCode Dme = IataCode.Create("DME").Value;
    private static readonly CurrencyCode Rub = CurrencyCode.Create("RUB").Value;

    private static Itinerary BuildItinerary(int sliceCount = 1)
    {
        var seg = Segment
            .Create(
                Led,
                Dme,
                new DateTimeOffset(2026, 7, 15, 9, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 7, 15, 12, 0, 0, TimeSpan.Zero),
                "SU",
                "100",
                CabinClass.Economy
            )
            .Value;

        var slices = Enumerable
            .Range(0, sliceCount)
            .Select(_ => Slice.Create([seg]).Value)
            .ToArray();
        return Itinerary.Create(slices).Value;
    }

    private static BookableOffer BuildBookableOffer() =>
        new(
            Id: OfferId.New(),
            Itinerary: BuildItinerary(),
            TotalAmount: Money.Create(5420m, Rub).Value,
            Provider: ProviderId.Duffel,
            FetchedAt: DateTimeOffset.UtcNow,
            ExpiresAt: DateTimeOffset.UtcNow.AddMinutes(20),
            FareConditions: new FareConditions(false, false, null, null),
            ProviderOfferRef: "off_test_abc123"
        );

    private static DeeplinkOffer BuildDeeplinkOffer() =>
        new(
            Id: OfferId.New(),
            Itinerary: BuildItinerary(),
            TotalAmount: Money.Create(4800m, Rub).Value,
            Provider: ProviderId.Travelpayouts,
            FetchedAt: DateTimeOffset.UtcNow,
            DeeplinkUrl: new Uri("https://tp.travel/xyz"),
            PartnerName: "Travelpayouts"
        );

    // ── OfferDto.From ────────────────────────────────────────────────────────────

    [Fact]
    public void OfferDto_From_BookableOffer_PopulatesProviderOfferRefAndExpiresAt()
    {
        var offer = BuildBookableOffer();
        var dto = OfferDto.From(offer);

        dto.ProviderOfferRef.ShouldBe("off_test_abc123");
        dto.ExpiresAt.ShouldNotBeNull();
        dto.DeeplinkUrl.ShouldBeNull();
        dto.PartnerName.ShouldBeNull();
        dto.TotalAmount.ShouldBe(5420m);
        dto.Currency.ShouldBe("RUB");
        dto.Provider.ShouldBe("duffel");
    }

    [Fact]
    public void OfferDto_From_DeeplinkOffer_PopulatesDeeplinkUrlAndPartnerName()
    {
        var offer = BuildDeeplinkOffer();
        var dto = OfferDto.From(offer);

        dto.DeeplinkUrl.ShouldBe("https://tp.travel/xyz");
        dto.PartnerName.ShouldBe("Travelpayouts");
        dto.ProviderOfferRef.ShouldBeNull();
        dto.ExpiresAt.ShouldBeNull();
        dto.TotalAmount.ShouldBe(4800m);
        dto.Provider.ShouldBe("travelpayouts");
    }

    // ── ItineraryDto.From ────────────────────────────────────────────────────────

    [Fact]
    public void ItineraryDto_From_OneWay_HasOneSlice_IsRoundTripFalse()
    {
        var itinerary = BuildItinerary(sliceCount: 1);
        var dto = ItineraryDto.From(itinerary);

        dto.Slices.Length.ShouldBe(1);
        dto.IsRoundTrip.ShouldBeFalse();
        dto.TotalDuration.ShouldBeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public void ItineraryDto_From_RoundTrip_HasTwoSlices_IsRoundTripTrue()
    {
        var itinerary = BuildItinerary(sliceCount: 2);
        var dto = ItineraryDto.From(itinerary);

        dto.Slices.Length.ShouldBe(2);
        dto.IsRoundTrip.ShouldBeTrue();
    }

    [Fact]
    public void SliceDto_From_RoundTripsSegmentCount()
    {
        var seg = Segment
            .Create(
                Led,
                Dme,
                new DateTimeOffset(2026, 7, 15, 9, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 7, 15, 12, 0, 0, TimeSpan.Zero),
                "SU",
                "100",
                CabinClass.Economy
            )
            .Value;
        var slice = Slice.Create([seg]).Value;
        var dto = SliceDto.From(slice);

        dto.Segments.Length.ShouldBe(1);
        dto.Origin.ShouldBe("LED");
        dto.Destination.ShouldBe("DME");
        dto.Duration.ShouldBeGreaterThan(TimeSpan.Zero);
    }

    // ── QuotedOfferResponse price-change fields ──────────────────────────────────

    [Fact]
    public void QuotedOfferResponse_carries_price_change_fields_on_requote()
    {
        // Arrange: a QuotedOfferResult that represents a successful re-quote where
        // the provider returned a higher price than the cached offer.
        var aggregateId = Guid.NewGuid();
        var offer = BuildBookableOffer();
        var oldMoney = Money.Create(5420m, Rub).Value;
        var newMoney = Money.Create(5999m, Rub).Value;

        var result = new QuotedOfferResult(
            AggregateId: aggregateId,
            Offer: offer,
            PriceChanged: true,
            OldAmount: oldMoney,
            NewAmount: newMoney
        );

        // Act: map QuotedOfferResult → QuotedOfferResponse (the same mapping used by QuoteOfferEndpoint)
        var response = new QuotedOfferResponse(
            AggregateId: result.AggregateId,
            Offer: OfferDto.From(result.Offer),
            PriceChanged: result.PriceChanged,
            OldAmount: result.OldAmount?.Amount,
            OldCurrency: result.OldAmount?.Currency.Value,
            NewAmount: result.NewAmount?.Amount,
            NewCurrency: result.NewAmount?.Currency.Value
        );

        // Assert: all price-change fields are surfaced correctly on the HTTP DTO.
        response.AggregateId.ShouldBe(aggregateId);
        response.PriceChanged.ShouldBeTrue();
        response.OldAmount.ShouldBe(5420m);
        response.OldCurrency.ShouldBe("RUB");
        response.NewAmount.ShouldBe(5999m);
        response.NewCurrency.ShouldBe("RUB");
    }

    [Fact]
    public void QuotedOfferResponse_price_change_fields_are_null_when_price_unchanged()
    {
        var result = new QuotedOfferResult(
            AggregateId: Guid.NewGuid(),
            Offer: BuildBookableOffer(),
            PriceChanged: false,
            OldAmount: null,
            NewAmount: null
        );

        var response = new QuotedOfferResponse(
            AggregateId: result.AggregateId,
            Offer: OfferDto.From(result.Offer),
            PriceChanged: result.PriceChanged,
            OldAmount: result.OldAmount?.Amount,
            OldCurrency: result.OldAmount?.Currency.Value,
            NewAmount: result.NewAmount?.Amount,
            NewCurrency: result.NewAmount?.Currency.Value
        );

        response.PriceChanged.ShouldBeFalse();
        response.OldAmount.ShouldBeNull();
        response.OldCurrency.ShouldBeNull();
        response.NewAmount.ShouldBeNull();
        response.NewCurrency.ShouldBeNull();
    }

    [Fact]
    public void SegmentDto_From_MapsAllFields()
    {
        var depart = new DateTimeOffset(2026, 7, 15, 9, 0, 0, TimeSpan.Zero);
        var arrive = new DateTimeOffset(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);
        var seg = Segment
            .Create(Led, Dme, depart, arrive, "SU", "SU100", CabinClass.Business)
            .Value;
        var dto = SegmentDto.From(seg);

        dto.Origin.ShouldBe("LED");
        dto.Destination.ShouldBe("DME");
        dto.DepartAt.ShouldBe(depart);
        dto.ArriveAt.ShouldBe(arrive);
        dto.CarrierCode.ShouldBe("SU");
        dto.FlightNumber.ShouldBe("SU100");
        dto.CabinClass.ShouldBe("business");
    }
}
