using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Infrastructure.Providers.Duffel;
using Xunit;

namespace Travel.Modules.Flights.Tests.Unit.Providers.Duffel;

public sealed class DuffelMultiLegSearchTests
{
    private static readonly (
        string Origin,
        string Destination,
        string OriginZone,
        string DestinationZone,
        string Departure,
        string Arrival
    )[] Route =
    [
        (
            "LHR",
            "JFK",
            "Europe/London",
            "America/New_York",
            "2030-06-01T10:00:00",
            "2030-06-01T12:00:00"
        ),
        (
            "BOS",
            "LHR",
            "America/New_York",
            "Europe/London",
            "2030-06-04T10:00:00",
            "2030-06-04T22:00:00"
        ),
        (
            "LHR",
            "CDG",
            "Europe/London",
            "Europe/Paris",
            "2030-06-06T10:00:00",
            "2030-06-06T12:30:00"
        ),
        (
            "CDG",
            "LHR",
            "Europe/Paris",
            "Europe/London",
            "2030-06-09T10:00:00",
            "2030-06-09T10:30:00"
        ),
    ];

    private static SearchCriteria Criteria() =>
        SearchCriteria
            .CreateMultiLeg(
                Route
                    .Select(r =>
                        RequestedFlightLeg
                            .Create(
                                IataCode.Create(r.Origin).Value,
                                IataCode.Create(r.Destination).Value,
                                DateOnly.Parse(r.Departure[..10])
                            )
                            .Value
                    )
                    .ToArray(),
                2,
                CabinClass.Economy,
                CurrencyCode.Create("USD").Value
            )
            .Value;

    private static JsonObject Segment(
        string origin,
        string destination,
        string originZone,
        string destinationZone,
        string departure,
        string arrival
    ) =>
        new()
        {
            ["origin"] = new JsonObject { ["iata_code"] = origin, ["time_zone"] = originZone },
            ["destination"] = new JsonObject
            {
                ["iata_code"] = destination,
                ["time_zone"] = destinationZone,
            },
            ["departing_at"] = departure,
            ["arriving_at"] = arrival,
            ["marketing_carrier"] = new JsonObject { ["iata_code"] = "ZZ" },
            ["marketing_carrier_flight_number"] = "101",
            ["passengers"] = new JsonArray(
                new JsonObject { ["passenger_id"] = "pas_1", ["cabin_class"] = "economy" },
                new JsonObject { ["passenger_id"] = "pas_2", ["cabin_class"] = "economy" }
            ),
        };

    private static JsonObject Offer() =>
        new()
        {
            ["id"] = "off_fictional",
            ["total_amount"] = "500.00",
            ["total_currency"] = "USD",
            ["expires_at"] = "2030-05-01T12:00:00Z",
            ["passengers"] = new JsonArray(
                new JsonObject { ["id"] = "pas_1", ["type"] = "adult" },
                new JsonObject { ["id"] = "pas_2", ["type"] = "adult" }
            ),
            ["slices"] = new JsonArray(
                Route
                    .Select(r =>
                        (JsonNode)
                            new JsonObject
                            {
                                ["segments"] = new JsonArray(
                                    Segment(
                                        r.Origin,
                                        r.Destination,
                                        r.OriginZone,
                                        r.DestinationZone,
                                        r.Departure,
                                        r.Arrival
                                    )
                                ),
                            }
                    )
                    .ToArray()
            ),
        };

