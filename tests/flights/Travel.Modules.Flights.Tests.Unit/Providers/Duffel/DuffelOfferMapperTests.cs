using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Travel.Modules.Flights.Core.ValueObjects;
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

    // =====================================================================
    // Task 4.7 — baggage summary, edge cases, all-slice fare
    // =====================================================================

    private static DuffelOfferDto BuildMinimalOffer(
        string? fareBrandName = null,
        DuffelConditionsDto? conditions = null,
        DuffelBaggageDto[]? baggages = null
    ) =>
        new(
            Id: "off_test",
            TotalAmount: "100.00",
            TotalCurrency: "USD",
            ExpiresAt: DateTimeOffset.UtcNow.AddDays(1),
            Slices:
            [
                new DuffelSliceDto(
                    Segments:
                    [
                        new DuffelSegmentDto(
                            Origin: new DuffelPlaceDto("LED"),
                            Destination: new DuffelPlaceDto("DME"),
                            DepartingAt: DateTimeOffset.UtcNow.AddHours(2),
                            ArrivingAt: DateTimeOffset.UtcNow.AddHours(4),
                            MarketingCarrier: new DuffelCarrierDto("SU"),
                            MarketingCarrierFlightNumber: "100",
                            Passengers:
                            [
                                new DuffelSegmentPassengerDto("economy", "Economy", baggages ?? []),
                            ]
                        ),
                    ],
                    FareBrandName: fareBrandName
                ),
            ],
            Conditions: conditions
        );

    [Fact]
    public void Mapper_maps_baggage_summary()
    {
        var baggages = new DuffelBaggageDto[] { new("checked", 1), new("carry_on", 1) };
        var dto = BuildMinimalOffer(baggages: baggages);

        var result = DuffelOfferMapper.Map(dto, MakeTime());

        result.IsError.ShouldBeFalse();
        var fc = result.Value.FareConditions;
        fc.CheckedBaggageQuantity.ShouldBe(1);
        fc.CarryOnBaggageQuantity.ShouldBe(1);
    }

    [Fact]
    public void Mapper_rejects_unparseable_amount()
    {
        var dto = BuildMinimalOffer() with { TotalAmount = "not_a_number" };

        var result = DuffelOfferMapper.Map(dto, MakeTime());

        result.IsError.ShouldBeTrue();
        result.FirstError.Code.ShouldBe("DuffelOffer.InvalidAmount");
    }

    [Fact]
    public void Mapper_handles_null_conditions()
    {
        var dto = BuildMinimalOffer(conditions: null);

        var result = DuffelOfferMapper.Map(dto, MakeTime());

        result.IsError.ShouldBeFalse();
        result.Value.FareConditions.ChangeAllowed.ShouldBeFalse();
        result.Value.FareConditions.RefundAllowed.ShouldBeFalse();
    }

    [Fact]
    public void Mapper_reads_fare_from_all_slices()
    {
        // Two-slice offer where only the second slice has a fare brand name
        var dto = new DuffelOfferDto(
            Id: "off_twoslice",
            TotalAmount: "200.00",
            TotalCurrency: "USD",
            ExpiresAt: DateTimeOffset.UtcNow.AddDays(1),
            Slices:
            [
                new DuffelSliceDto(
                    Segments:
                    [
                        new DuffelSegmentDto(
                            Origin: new DuffelPlaceDto("LED"),
                            Destination: new DuffelPlaceDto("SVO"),
                            DepartingAt: DateTimeOffset.UtcNow.AddHours(1),
                            ArrivingAt: DateTimeOffset.UtcNow.AddHours(2),
                            MarketingCarrier: new DuffelCarrierDto("SU"),
                            MarketingCarrierFlightNumber: "001",
                            Passengers: [new DuffelSegmentPassengerDto("economy", "Economy", [])]
                        ),
                    ],
                    FareBrandName: null // first slice has no fare brand
                ),
                new DuffelSliceDto(
                    Segments:
                    [
                        new DuffelSegmentDto(
                            Origin: new DuffelPlaceDto("SVO"),
                            Destination: new DuffelPlaceDto("JFK"),
                            DepartingAt: DateTimeOffset.UtcNow.AddHours(4),
                            ArrivingAt: DateTimeOffset.UtcNow.AddHours(14),
                            MarketingCarrier: new DuffelCarrierDto("SU"),
                            MarketingCarrierFlightNumber: "101",
                            Passengers: [new DuffelSegmentPassengerDto("economy", "Economy", [])]
                        ),
                    ],
                    FareBrandName: "BIZFLEX" // second slice has a fare brand name
                ),
            ],
            Conditions: null
        );

        var result = DuffelOfferMapper.Map(dto, MakeTime());

        result.IsError.ShouldBeFalse();
        // Should pick the first non-null fare brand across all slices
        result.Value.FareConditions.FareBasisCode.ShouldBe("BIZFLEX");
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
                            Passengers: [new DuffelSegmentPassengerDto("economy", null, [])]
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
