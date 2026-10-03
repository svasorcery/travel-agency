using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using StackExchange.Redis;
using Testcontainers.Redis;
using Travel.Modules.Flights.Application.Queries;
using Travel.Modules.Flights.Application.Search;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Travel.Modules.Flights.Core.ValueObjects.Offer;
using Travel.Modules.Flights.Infrastructure.Cache;
using Travel.Tests.Fixtures;
using Xunit;

namespace Travel.Modules.Flights.Tests.Integration.Cache;

[Trait("Category", "Integration")]
public sealed class SearchCacheRedisTests : IAsyncLifetime
{
    private readonly RedisContainer _redisContainer = new RedisBuilder("redis:7-alpine").Build();
    private IConnectionMultiplexer _redis = default!;
    private SearchCacheRedis _cache = default!;

    private static readonly IataCode Led = IataCode.Create("LED").Value;
    private static readonly IataCode Dme = IataCode.Create("DME").Value;
    private static readonly CurrencyCode Rub = CurrencyCode.Create("RUB").Value;

    public async ValueTask InitializeAsync()
    {
        await _redisContainer.StartAsync();
        _redis = await ConnectionMultiplexer.ConnectAsync(_redisContainer.GetConnectionString());
        _cache = new SearchCacheRedis(_redis, NullLogger<SearchCacheRedis>.Instance);
    }

    public async ValueTask DisposeAsync()
    {
        _redis.Dispose();
        await _redisContainer.DisposeAsync();
    }

    private static SearchResult Result(IReadOnlyList<Offer> offers)
    {
        var ranked = OfferRanker.Rank(
            offers.Select(o => new RankingCandidate(o, o.TotalAmount, RankingPriceState.Native)),
            Rub
        );
        return new SearchResult(ranked.Offers, [], ranked.Ranking, []);
    }

    private static Itinerary BuildItinerary()
    {
        var depart = new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero);
        var arrive = depart.AddHours(2);

        var segment = Segment
            .Create(Led, Dme, depart, arrive, "SU", "SU1234", CabinClass.Economy)
            .Value;