    private sealed class Transport(string response) : HttpMessageHandler
    {
        public string? Body;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage req,
            CancellationToken ct
        )
        {
            Body = req.Content is null ? null : await req.Content.ReadAsStringAsync(ct);
            return new(HttpStatusCode.OK)
            {
                Content = new StringContent(response, Encoding.UTF8, "application/json"),
            };
        }
    }

    private static DuffelFlightSearchProvider Provider(HttpClient http)
    {
        http.BaseAddress = new Uri("https://example.invalid");
        var opts = Options.Create(new DuffelOptions { ApiKey = "fictional" });
        return new(
            new(http, opts),
            opts,
            new FakeTimeProvider(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero)),
            NullLogger<DuffelFlightSearchProvider>.Instance
        );
    }

    private static string Response(params JsonObject[] offers) =>
        new JsonObject
        {
            ["data"] = new JsonObject
            {
                ["offers"] = new JsonArray(offers.Select(o => (JsonNode)o).ToArray()),
            },
        }.ToJsonString();

    [Fact]
    public async Task Refreshed_quote_uses_the_same_complete_airport_time_mapping()
    {
        var wire = new JsonObject { ["data"] = Offer() }.ToJsonString();
        using var http = new HttpClient(new Transport(wire))
        {
            BaseAddress = new Uri("https://example.invalid"),
        };
        var opts = Options.Create(new DuffelOptions { ApiKey = "fictional" });
        var booking = new DuffelFlightBookingProvider(
            new(http, opts),
            new FakeTimeProvider(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero)),
            NullLogger<DuffelFlightBookingProvider>.Instance
        );
        var result = await booking.RefreshOfferAsync(
            "off_fictional",
            TestContext.Current.CancellationToken
        );
        result.IsError.ShouldBeFalse();
        result.Value.Itinerary.Slices.Count.ShouldBe(4);
        result.Value.Itinerary.Slices[0].DepartAt.Offset.ShouldBe(TimeSpan.FromHours(1));
        result.Value.Itinerary.Slices[0].ArriveAt.Offset.ShouldBe(TimeSpan.FromHours(-4));
        result.Value.Party!.FirstDepartureLocalDate.ShouldBe(new DateOnly(2030, 6, 1));
    }

    [Fact]
    public async Task Valid_whole_party_with_a_different_requested_count_is_unavailable()
    {
        var c = Criteria();
        var singleton = SearchCriteria.CreateMultiLeg(c.Legs, 1, c.CabinClass, c.Currency).Value;
        using var http = new HttpClient(new Transport(Response(Offer())));
        var result = await Provider(http)
            .SearchAsync(singleton, TestContext.Current.CancellationToken);
        result.IsError.ShouldBeTrue();
        result.FirstError.Code.ShouldBe("Flights.ProviderUnavailable");
    }

    [Fact]
    public async Task Sends_all_four_requested_legs_and_maps_the_whole_offer()
    {
        var transport = new Transport(Response(Offer()));
        using var http = new HttpClient(transport);
        var result = await Provider(http)
            .SearchAsync(Criteria(), TestContext.Current.CancellationToken);
        using var body = JsonDocument.Parse(transport.Body!);
        var data = body.RootElement.GetProperty("data");
        var slices = data.GetProperty("slices");
        slices.GetArrayLength().ShouldBe(4);
        for (var i = 0; i < 4; i++)
        {
            slices[i].GetProperty("origin").GetString().ShouldBe(Route[i].Origin);
            slices[i].GetProperty("destination").GetString().ShouldBe(Route[i].Destination);
            slices[i].GetProperty("departure_date").GetString().ShouldBe(Route[i].Departure[..10]);
        }
        data.GetProperty("passengers").GetArrayLength().ShouldBe(2);
        result.IsError.ShouldBeFalse();
        result.Value.Single().Itinerary.Slices.Count.ShouldBe(4);
    }

    [Theory]
    [InlineData("airport")]
    [InlineData("date")]
    [InlineData("slice-count")]
    [InlineData("adult-count")]
    [InlineData("member")]
    [InlineData("cabin")]
    [InlineData("time")]
    [InlineData("null-baggage")]
    public async Task Invalid_nonempty_inventory_fails_the_entire_provider(string fault)
    {
        var bad = Offer();
        var slices = bad["slices"]!.AsArray();
        var segment = slices[3]!["segments"]![0]!;
        switch (fault)
        {
            case "airport":
                segment["destination"]!["iata_code"] = "LGW";
                break;
            case "date":
                segment["departing_at"] = "2030-06-10T10:00:00";
                segment["arriving_at"] = "2030-06-10T10:30:00";
                break;
            case "slice-count":
                slices.RemoveAt(3);
                break;
            case "adult-count":
                bad["passengers"]!.AsArray().RemoveAt(1);
                break;
            case "member":
                segment["passengers"]![0]!["passenger_id"] = "foreign";
                break;
            case "cabin":
                segment["passengers"]![0]!["cabin_class"] = "business";
                segment["passengers"]![1]!["cabin_class"] = "business";
                break;
            case "time":
                segment["departing_at"] = "2030-06-08T10:00:00";
                break;
            case "null-baggage":
                segment["passengers"]![0]!["baggages"] = new JsonArray((JsonNode?)null);
                break;
        }
        using var http = new HttpClient(new Transport(Response(Offer(), bad)));
        var result = await Provider(http)
            .SearchAsync(Criteria(), TestContext.Current.CancellationToken);
        result.IsError.ShouldBeTrue();
        result.FirstError.Code.ShouldBe("Flights.ProviderUnavailable");
        result.FirstError.Description.ShouldNotContain("off_fictional");
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("null")]
    [InlineData("{\"data\":null}")]
    [InlineData("{\"data\":{\"offers\":null}}")]
    [InlineData("{\"data\":{\"offers\":[null]}}")]
    public async Task Malformed_supplier_inventory_returns_a_fixed_safe_failure(string response)
    {
        using var http = new HttpClient(new Transport(response));
        var result = await Provider(http)
            .SearchAsync(Criteria(), TestContext.Current.CancellationToken);
        result.IsError.ShouldBeTrue();
        result.FirstError.Code.ShouldBe("Flights.ProviderUnavailable");
    }

    [Fact]
    public async Task Genuine_empty_inventory_is_successful_empty()
    {
        using var http = new HttpClient(new Transport(Response()));
        var result = await Provider(http)
            .SearchAsync(Criteria(), TestContext.Current.CancellationToken);
        result.IsError.ShouldBeFalse();
        result.Value.ShouldBeEmpty();
    }

    [Fact]
    public async Task Connection_segments_do_not_expand_the_four_requested_legs()
    {
        var offer = Offer();
        offer["slices"]![0]!["segments"] = new JsonArray(
            Segment(
                "LHR",
                "CDG",
                "Europe/London",
                "Europe/Paris",
                "2030-06-01T08:00:00",
                "2030-06-01T10:30:00"
            ),
            Segment(
                "CDG",
                "JFK",
                "Europe/Paris",
                "America/New_York",
                "2030-06-01T12:00:00",
                "2030-06-01T14:00:00"
            )
        );
        var transport = new Transport(Response(offer));
        using var http = new HttpClient(transport);
        var result = await Provider(http)
            .SearchAsync(Criteria(), TestContext.Current.CancellationToken);
        result.IsError.ShouldBeFalse();
        result.Value.Single().Itinerary.Slices[0].Segments.Count.ShouldBe(2);
        using var body = JsonDocument.Parse(transport.Body!);
        body.RootElement.GetProperty("data").GetProperty("slices").GetArrayLength().ShouldBe(4);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Legacy_city_request_preserves_provider_airport_provenance(bool roundTrip)
    {
        var offer = Offer();
        var slices = offer["slices"]!.AsArray();
        while (slices.Count > (roundTrip ? 2 : 1))
            slices.RemoveAt(slices.Count - 1);
        if (roundTrip)
            slices[1]!["segments"]![0]!["origin"]!["iata_code"] = "JFK";
        var criteria = SearchCriteria
            .Create(
                IataCode.Create("LON").Value,
                IataCode.Create("JFK").Value,
                new DateOnly(2030, 6, 1),
                roundTrip ? new DateOnly(2030, 6, 4) : null,
                2,
                CabinClass.Economy,
                CurrencyCode.Create("USD").Value
            )
            .Value;
        using var http = new HttpClient(new Transport(Response(offer)));
        var result = await Provider(http)
            .SearchAsync(criteria, TestContext.Current.CancellationToken);
        result.IsError.ShouldBeFalse();
        result.Value.Single().Itinerary.Slices[0].Origin.Value.ShouldBe("LHR");
    }

    [Fact]
    public async Task Legacy_return_rejects_open_jaw_geometry()
    {
        var offer = Offer();
        var slices = offer["slices"]!.AsArray();
        while (slices.Count > 2)
            slices.RemoveAt(slices.Count - 1);
        var criteria = SearchCriteria
            .Create(
                IataCode.Create("LON").Value,
                IataCode.Create("JFK").Value,
                new DateOnly(2030, 6, 1),
                new DateOnly(2030, 6, 4),
                2,
                CabinClass.Economy,
                CurrencyCode.Create("USD").Value
            )
            .Value;
        using var http = new HttpClient(new Transport(Response(offer)));
        (
            await Provider(http).SearchAsync(criteria, TestContext.Current.CancellationToken)
        ).IsError.ShouldBeTrue();
    }
}
