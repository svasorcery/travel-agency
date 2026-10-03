using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ErrorOr;
using Shouldly;
using Travel.Modules.Flights.Application.Queries;
using Travel.Modules.Flights.Application.Search;
using Travel.Modules.Flights.Core.Errors;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Travel.Modules.Flights.Core.ValueObjects.Offer;
using Xunit;

namespace Travel.Host.Tests.Integration.Flights;

[Collection(HostIntegrationCollection.Name)]
public sealed class FlightsSearchContractHttpTests : IClassFixture<FlightsApiFixture>
{
    private static readonly IataCode Led = IataCode.Create("LED").Value;
    private static readonly IataCode Dme = IataCode.Create("DME").Value;
    private static readonly CurrencyCode Rub = CurrencyCode.Create("RUB").Value;
    private static readonly DateTimeOffset FetchedAt = DateTimeOffset.Parse(
        "2030-06-01T08:00:00+00:00"
    );
    private readonly FlightsApiFixture _fixture;

    public FlightsSearchContractHttpTests(FlightsApiFixture fixture)
    {
        _fixture = fixture;
        _fixture.Bus.Reset();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Search_serializes_ranking_facts_without_calling_AI(bool naturalLanguage)
    {
        var offer = new BookableOffer(
            OfferId.New(),
            BuildItinerary(false),
            Money.Create(10500, Rub).Value,
            ProviderId.Duffel,
            FetchedAt,
            FetchedAt.AddMinutes(20),
            new FareConditions(false, false, null, null),
            "fictional",
            Party: CreateParty(1)
        );
        var ranked = OfferRanker.Rank(
            [new RankingCandidate(offer, offer.TotalAmount, RankingPriceState.Native)],
            Rub
        );
        ErrorOr<SearchResult> result = new SearchResult(ranked.Offers, [], ranked.Ranking);
        _fixture.Bus.On<SearchFlightsQuery>(result);
        _fixture.Bus.On<NlSearchQuery>(result);
        using var response = await _fixture.Client.PostAsJsonAsync(
            naturalLanguage ? "/api/flights/search/nl" : "/api/flights/search?currency=RUB",
            naturalLanguage
                ? (object)new { query = "fictional search" }
                : LoadCase("oneWay").GetProperty("request"),
            TestContext.Current.CancellationToken
        );
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)
        );
        var ranking = json.RootElement.GetProperty("ranking");
        ranking.GetProperty("policy").GetString().ShouldBe("price-first-v1");
        var entry = ranking.GetProperty("entries")[0];
        entry.GetProperty("offerId").GetGuid().ShouldBe(offer.Id.Value);
        entry.GetProperty("priceState").GetString().ShouldBe("native");
        entry.GetProperty("sourceAmount").GetDecimal().ShouldBe(10500);
        entry.GetProperty("durationSeconds").GetInt64().ShouldBe(7200);
        entry.GetProperty("transfers").GetInt32().ShouldBe(0);
    }

    [Theory]
    [InlineData("oneWay", false)]
    [InlineData("roundTrip", true)]
    public async Task Anonymous_search_serializes_both_offer_variants(
        string caseName,
        bool roundTrip
    )
    {
        var example = LoadCase(caseName);
        var itinerary = BuildItinerary(roundTrip);
        var bookable = new BookableOffer(
            new OfferId(Guid.Parse("11111111-1111-1111-1111-111111111111")),
            itinerary,
            Money.Create(roundTrip ? 20500m : 10500m, Rub).Value,
            ProviderId.Duffel,
            FetchedAt,
            FetchedAt.AddMinutes(20),
            new FareConditions(false, false, null, null),
            roundTrip ? "off_fixture_rt_2030-06-10_2030-06-17" : "off_fixture_ow_2030-06-10",
            Party: CreateParty(1)
        );
        var partnerSegment = Segment
            .Create(
                Led,
                Dme,
                new DateTimeOffset(2030, 6, 10, 12, 0, 0, TimeSpan.FromHours(3)),
                new DateTimeOffset(2030, 6, 10, 14, 0, 0, TimeSpan.FromHours(3)),
                "DP",
                "DP202",
                CabinClass.Economy
            )
            .Value;
        var partner = new DeeplinkOffer(
            new OfferId(Guid.Parse("22222222-2222-2222-2222-222222222222")),
            Itinerary.Create([Slice.Create([partnerSegment]).Value]).Value,
            Money.Create(roundTrip ? 18500m : 9800m, Rub).Value,
            ProviderId.Travelpayouts,
            FetchedAt,
            new Uri("https://partner.invalid/fixture"),
            "Example Partner"
        );
        _fixture.Bus.On<SearchFlightsQuery>(
            (ErrorOr<SearchResult>)new SearchResult([bookable, partner], [])
        );

        using var response = await _fixture.Client.PostAsJsonAsync(
            "/api/flights/search?currency=RUB",
            example.GetProperty("request"),
            TestContext.Current.CancellationToken
        );
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var actual = JsonDocument.Parse(body);
        JsonElement
            .DeepEquals(actual.RootElement, example.GetProperty("currentResponse"))
            .ShouldBeTrue($"Wire response for {caseName} differed: {body}");
    }

    [Theory]
    [InlineData("oneWay", false)]
    [InlineData("roundTrip", true)]
    public async Task Anonymous_search_binds_the_documented_criteria(
        string caseName,
        bool roundTrip
    )
    {
        var example = LoadCase(caseName);
        SearchCriteria? captured = null;
        _fixture.Bus.OnCapture<SearchFlightsQuery>(query =>
        {
            captured = query.Criteria;
            return (ErrorOr<SearchResult>)new SearchResult([], []);
        });
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/flights/search?currency=RUB"
        )
        {
            Content = JsonContent.Create(example.GetProperty("request")),
        };
        request.Headers.AcceptLanguage.ParseAdd("en-US,en;q=0.8");

        using var response = await _fixture.Client.SendAsync(
            request,
            TestContext.Current.CancellationToken
        );

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        captured.ShouldNotBeNull();
        captured.Origin.Value.ShouldBe("LED");
        captured.Destination.Value.ShouldBe("DME");
        captured.DepartureDate.ShouldBe(new DateOnly(2030, 6, 10));
        captured.ReturnDate.ShouldBe(roundTrip ? new DateOnly(2030, 6, 17) : null);
        captured.PassengerCount.ShouldBe(1);
        captured.CabinClass.Code.ShouldBe("economy");
        captured.Currency.Value.ShouldBe("RUB");
        captured.Locale.ShouldBe("en");
    }

    [Theory]
    [InlineData("partial")]
    [InlineData("empty")]
    [InlineData("emptyPartial")]
    [InlineData("mixedCurrency")]
    public async Task Search_result_states_match_the_shared_wire_examples(string caseName)
    {
        var failure = new ProviderFailure("travelpayouts", "Timeout", 4000);
        SearchResult result = caseName switch
        {
            "partial" => new SearchResult([CreateBookable(10500m, Rub)], [failure]),
            "empty" => new SearchResult([], []),
            "emptyPartial" => new SearchResult([], [failure]),
            "mixedCurrency" => new SearchResult(
                [CreateBookable(10000m, CurrencyCode.Create("EUR").Value)],
                []
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(caseName)),
        };
        _fixture.Bus.On<SearchFlightsQuery>((ErrorOr<SearchResult>)result);
        using var response = await _fixture.Client.PostAsJsonAsync(
            "/api/flights/search?currency=RUB",
            LoadCase("oneWay").GetProperty("request"),
            TestContext.Current.CancellationToken
        );
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)
        );
        JsonElement
            .DeepEquals(body.RootElement, LoadCase(caseName).GetProperty("currentResponse"))
            .ShouldBeTrue($"Wire response for {caseName} differed: {body.RootElement}");
    }

    [Fact]
    public async Task All_provider_failure_is_a_problem_not_an_empty_result()
    {
        _fixture.Bus.On<SearchFlightsQuery>(
            (ErrorOr<SearchResult>)FlightsErrors.ProviderUnavailable("all")
        );
        using var response = await _fixture.Client.PostAsJsonAsync(
            "/api/flights/search?currency=RUB",
            LoadCase("oneWay").GetProperty("request"),
            TestContext.Current.CancellationToken
        );
        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        using var actual = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)
        );
        var expected = LoadCase("unavailable").GetProperty("currentResponse");
        foreach (var field in new[] { "type", "title", "status", "detail", "errors" })
            JsonElement
                .DeepEquals(actual.RootElement.GetProperty(field), expected.GetProperty(field))
                .ShouldBeTrue($"ProblemDetails field {field} changed");
        actual.RootElement.GetProperty("traceId").GetString().ShouldNotBeNullOrWhiteSpace();
    }

    private static Itinerary BuildItinerary(bool roundTrip)
    {
        var outbound = Segment
            .Create(
                Led,
                Dme,
                new DateTimeOffset(2030, 6, 10, 10, 0, 0, TimeSpan.FromHours(3)),
                new DateTimeOffset(2030, 6, 10, 12, 0, 0, TimeSpan.FromHours(3)),
                "SU",
                "SU101",
                CabinClass.Economy
            )
            .Value;
        var slices = new List<Slice> { Slice.Create([outbound]).Value };
        if (roundTrip)
        {
            var inbound = Segment
                .Create(
                    Dme,
                    Led,
                    new DateTimeOffset(2030, 6, 17, 17, 0, 0, TimeSpan.FromHours(3)),
                    new DateTimeOffset(2030, 6, 17, 19, 0, 0, TimeSpan.FromHours(3)),
                    "SU",
                    "SU102",
                    CabinClass.Economy
                )
                .Value;
            slices.Add(Slice.Create([inbound]).Value);
        }
        return Itinerary.Create(slices).Value;
    }

    private static BookableOffer CreateBookable(decimal amount, CurrencyCode currency) =>
        new(
            new OfferId(Guid.Parse("11111111-1111-1111-1111-111111111111")),
            BuildItinerary(false),
            Money.Create(amount, currency).Value,
            ProviderId.Duffel,
            FetchedAt,
            FetchedAt.AddMinutes(20),
            new FareConditions(false, false, null, null),
            "off_fixture_duffel_1",
            Party: CreateParty(1)
        );

    private static BookableOfferParty CreateParty(int count) =>
        BookableOfferParty
            .Create(
                Enumerable
                    .Range(1, count)
                    .Select(i => new SupplierPassengerSlot(
                        SupplierPassengerReference.Create($"pas_http_{i}").Value,
                        BookingPassengerKind.Adult
                    )),
                new DateOnly(2030, 6, 10),
                true,
                false
            )
            .Value;

    [Theory]
    [InlineData("groupTwo", 2, 20750)]
    [InlineData("groupNine", 9, 92900)]
    public async Task Group_search_matches_exact_group_total_and_separate_provider_skips(
        string caseName,
        int count,
        int total
    )
    {
        var example = LoadCase(caseName);
        var offer = CreateBookable(total, Rub) with
        {
            ProviderOfferRef = "off_fixture_ow_2030-06-10",
            Party = CreateParty(count),
        };
        _fixture.Bus.OnCapture<SearchFlightsQuery>(query =>
        {
            query.Criteria.PassengerCount.ShouldBe(count);
            return (ErrorOr<SearchResult>)
                new SearchResult(
                    [offer],
                    [],
                    SkippedProviders:
                    [
                        new SkippedProvider("travelpayouts", "passenger-count-unsupported"),
                    ]
                );
        });
        using var response = await _fixture.Client.PostAsJsonAsync(
            "/api/flights/search?currency=RUB",
            example.GetProperty("request"),
            TestContext.Current.CancellationToken
        );
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var actual = JsonDocument.Parse(text);
        JsonElement
            .DeepEquals(actual.RootElement, example.GetProperty("currentResponse"))
            .ShouldBeTrue($"Group wire fixture drift: {text}");
        actual
            .RootElement.GetProperty("skippedProviders")[0]
            .TryGetProperty("elapsedMs", out _)
            .ShouldBeFalse();
        text.ShouldNotContain("pas_http_");
    }

    [Theory]
    [InlineData("0")]
    [InlineData("10")]
    [InlineData("1.5")]
    public async Task Invalid_request_counts_never_reach_search_bus(string count)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/flights/search")
        {
            Content = new StringContent(
                $"{{\"origin\":\"LED\",\"destination\":\"DME\",\"departureDate\":\"2030-06-10\",\"passengerCount\":{count}}}",
                System.Text.Encoding.UTF8,
                "application/json"
            ),
        };
        using var response = await _fixture.Client.SendAsync(
            request,
            TestContext.Current.CancellationToken
        );
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        _fixture.Bus.InvocationCount.ShouldBe(0);
    }

    private static JsonElement LoadCase(string caseName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (
            directory is not null && !File.Exists(Path.Combine(directory.FullName, "Travel.slnx"))
        )
            directory = directory.Parent;
        directory.ShouldNotBeNull();
        var path = Path.Combine(directory.FullName, "tests", "fixtures", "flights-search.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.GetProperty(caseName).Clone();
    }
}