        var slice = Slice.Create(new[] { segment }).Value;
        return Itinerary.Create(new[] { slice }).Value;
    }

    private static BookableOffer BuildBookableOffer()
    {
        var itinerary = BuildItinerary();
        var money = Money.Create(5000m, Rub).Value;
        var fetchedAt = new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero);
        var expiresAt = fetchedAt.AddHours(1);
        var fare = new FareConditions(
            ChangeAllowed: false,
            RefundAllowed: false,
            FareBasisCode: "YECO",
            CabinClassMarketing: "Economy"
        );

        return new BookableOffer(
            OfferId.New(),
            itinerary,
            money,
            ProviderId.Duffel,
            fetchedAt,
            expiresAt,
            fare,
            "duffel-ref-001",
            TestPii.Binding().Party
        );
    }

    private static DeeplinkOffer BuildDeeplinkOffer()
    {
        var itinerary = BuildItinerary();
        var money = Money.Create(4800m, Rub).Value;
        var fetchedAt = new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero);

        return new DeeplinkOffer(
            OfferId.New(),
            itinerary,
            money,
            ProviderId.Travelpayouts,
            fetchedAt,
            new Uri("https://tp.example.com/deeplink?token=abc"),
            "Aviasales"
        );
    }

    [Fact]
    public async Task TryGetAsync_returns_null_for_missing_key()
    {
        var ct = TestContext.Current.CancellationToken;

        var result = await _cache.TryGetAsync("flights:search:nonexistent", ct);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task SetAsync_then_TryGetAsync_round_trips_mixed_offers()
    {
        var ct = TestContext.Current.CancellationToken;
        const string key = "flights:search:test-mixed";

        var bookable = BuildBookableOffer();
        var deeplink = BuildDeeplinkOffer();
        IReadOnlyList<Offer> offers = new List<Offer> { bookable, deeplink };

        await _cache.SetAsync(key, Result(offers), TimeSpan.FromMinutes(5), ct);

        var result = await _cache.TryGetAsync(key, ct);

        result.ShouldNotBeNull();
        result!.Offers.Count.ShouldBe(2);
        result.Offers.ShouldContain(o => o is BookableOffer);
        result.Offers.ShouldContain(o => o is DeeplinkOffer);
    }

    [Fact]
    public async Task SetAsync_then_TryGetAsync_preserves_bookable_offer_fields()
    {
        var ct = TestContext.Current.CancellationToken;
        const string key = "flights:search:test-bookable-fields";

        var original = BuildBookableOffer();
        IReadOnlyList<Offer> offers = new List<Offer> { original };

        await _cache.SetAsync(key, Result(offers), TimeSpan.FromMinutes(5), ct);
        var result = await _cache.TryGetAsync(key, ct);

        result.ShouldNotBeNull();
        var deserialized = result!.Offers[0].ShouldBeOfType<BookableOffer>();
        deserialized.ProviderOfferRef.ShouldBe(original.ProviderOfferRef);
        deserialized.TotalAmount.Amount.ShouldBe(original.TotalAmount.Amount);
        deserialized.TotalAmount.Currency.ShouldBe(original.TotalAmount.Currency);
    }

    [Fact]
    public async Task SetAsync_then_TryGetAsync_preserves_deeplink_offer_fields()
    {
        var ct = TestContext.Current.CancellationToken;
        const string key = "flights:search:test-deeplink-fields";

        var original = BuildDeeplinkOffer();
        IReadOnlyList<Offer> offers = new List<Offer> { original };

        await _cache.SetAsync(key, Result(offers), TimeSpan.FromMinutes(5), ct);
        var result = await _cache.TryGetAsync(key, ct);

        result.ShouldNotBeNull();
        var deserialized = result!.Offers[0].ShouldBeOfType<DeeplinkOffer>();
        deserialized.PartnerName.ShouldBe(original.PartnerName);
        deserialized.DeeplinkUrl.ShouldBe(original.DeeplinkUrl);
    }

    [Fact]
    public async Task Corrupt_cache_entry_is_treated_as_a_miss()
    {
        var ct = TestContext.Current.CancellationToken;
        const string key = "flights:search:corrupt-entry";

        // Directly write a malformed JSON blob to Redis
        var db = _redis.GetDatabase();
        await db.StringSetAsync(key, "{ this is not valid json {{[[");

        // TryGetAsync must return null (cache miss), not throw
        var result = await _cache.TryGetAsync(key, ct);

        result.ShouldBeNull("corrupt/old-schema cache entries must be treated as misses");
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{\"schemaVersion\":1,\"result\":null}")]
    [InlineData("{\"schemaVersion\":2,\"result\":{\"offers\":[],\"partialFailures\":[]}}")]
    public async Task Legacy_or_incomplete_envelopes_are_misses(string json)
    {
        await _redis.GetDatabase().StringSetAsync("invalid-envelope", json);
        (
            await _cache.TryGetAsync("invalid-envelope", TestContext.Current.CancellationToken)
        ).ShouldBeNull();
    }

    [Fact]
    public async Task Inconsistent_cached_rank_is_a_miss()
    {
        var ct = TestContext.Current.CancellationToken;
        await _cache.SetAsync(
            "bad-rank",
            Result([BuildBookableOffer()]),
            TimeSpan.FromMinutes(5),
            ct
        );
        var db = _redis.GetDatabase();
        var node = System.Text.Json.Nodes.JsonNode.Parse(
            (string)(await db.StringGetAsync("bad-rank"))!
        )!;
        node["result"]!["ranking"]!["entries"]![0]!["rank"] = 42;
        await db.StringSetAsync("bad-rank", node.ToJsonString());
        (await _cache.TryGetAsync("bad-rank", ct)).ShouldBeNull();
    }

    [Theory]
    [InlineData("negative-display")]
    [InlineData("invalid-source-currency")]
    [InlineData("negative-duration")]
    public async Task Invalid_restored_value_objects_are_a_cache_miss(string corruption)
    {
        var ct = TestContext.Current.CancellationToken;
        var offer = BuildBookableOffer();
        var ranked = OfferRanker.Rank(
            [
                new RankingCandidate(
                    offer,
                    Money.Create(100, CurrencyCode.Create("EUR").Value).Value,
                    RankingPriceState.Converted
                ),
            ],
            Rub
        );
        await _cache.SetAsync(
            "invalid-values",
            new SearchResult(ranked.Offers, [], ranked.Ranking, []),
            TimeSpan.FromMinutes(5),
            ct
        );
        var db = _redis.GetDatabase();
        var node = System.Text.Json.Nodes.JsonNode.Parse(
            (string)(await db.StringGetAsync("invalid-values"))!
        )!;
        var restoredOffer = node["result"]!["offers"]![0]!;
        var entry = node["result"]!["ranking"]!["entries"]![0]!;
        switch (corruption)
        {
            case "negative-display":
                restoredOffer["totalAmount"]!["amount"] = -1;
                break;
            case "invalid-source-currency":
                entry["sourcePrice"]!["currency"]!["value"] = "invalid";
                break;
            case "negative-duration":
                restoredOffer["itinerary"]!["totalDuration"]!["value"] = "-00:00:01";
                entry["durationSeconds"] = -1;
                break;
        }
        await db.StringSetAsync("invalid-values", node.ToJsonString());
        (await _cache.TryGetAsync("invalid-values", ct)).ShouldBeNull();
    }
}
