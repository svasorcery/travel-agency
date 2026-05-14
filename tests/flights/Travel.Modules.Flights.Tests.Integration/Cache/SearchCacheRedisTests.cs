using Shouldly;
using StackExchange.Redis;
using Testcontainers.Redis;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Travel.Modules.Flights.Core.ValueObjects.Offer;
using Travel.Modules.Flights.Infrastructure.Cache;
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
        _cache = new SearchCacheRedis(_redis);
    }

    public async ValueTask DisposeAsync()
    {
        _redis.Dispose();
        await _redisContainer.DisposeAsync();
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
            "duffel-ref-001"
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

        await _cache.SetAsync(key, offers, TimeSpan.FromMinutes(5), ct);

        var result = await _cache.TryGetAsync(key, ct);

        result.ShouldNotBeNull();
        result!.Count.ShouldBe(2);
        result[0].ShouldBeOfType<BookableOffer>();
        result[1].ShouldBeOfType<DeeplinkOffer>();
    }

    [Fact]
    public async Task SetAsync_then_TryGetAsync_preserves_bookable_offer_fields()
    {
        var ct = TestContext.Current.CancellationToken;
        const string key = "flights:search:test-bookable-fields";

        var original = BuildBookableOffer();
        IReadOnlyList<Offer> offers = new List<Offer> { original };

        await _cache.SetAsync(key, offers, TimeSpan.FromMinutes(5), ct);
        var result = await _cache.TryGetAsync(key, ct);

        result.ShouldNotBeNull();
        var deserialized = result![0].ShouldBeOfType<BookableOffer>();
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

        await _cache.SetAsync(key, offers, TimeSpan.FromMinutes(5), ct);
        var result = await _cache.TryGetAsync(key, ct);

        result.ShouldNotBeNull();
        var deserialized = result![0].ShouldBeOfType<DeeplinkOffer>();
        deserialized.PartnerName.ShouldBe(original.PartnerName);
        deserialized.DeeplinkUrl.ShouldBe(original.DeeplinkUrl);
    }
}
