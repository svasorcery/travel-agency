using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Shouldly;
using Travel.Modules.Flights.Application.Search;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Offer;
using Xunit;

namespace Travel.Modules.Flights.Tests.Unit.Search;

public sealed class SearchJourneyValidationTests
{
    [Theory]
    [InlineData("nullSlices")]
    [InlineData("nullSlice")]
    [InlineData("nullSegments")]
    [InlineData("nullSegment")]
    [InlineData("nullCabin")]
    [InlineData("nullAirport")]
    [InlineData("sliceOrigin")]
    [InlineData("sliceDuration")]
    [InlineData("totalDuration")]
    [InlineData("utcChronology")]
    public void Deserialized_malformed_fresh_route_fails_safely(string corruption)
    {
        var criteria = MultiLegSearchHandlerTests.Criteria();
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            Converters = { new JsonStringEnumConverter() },
        };
        var node = JsonNode.Parse(
            JsonSerializer.Serialize<Offer>(MultiLegSearchHandlerTests.Offer(criteria), options)
        )!;
        var itinerary = node["itinerary"]!;
        var slices = itinerary["slices"]!;
        switch (corruption)
        {
            case "nullSlices":
                itinerary["slices"] = null;
                break;
            case "nullSlice":
                slices[3] = null;
                break;
            case "nullSegments":
                slices[3]!["segments"] = null;
                break;
            case "nullSegment":
                slices[3]!["segments"]![0] = null;
                break;
            case "nullCabin":
                slices[3]!["segments"]![0]!["cabin"] = null;
                break;
            case "nullAirport":
                slices[3]!["segments"]![0]!["origin"] = null;
                break;
            case "sliceOrigin":
                slices[3]!["origin"]!["value"] = "SVO";
                break;
            case "sliceDuration":
                slices[3]!["duration"]!["value"] = "01:00:00";
                break;
            case "totalDuration":
                itinerary["totalDuration"]!["value"] = "01:00:00";
                break;
            case "utcChronology":
                slices[3]!["segments"]![0]!["departAt"] = "2030-06-01T01:00:00+03:00";
                slices[3]!["segments"]![0]!["arriveAt"] = "2030-06-01T03:00:00+03:00";
                break;
        }
        var restored = node.Deserialize<Offer>(options)!;
        SearchJourneyValidation.IsValidOffer(restored).ShouldBeFalse();
        SearchJourneyValidation.Matches(restored, criteria).ShouldBeFalse();
    }

    [Fact]
    public void Party_date_uses_recorded_origin_local_day_even_when_utc_day_differs()
    {
        var criteria = MultiLegSearchHandlerTests.Criteria();
        var offer = MultiLegSearchHandlerTests.Offer(criteria);
        DateOnly
            .FromDateTime(offer.Itinerary.Slices[0].DepartAt.UtcDateTime)
            .ShouldBe(criteria.DepartureDate.AddDays(-1));
        SearchJourneyValidation.Matches(offer, criteria).ShouldBeTrue();
    }

    [Fact]
    public void Legacy_city_bookable_retains_supplier_airports_and_mirrored_geometry()
    {
        var date = new DateOnly(2030, 6, 1);
        var legacy = SearchCriteria
            .Create(
                IataCode.Create("LON").Value,
                IataCode.Create("PAR").Value,
                date,
                date.AddDays(3),
                2,
                CabinClass.Economy,
                CurrencyCode.Create("USD").Value
            )
            .Value;
        var explicitAirports = SearchCriteria
            .CreateMultiLeg(
                [
                    RequestedFlightLeg
                        .Create(IataCode.Create("LHR").Value, IataCode.Create("CDG").Value, date)
                        .Value,
                    RequestedFlightLeg
                        .Create(
                            IataCode.Create("CDG").Value,
                            IataCode.Create("LHR").Value,
                            date.AddDays(3)
                        )
                        .Value,
                ],
                2,
                CabinClass.Economy,
                legacy.Currency
            )
            .Value;
        var offer = MultiLegSearchHandlerTests.Offer(explicitAirports);
        SearchJourneyValidation.Matches(offer, legacy).ShouldBeTrue();
        var exactCities = SearchCriteria
            .CreateMultiLeg(legacy.Legs, 2, CabinClass.Economy, legacy.Currency)
            .Value;
        SearchJourneyValidation.Matches(offer, exactCities).ShouldBeFalse();
    }

    [Fact]
    public void Legacy_round_trip_summary_does_not_invent_missing_inbound_facts()
    {
        var criteria = MultiLegSearchHandlerTests.Criteria(1);
        var book = MultiLegSearchHandlerTests.Offer(criteria);
        var legacy = SearchCriteria
            .Create(
                criteria.Origin,
                criteria.Destination,
                criteria.DepartureDate,
                criteria.DepartureDate.AddDays(3),
                1,
                CabinClass.Economy,
                criteria.Currency
            )
            .Value;
        var summary = new DeeplinkOffer(
            book.Id,
            Itinerary.Create([book.Itinerary.Slices[0]]).Value,
            book.TotalAmount,
            new("travelpayouts"),
            book.FetchedAt,
            new("https://example.invalid"),
            "Fictional partner"
        );
        SearchJourneyValidation.Matches(summary, legacy).ShouldBeTrue();
        SearchJourneyValidation.Matches(summary, criteria).ShouldBeFalse();
        var rank = OfferRanker.Rank(
            [new(summary, summary.TotalAmount, RankingPriceState.Native)],
            criteria.Currency
        );
        rank.Ranking.Entries.Single().DurationSeconds.ShouldBeNull();
        rank.Ranking.Entries.Single().Transfers.ShouldBeNull();
        rank.Ranking.Entries.Single().Limitations.ShouldBe(["partial-itinerary"]);
    }
}
