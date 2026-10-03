using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ErrorOr;
using Shouldly;
using Travel.Modules.Flights.Api.Contracts;
using Travel.Modules.Flights.Application.Commands;
using Travel.Modules.Flights.Application.Queries;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Travel.Modules.Flights.Core.ValueObjects.Offer;
using Xunit;

namespace Travel.Host.Tests.Integration.Flights;

// The fixture uses only TestServer and a fake bus: no providers, persistence or initializer.
[Collection(HostIntegrationCollection.Name)]
public sealed class MultiLegSearchHttpTests : IClassFixture<FlightsApiFixture>
{
    private readonly FlightsApiFixture fixture;
    private const string Leg =
        "{\"origin\":\"LED\",\"destination\":\"DME\",\"departureDate\":\"2030-06-10\"}";

    public MultiLegSearchHttpTests(FlightsApiFixture fixture)
    {
        this.fixture = fixture;
        fixture.Bus.Reset();
        fixture.Logs.Messages.Clear();
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(4, 9)]
    public async Task Anonymous_v2_dispatches_exact_ordered_airports_and_existing_options(
        int count,
        int passengers
    )
    {
        SearchCriteria? captured = null;
        fixture.Bus.OnCapture<SearchFlightsQuery>(query =>
        {
            captured = query.Criteria;
            return (ErrorOr<SearchResult>)new SearchResult([], []);
        });
        var airports = new[] { "LED", "DME", "LHR", "CDG", "FRA" };
        var legs = Enumerable
            .Range(0, count)
            .Select(i => new
            {
                origin = airports[i],
                destination = airports[i + 1],
                departureDate = new DateOnly(2030, 6, 10 + i),
            })
            .ToArray();
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/flights/search/v2?currency=EUR"
        )
        {
            Content = JsonContent.Create(
                new
                {
                    legs,
                    passengerCount = passengers,
                    cabinClass = "business",
                }
            ),
        };
        request.Headers.AcceptLanguage.ParseAdd("en-US,en;q=0.8");
        using var response = await fixture.Client.SendAsync(
            request,
            TestContext.Current.CancellationToken
        );
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        captured.ShouldNotBeNull();
        captured.RouteMode.ShouldBe(SearchRouteMode.ExplicitAirportLegs);
        captured.Legs.Select(l => l.Origin.Value).ShouldBe(airports.Take(count));
        captured.Legs.Select(l => l.Destination.Value).ShouldBe(airports.Skip(1).Take(count));
        captured.Legs.Select(l => l.DepartureDate).ShouldBe(legs.Select(l => l.departureDate));
        captured.PassengerCount.ShouldBe(passengers);
        captured.CabinClass.Code.ShouldBe("business");
        captured.Currency.Value.ShouldBe("EUR");
        captured.Locale.ShouldBe("en");
        captured.JourneyKind.ShouldBe(count == 1 ? JourneyKind.OneWay : JourneyKind.MultiLeg);
        fixture.Bus.InvocationCount.ShouldBe(1);
    }

    [Fact]
    public async Task V2_uses_existing_currency_locale_cabin_and_party_defaults()
    {
        fixture.Bus.OnCapture<SearchFlightsQuery>(query =>
        {
            query.Criteria.Currency.Value.ShouldBe("RUB");
            query.Criteria.Locale.ShouldBe("ru");
            query.Criteria.CabinClass.Code.ShouldBe("economy");
            query.Criteria.PassengerCount.ShouldBe(1);
            return (ErrorOr<SearchResult>)new SearchResult([], []);
        });
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/flights/search/v2")
        {
            Content = new StringContent($"{{\"legs\":[{Leg}]}}", Encoding.UTF8, "application/json"),
        };
        request.Headers.AcceptLanguage.ParseAdd("de-DE");
        using var response = await fixture.Client.SendAsync(
            request,
            TestContext.Current.CancellationToken
        );
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("null", null)]
    [InlineData("{}", null)]
    [InlineData("{\"legs\":null}", null)]
    [InlineData("{\"legs\":[]}", null)]
    [InlineData("{\"legs\":[null]}", 0)]
    [InlineData("{\"legs\":[{}]}", 0)]
    [InlineData(
        "{\"legs\":[{\"origin\":null,\"destination\":\"DME\",\"departureDate\":\"2030-06-10\"}]}",
        0
    )]
    [InlineData(
        "{\"legs\":[{\"origin\":\"private-secret\",\"destination\":\"DME\",\"departureDate\":\"2030-06-10\"}]}",
        0
    )]
    [InlineData(
        "{\"legs\":[{\"origin\":\"LED\",\"destination\":\"LED\",\"departureDate\":\"2030-06-10\"}]}",
        0
    )]
    [InlineData(
        "{\"legs\":[{\"origin\":\"LED\",\"destination\":\"DME\",\"departureDate\":\"2030-02-30\"}]}",
        0
    )]
    [InlineData(
        "{\"legs\":[{\"origin\":\"LED\",\"destination\":\"DME\",\"departureDate\":\"0001-01-01\"}]}",
        0
    )]
    [InlineData(
        "{\"legs\":[{\"origin\":\"LED\",\"Origin\":\"LHR\",\"destination\":\"DME\",\"departureDate\":\"2030-06-10\"}]}",
        0
    )]
    [InlineData(
        "{\"legs\":[{\"origin\":\"LED\",\"destination\":\"DME\",\"departureDate\":\"2030-06-10\",\"private-secret\":true}]}",
        0
    )]
    [InlineData("{\"legs\":[" + Leg + "],\"private-secret\":true}", null)]
    [InlineData("{\"legs\":[" + Leg + "],\"Legs\":[" + Leg + "]}", null)]
    [InlineData("{\"legs\":[" + Leg + "],\"passengerCount\":1,\"PassengerCount\":2}", null)]
    [InlineData(
        "{\"legs\":[" + Leg + "],\"cabinClass\":\"economy\",\"CabinClass\":\"first\"}",
        null
    )]
    [InlineData("{private-secret", null)]
    public async Task Unsafe_inputs_have_fixed_problem_and_never_dispatch(string body, int? index)
    {
        await AssertRejected(body, HttpStatusCode.BadRequest, index);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("10")]
    [InlineData("1.5")]
    [InlineData("null")]
    [InlineData("\"private-secret\"")]
    public async Task Invalid_counts_are_rejected_before_dispatch(string count) =>
        await AssertRejected(
            $"{{\"legs\":[{Leg}],\"passengerCount\":{count}}}",
            HttpStatusCode.BadRequest
        );

    [Theory]
    [InlineData("null")]
    [InlineData("true")]
    [InlineData("\"private-secret\"")]
    public async Task Invalid_cabins_are_rejected_before_dispatch(string cabin) =>
        await AssertRejected(
            $"{{\"legs\":[{Leg}],\"cabinClass\":{cabin}}}",
            HttpStatusCode.BadRequest
        );

    [Fact]
    public async Task Five_legs_are_rejected_before_dispatch() =>
        await AssertRejected(
            $"{{\"legs\":[{string.Join(',', Enumerable.Repeat(Leg, 5))}]}}",
            HttpStatusCode.BadRequest
        );

    [Fact]
    public async Task Decreasing_dates_have_zero_based_second_leg_index() =>
        await AssertRejected(
            $"{{\"legs\":[{Leg},{Leg.Replace("2030-06-10", "2030-06-09")}]}}",
            HttpStatusCode.BadRequest,
            1
        );

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Oversized_body_is_rejected_with_or_without_content_length(bool chunked)
    {
        var body = "{\"private-secret\":\"" + new string('x', 17000) + "\"}";
        await AssertRejected(body, HttpStatusCode.RequestEntityTooLarge, chunked: chunked);
    }

    [Fact]
    public async Task Exact_16_KiB_body_is_accepted_without_disk_buffering()
    {
        fixture.Bus.On<SearchFlightsQuery>((ErrorOr<SearchResult>)new SearchResult([], []));
        var json = $"{{\"legs\":[{Leg}]}}";
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/flights/search/v2")
        {
            Content = new StringContent(
                json + new string(' ', 16 * 1024 - Encoding.UTF8.GetByteCount(json)),
                Encoding.UTF8,
                "application/json"
            ),
        };
        using var response = await fixture.Client.SendAsync(
            request,
            TestContext.Current.CancellationToken
        );
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        fixture.Bus.InvocationCount.ShouldBe(1);
    }

    [Theory]
    [InlineData(1, "one-way", false)]
    [InlineData(2, "multi-leg", false)]
    [InlineData(3, "multi-leg", false)]
    [InlineData(4, "multi-leg", false)]
    public void Current_itinerary_transport_uses_geometry(int count, string kind, bool roundTrip)
    {
        var wire = JsonSerializer.SerializeToElement(
            ItineraryDto.From(BuildItinerary(count)),
            new JsonSerializerOptions(JsonSerializerDefaults.Web)
        );
        wire.GetProperty("journeyKind").GetString().ShouldBe(kind);
        wire.GetProperty("isRoundTrip").GetBoolean().ShouldBe(roundTrip);
    }

    [Theory]
    [InlineData(1, false, "one-way")]
    [InlineData(2, true, "round-trip")]
    [InlineData(2, false, "multi-leg")]
    [InlineData(3, false, "multi-leg")]
    [InlineData(4, false, "multi-leg")]
    public async Task Search_quote_owned_list_detail_cancel_share_geometry_kind(
        int count,
        bool mirrored,
        string kind
    )
    {
        var id = Guid.NewGuid();
        var owner = Guid.NewGuid();
        var now = DateTimeOffset.Parse("2030-06-01T00:00:00Z");
        var itinerary = BuildItinerary(count, mirrored);
        var reference = SupplierPassengerReference.Create("pas_fictional").Value;
        var party = BookableOfferParty
            .Create(
                [new SupplierPassengerSlot(reference, BookingPassengerKind.Adult)],
                new DateOnly(2030, 6, 10),
                true,
                false
            )
            .Value;
        var binding = QuoteBinding
            .Create(
                Guid.NewGuid(),
                party,
                [
                    new QuotePassengerSlot(
                        new BookingPassengerId(Guid.NewGuid()),
                        reference,
                        BookingPassengerKind.Adult
                    ),
                ]
            )
            .Value;
        var price = Money.Create(10000, CurrencyCode.Create("RUB").Value).Value;
        var offer = new BookableOffer(
            OfferId.New(),
            itinerary,
            price,
            ProviderId.Duffel,
            now,
            now.AddMinutes(20),
            new FareConditions(false, false, null, null),
            "off_fictional",
            party
        );
        var view = new OrderView(
            id,
            owner,
            "ord_fictional",
            "Held",
            10000,
            "RUB",
            JsonSerializer.Serialize(
                itinerary,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)
            ),
            [],
            now,
            null,
            null,
            null,
            1
        );
        fixture.Bus.On<SearchFlightsQuery>((ErrorOr<SearchResult>)new SearchResult([offer], []));
        fixture.Bus.On<QuoteOfferCommand>(
            (ErrorOr<QuotedOfferResult>)new QuotedOfferResult(id, offer, binding)
        );
        fixture.Bus.On<GetOrderQuery>((ErrorOr<OrderView>)view);
        fixture.Bus.On<ListOrdersQuery>(new OrderListView([view], 20, 0));
        fixture.Bus.On<CancelOrderCommand>(
            (ErrorOr<CancelledOrderResult>)
                new CancelledOrderResult(
                    id,
                    "Cancelled",
                    new OrderCommandSnapshot(price, itinerary, [], now, null, now, null, 1)
                )
        );
        fixture.IdempotencyStore.Reset();
        foreach (
            var route in new[]
            {
                "/api/flights/search/v2",
                "/api/flights/orders/quote",
                "/api/flights/orders",
                $"/api/flights/orders/{id}",
                $"/api/flights/orders/{id}/cancel",
            }
        )
        {
            var search = route.EndsWith("/v2", StringComparison.Ordinal);
            var quote = route.EndsWith("/quote", StringComparison.Ordinal);
            var cancel = route.EndsWith("/cancel", StringComparison.Ordinal);
            using var request = new HttpRequestMessage(
                search || quote || cancel ? HttpMethod.Post : HttpMethod.Get,
                route
            );
            if (search)
                request.Content = JsonContent.Create(
                    new
                    {
                        legs = itinerary
                            .Slices.Select(s => new
                            {
                                origin = s.Origin.Value,
                                destination = s.Destination.Value,
                                departureDate = DateOnly.FromDateTime(s.DepartAt.DateTime),
                            })
                            .ToArray(),
                    }
                );
            if (quote)
                request.Content = JsonContent.Create(
                    new { providerOfferRef = "off_fictional", provider = "duffel" }
                );
            if (!search && !quote)
                request.Headers.Add(TestAuthHandler.UserIdHeader, owner.ToString());
            if (cancel)
            {
                request.Headers.Add(TestAuthHandler.ScopesHeader, "flights:book");
                request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
            }
            using var response = await fixture.Client.SendAsync(
                request,
                TestContext.Current.CancellationToken
            );
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>(
                TestContext.Current.CancellationToken
            );
            var wire =
                search ? body.GetProperty("offers")[0]
                : quote ? body.GetProperty("offer")
                : body.TryGetProperty("items", out var items) ? items[0]
                : body;
            wire.GetProperty("itinerary").GetProperty("journeyKind").GetString().ShouldBe(kind);
            wire.GetProperty("itinerary")
                .GetProperty("isRoundTrip")
                .GetBoolean()
                .ShouldBe(mirrored);
            wire.GetProperty("itinerary").GetProperty("slices").GetArrayLength().ShouldBe(count);
        }
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    public async Task Historical_flat_null_slice_preserves_contract_error_payload_in_detail_and_list(
        int nullIndex,
        bool list
    )
    {
        var id = Guid.NewGuid();
        var owner = Guid.NewGuid();
        var stored = JsonSerializer.SerializeToNode(
            ItineraryDto.From(BuildItinerary(2, mirrored: true)),
            new JsonSerializerOptions(JsonSerializerDefaults.Web)
        )!;
        stored["slices"]![nullIndex] = null;
        stored["isRoundTrip"] = false;
        stored.AsObject().Remove("journeyKind");
        var view = new OrderView(
            id,
            owner,
            "ord_fictional",
            "Held",
            10000,
            "RUB",
            stored.ToJsonString(),
            [],
            DateTimeOffset.Parse("2030-06-01T00:00:00Z"),
            null,
            null,
            null,
            1
        );
        fixture.Bus.On<GetOrderQuery>((ErrorOr<OrderView>)view);
        fixture.Bus.On<ListOrdersQuery>(new OrderListView([view], 20, 0));
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            list ? "/api/flights/orders" : $"/api/flights/orders/{id}"
        );
        request.Headers.Add(TestAuthHandler.UserIdHeader, owner.ToString());
        using var response = await fixture.Client.SendAsync(
            request,
            TestContext.Current.CancellationToken
        );
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(
            TestContext.Current.CancellationToken
        );
        var actual = (list ? body.GetProperty("items")[0] : body).GetProperty("itinerary");
        actual.GetProperty("slices").GetArrayLength().ShouldBe(2);
        actual.GetProperty("slices")[nullIndex].ValueKind.ShouldBe(JsonValueKind.Null);
        JsonElement
            .DeepEquals(
                actual.GetProperty("slices"),
                JsonSerializer.SerializeToElement(stored["slices"])
            )
            .ShouldBeTrue();
        actual.GetProperty("totalDuration").GetString().ShouldBe("04:00:00");
        actual.GetProperty("isRoundTrip").GetBoolean().ShouldBeFalse();
        actual.GetProperty("journeyKind").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Historical_mirrored_itinerary_preserves_inverted_instants_and_duration_in_owned_HTTP(
        bool flat
    )
    {
        var id = Guid.NewGuid();
        var owner = Guid.NewGuid();
        var itinerary = BuildItinerary(2, mirrored: true);
        var stored = JsonSerializer.SerializeToNode(
            flat ? (object)ItineraryDto.From(itinerary) : itinerary,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)
        )!;
        var segment = stored["slices"]![1]!["segments"]![0]!;
        segment["departAt"] = "2030-06-10T10:30:00+03:00";
        segment["arriveAt"] = "2030-06-10T12:30:00+03:00";
        if (flat)
            stored.AsObject().Remove("journeyKind");
        var view = new OrderView(
            id,
            owner,
            "ord_fictional",
            "Held",
            10000,
            "RUB",
            stored.ToJsonString(),
            [],
            DateTimeOffset.Parse("2030-06-01T00:00:00Z"),
            null,
            null,
            null,
            1
        );
        fixture.Bus.On<GetOrderQuery>((ErrorOr<OrderView>)view);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/flights/orders/{id}");
        request.Headers.Add(TestAuthHandler.UserIdHeader, owner.ToString());
        using var response = await fixture.Client.SendAsync(
            request,
            TestContext.Current.CancellationToken
        );
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var wire = (
            await response.Content.ReadFromJsonAsync<JsonElement>(
                TestContext.Current.CancellationToken
            )
        ).GetProperty("itinerary");
        wire.GetProperty("journeyKind").GetString().ShouldBe("round-trip");
        wire.GetProperty("isRoundTrip").GetBoolean().ShouldBeTrue();
        wire.GetProperty("totalDuration").GetString().ShouldBe("04:00:00");
        wire.GetProperty("slices")[1]
            .GetProperty("segments")[0]
            .GetProperty("departAt")
            .GetDateTimeOffset()
            .ShouldBe(DateTimeOffset.Parse("2030-06-10T10:30:00+03:00"));
    }

    [Theory]
    [InlineData(1, "one-way")]
    [InlineData(2, "multi-leg")]
    [InlineData(3, "multi-leg")]
    [InlineData(4, "multi-leg")]
    public void Flat_historical_DTO_fallback_normalizes_geometry_without_revalidation(
        int count,
        string kind
    )
    {
        var original = ItineraryDto.From(BuildItinerary(count));
        var stored = JsonSerializer.SerializeToNode(
            original with
            {
                IsRoundTrip = true,
            },
            new JsonSerializerOptions(JsonSerializerDefaults.Web)
        )!;
        stored.AsObject().Remove("journeyKind");
        var view = new OrderView(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "ord_fictional",
            "Held",
            10000,
            "RUB",
            stored.ToJsonString(),
            [],
            DateTimeOffset.Parse("2030-06-01T00:00:00Z"),
            null,
            null,
            null,
            1
        );
        var actual = JsonSerializer
            .SerializeToElement(
                OrderResponseMapper.From(view),
                new JsonSerializerOptions(JsonSerializerDefaults.Web)
            )
            .GetProperty("itinerary");
        actual.GetProperty("journeyKind").GetString().ShouldBe(kind);
        actual.GetProperty("isRoundTrip").GetBoolean().ShouldBeFalse();
        actual.GetProperty("totalDuration").GetString().ShouldBe(original.TotalDuration.ToString());
    }

    private async Task AssertRejected(
        string body,
        HttpStatusCode status,
        int? index = null,
        bool chunked = false
    )
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/flights/search/v2")
        {
            Content = chunked
                ? new UnknownLengthContent(body)
                : new StringContent(body, Encoding.UTF8, "application/json"),
        };
        using var response = await fixture.Client.SendAsync(
            request,
            TestContext.Current.CancellationToken
        );
        response.StatusCode.ShouldBe(status);
        fixture.Bus.InvocationCount.ShouldBe(0);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var problem = JsonDocument.Parse(text);
        problem
            .RootElement.GetProperty("type")
            .GetString()
            .ShouldBe(
                status == HttpStatusCode.BadRequest
                    ? "https://travel.local/errors/Flights.MultiLegSearchInvalid"
                    : "https://travel.local/errors/Flights.RequestTooLarge"
            );
        if (index is not null)
            problem.RootElement.GetProperty("legIndex").GetInt32().ShouldBe(index.Value);
        text.ShouldNotContain("private-secret");
        string.Join('\n', fixture.Logs.Messages).ShouldNotContain("private-secret");
    }

    internal static Itinerary BuildItinerary(int count, bool mirrored = false)
    {
        var airports = mirrored
            ? new[] { "LED", "DME", "LED" }
            : new[] { "LED", "DME", "LHR", "CDG", "FRA" };
        return Itinerary
            .Create(
                Enumerable
                    .Range(0, count)
                    .Select(i =>
                        Slice
                            .Create([
                                Segment
                                    .Create(
                                        IataCode.Create(airports[i]).Value,
                                        IataCode.Create(airports[i + 1]).Value,
                                        new DateTimeOffset(
                                            2030,
                                            6,
                                            10 + i,
                                            10,
                                            0,
                                            0,
                                            TimeSpan.FromHours(3)
                                        ),
                                        new DateTimeOffset(
                                            2030,
                                            6,
                                            10 + i,
                                            12,
                                            0,
                                            0,
                                            TimeSpan.FromHours(3)
                                        ),
                                        "SU",
                                        "SU101",
                                        CabinClass.Economy
                                    )
                                    .Value,
                            ])
                            .Value
                    )
                    .ToArray()
            )
            .Value;
    }

    private sealed class UnknownLengthContent : HttpContent
    {
        private readonly byte[] bytes;

        public UnknownLengthContent(string body)
        {
            bytes = Encoding.UTF8.GetBytes(body);
            Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(
                "application/json"
            );
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            stream.WriteAsync(bytes).AsTask();

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
