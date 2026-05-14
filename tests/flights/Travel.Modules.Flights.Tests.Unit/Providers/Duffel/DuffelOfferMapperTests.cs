using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Travel.Modules.Flights.Infrastructure.Providers.Duffel;
using Travel.Modules.Flights.Infrastructure.Providers.Duffel.Dto;
using Xunit;

namespace Travel.Modules.Flights.Tests.Unit.Providers.Duffel;

public sealed class DuffelOfferMapperTests
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static DuffelOfferDto LoadFirstOffer(string fixtureName)
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "Providers",
            "Duffel",
            "Fixtures",
            fixtureName
        );
        var json = File.ReadAllText(path);
        var response = JsonSerializer.Deserialize<DuffelOfferRequestResponseDto>(json, JsonOpts)!;
        return response.Data.Offers[0];
    }

    private static FakeTimeProvider MakeTime() =>
        new FakeTimeProvider(new DateTimeOffset(2026, 5, 14, 0, 0, 0, TimeSpan.Zero));

    [Fact]
    public void Map_OneWayFixture_ReturnsSuccessfulOffer()
    {
        var dto = LoadFirstOffer("offer-oneway.json");
        var time = MakeTime();

        var result = DuffelOfferMapper.Map(dto, time);

        result.IsError.ShouldBeFalse();
        var offer = result.Value;

        offer.Itinerary.Slices.Count.ShouldBe(1);
        offer.TotalAmount.Amount.ShouldBe(5420m);
        offer.TotalAmount.Currency.Value.ShouldBe("RUB");
        offer.ProviderOfferRef.ShouldBe("off_0000AEdGUbCLgfECl5nOhp");
        offer.Itinerary.IsOneWay.ShouldBeTrue();
        offer.FareConditions.ChangeAllowed.ShouldBeFalse();
        offer.FareConditions.RefundAllowed.ShouldBeTrue();
        offer.FareConditions.FareBasisCode.ShouldBe("ECONBASIC");
        offer.FareConditions.CabinClassMarketing.ShouldBe("Economy");
    }

    [Fact]
    public void Map_RoundTripFixture_ReturnsIsRoundTripTrue()
    {
        var dto = LoadFirstOffer("offer-roundtrip.json");
        var time = MakeTime();

        var result = DuffelOfferMapper.Map(dto, time);

        result.IsError.ShouldBeFalse();
        result.Value.Itinerary.IsRoundTrip.ShouldBeTrue();
        result.Value.Itinerary.Slices.Count.ShouldBe(2);
    }

    [Fact]
    public void Map_BadIataCode_ReturnsError()
    {
        var dto = new DuffelOfferDto(
            Id: "off_bad",
            TotalAmount: "100.00",
            TotalCurrency: "RUB",
            ExpiresAt: DateTimeOffset.UtcNow.AddDays(1),
            Slices:
            [
                new DuffelSliceDto(
                    Segments:
                    [
                        new DuffelSegmentDto(
                            Origin: new DuffelPlaceDto("XX"), // invalid — 2 chars
                            Destination: new DuffelPlaceDto("DME"),
                            DepartingAt: DateTimeOffset.UtcNow.AddHours(2),
                            ArrivingAt: DateTimeOffset.UtcNow.AddHours(4),
                            MarketingCarrier: new DuffelCarrierDto("SU"),
                            MarketingCarrierFlightNumber: "100",
                            Passengers: [new DuffelSegmentPassengerDto("economy", null)]
                        ),
                    ],
                    FareBrandName: null
                ),
            ],
            Conditions: null
        );

        var result = DuffelOfferMapper.Map(dto, MakeTime());

        result.IsError.ShouldBeTrue();
        result.FirstError.Code.ShouldBe("IataCode.Length");
    }
}
