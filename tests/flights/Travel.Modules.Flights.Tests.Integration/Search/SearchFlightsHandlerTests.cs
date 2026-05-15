using System.Text.Json;
using ErrorOr;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using StackExchange.Redis;
using Testcontainers.Redis;
using Travel.Modules.Flights.Application.Handlers.Search;
using Travel.Modules.Flights.Application.Observability;
using Travel.Modules.Flights.Application.Queries;
using Travel.Modules.Flights.Application.Search;
using Travel.Modules.Flights.Core.Errors;
using Travel.Modules.Flights.Core.Providers;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Travel.Modules.Flights.Core.ValueObjects.Offer;
using Travel.Modules.Flights.Infrastructure.Cache;
using Xunit;

namespace Travel.Modules.Flights.Tests.Integration.Search;

[Trait("Category", "Integration")]
public sealed class SearchFlightsHandlerTests : IAsyncLifetime
{
    private readonly RedisContainer _redisContainer = new RedisBuilder("redis:7-alpine").Build();
    private IConnectionMultiplexer _redis = default!;
    private ISearchCache _cache = default!;

    private static readonly IataCode Led = IataCode.Create("LED").Value;
    private static readonly IataCode Dme = IataCode.Create("DME").Value;
    private static readonly CurrencyCode Rub = CurrencyCode.Create("RUB").Value;
    private static readonly CurrencyCode Eur = CurrencyCode.Create("EUR").Value;

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

    // ─── helpers ───────────────────────────────────────────────────────────────

    private static SearchCriteria BuildCriteria() =>
        SearchCriteria
            .Create(
                Led,
                Dme,
                new DateOnly(2026, 6, 1),
                returnDate: null,
                passengerCount: 1,
                CabinClass.Economy,
                Rub
            )
            .Value;

    private static Itinerary BuildItinerary(
        string carrier = "SU",
        string flightNumber = "SU1234",
        DateTimeOffset? departAt = null,
        DateTimeOffset? arriveAt = null
    )
    {
        var depart = departAt ?? new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero);
        var arrive = arriveAt ?? depart.AddHours(2);
        var segment = Segment
            .Create(Led, Dme, depart, arrive, carrier, flightNumber, CabinClass.Economy)
            .Value;
        var slice = Slice.Create(new[] { segment }).Value;
        return Itinerary.Create(new[] { slice }).Value;
    }

    private static BookableOffer BuildBookable(
        decimal amount,
        string carrier = "SU",
        string flightNumber = "SU1234",
        CurrencyCode? currency = null
    ) =>
        new(
            OfferId.New(),
            BuildItinerary(carrier, flightNumber),
            Money.Create(amount, currency ?? Rub).Value,
            ProviderId.Duffel,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddHours(1),
            new FareConditions(false, false, "YECO", "Economy"),
            "ref-" + Guid.NewGuid()
        );

    private static DeeplinkOffer BuildDeeplink(
        decimal amount,
        string carrier = "S7",
        string flightNumber = "S71000"
    ) =>
        new(
            OfferId.New(),
            BuildItinerary(carrier, flightNumber),
            Money.Create(amount, Rub).Value,
            ProviderId.Travelpayouts,
            DateTimeOffset.UtcNow,
            new Uri("https://tp.example.com/deeplink"),
            "Aviasales"
        );

    private static SearchFlightsQuery BuildQuery() => new(BuildCriteria());

    // ─── fake providers ─────────────────────────────────────────────────────────

    private sealed class FakeProvider : IFlightSearchProvider
    {
        private readonly IReadOnlyList<Offer> _offers;
        private int _callCount;

        public FakeProvider(ProviderId id, IReadOnlyList<Offer> offers)
        {
            Id = id;
            _offers = offers;
        }

        public ProviderId Id { get; }
        public int CallCount => _callCount;

        public Task<ErrorOr<IReadOnlyList<Offer>>> SearchAsync(
            SearchCriteria criteria,
            CancellationToken ct
        )
        {
            Interlocked.Increment(ref _callCount);
            return Task.FromResult(ErrorOrFactory.From<IReadOnlyList<Offer>>(_offers));
        }
    }

    private sealed class FailingProvider : IFlightSearchProvider
    {
        public FailingProvider(ProviderId id) => Id = id;

        public ProviderId Id { get; }

        public Task<ErrorOr<IReadOnlyList<Offer>>> SearchAsync(
            SearchCriteria criteria,
            CancellationToken ct
        ) =>
            Task.FromResult<ErrorOr<IReadOnlyList<Offer>>>(
                FlightsErrors.ProviderUnavailable(Id.Value)
            );
    }

    private sealed class ThrowingProvider(ProviderId id, Exception exception)
        : IFlightSearchProvider
    {
        public ProviderId Id => id;

        public Task<ErrorOr<IReadOnlyList<Offer>>> SearchAsync(
            SearchCriteria criteria,
            CancellationToken ct
        ) => throw exception;
    }

    /// <summary>
    /// Returns its offers but only after a delay that exceeds the handler's 4-second budget.
    /// </summary>
    private sealed class SlowProvider(ProviderId id, IReadOnlyList<Offer> offers)
        : IFlightSearchProvider
    {
        public ProviderId Id => id;

        public async Task<ErrorOr<IReadOnlyList<Offer>>> SearchAsync(
            SearchCriteria criteria,
            CancellationToken ct
        )
        {
            // Delay longer than the 4 s handler budget; will be cancelled by the linked CTS
            await Task.Delay(TimeSpan.FromSeconds(10), ct);
            return ErrorOrFactory.From<IReadOnlyList<Offer>>(offers);
        }
    }

    private sealed class NoOpMetrics : IFlightsMetrics
    {
        public void RecordSearchLatency(double elapsedMs, string provider, string status) { }

        public void RecordSearchError(string provider) { }

        public void RecordPaymentOutcome(bool success) { }

        public void RecordAggregateEventsAppended(string eventType, long count = 1) { }

        public void RecordNlSearchUsage(int inputTokens, int outputTokens, decimal costUsd) { }

        public void RecordWebhookReceived(string eventType) { }

        public void RecordWebhookProcessingLag(double ms, string eventType) { }

        public void RecordAirlineInitiatedChange() { }

        public void RecordPaymentDuration(double ms, string outcome) { }

        public void RecordNlSearchDuration(double ms) { }
    }

    /// <summary>
    /// IFxRates stub that applies a fixed multiplier per (from, to) pair.
    /// If the currency is already the target, returns 1:1.
    /// </summary>
    private sealed class FakeIFxRates(Dictionary<(CurrencyCode, CurrencyCode), decimal> rates)
        : IFxRates
    {
        public Task<ErrorOr<decimal>> GetRateAsync(
            CurrencyCode from,
            CurrencyCode to,
            CancellationToken ct
        )
        {
            if (from == to)
                return Task.FromResult<ErrorOr<decimal>>(1m);
            if (rates.TryGetValue((from, to), out var r))
                return Task.FromResult<ErrorOr<decimal>>(r);
            return Task.FromResult<ErrorOr<decimal>>(
                Error.NotFound("FxRate.NotFound", $"No rate for {from}->{to}")
            );
        }

        public async Task<ErrorOr<Money>> ConvertAsync(
            Money amount,
            CurrencyCode to,
            CancellationToken ct
        )
        {
            var rateResult = await GetRateAsync(amount.Currency, to, ct);
            if (rateResult.IsError)
                return rateResult.FirstError;
            return Money.Create(amount.Amount * rateResult.Value, to);
        }
    }

    private static readonly IFxRates PassthroughFx = new FakeIFxRates(
        new Dictionary<(CurrencyCode, CurrencyCode), decimal>()
    );

    // ─── tests ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task BothProvidersReturn_MergedDedupedRanked_SecondCallHitsCache()
    {
        var ct = TestContext.Current.CancellationToken;

        // Distinct flights so both are kept after dedup
        var offer1 = BuildBookable(5000m, "SU", "SU1001");
        var offer2 = BuildBookable(3000m, "S7", "S71002");

        var provider1 = new FakeProvider(ProviderId.Duffel, new[] { offer1 });
        var provider2 = new FakeProvider(ProviderId.Travelpayouts, new[] { offer2 });

        var result = await SearchFlightsHandler.Handle(
            BuildQuery(),
            new[] { provider1, provider2 },
            _cache,
            PassthroughFx,
            new NoOpMetrics(),
            TimeProvider.System,
            NullLogger<SearchFlightsQuery>.Instance,
            ct
        );

        result.IsError.ShouldBeFalse();
        result.Value.PartialFailures.ShouldBeEmpty();
        result.Value.Offers.Count.ShouldBe(2);
        // Ranked by price: cheapest first
        result.Value.Offers[0].TotalAmount.Amount.ShouldBe(3000m);

        // Second call — cache hit, providers not called again
        var result2 = await SearchFlightsHandler.Handle(
            BuildQuery(),
            new[] { provider1, provider2 },
            _cache,
            PassthroughFx,
            new NoOpMetrics(),
            TimeProvider.System,
            NullLogger<SearchFlightsQuery>.Instance,
            ct
        );

        result2.IsError.ShouldBeFalse();
        result2.Value.Offers.Count.ShouldBe(2);
        provider1.CallCount.ShouldBe(1);
        provider2.CallCount.ShouldBe(1);
    }

    [Fact]
    public async Task OneProviderFails_ResultHasOtherOffersAndOneFailure()
    {
        var ct = TestContext.Current.CancellationToken;

        var offer = BuildBookable(4500m, "SU", "SU2001");
        var goodProvider = new FakeProvider(ProviderId.Duffel, new[] { offer });
        var badProvider = new FailingProvider(ProviderId.Travelpayouts);

        // Use a unique criteria so cache doesn't interfere with other tests
        var criteria = SearchCriteria
            .Create(Led, Dme, new DateOnly(2026, 7, 15), null, 1, CabinClass.Business, Rub)
            .Value;
        var query = new SearchFlightsQuery(criteria);

        var result = await SearchFlightsHandler.Handle(
            query,
            new IFlightSearchProvider[] { goodProvider, badProvider },
            _cache,
            PassthroughFx,
            new NoOpMetrics(),
            TimeProvider.System,
            NullLogger<SearchFlightsQuery>.Instance,
            ct
        );

        result.IsError.ShouldBeFalse();
        result.Value.Offers.Count.ShouldBe(1);
        result.Value.Offers[0].TotalAmount.Amount.ShouldBe(4500m);
        result.Value.PartialFailures.Count.ShouldBe(1);
        result.Value.PartialFailures[0].Provider.ShouldBe(ProviderId.Travelpayouts.Value);
    }

    [Fact]
    public async Task BothProvidersFail_ReturnsProviderUnavailableError()
    {
        var ct = TestContext.Current.CancellationToken;

        var bad1 = new FailingProvider(ProviderId.Duffel);
        var bad2 = new FailingProvider(ProviderId.Travelpayouts);

        // Use unique criteria
        var criteria = SearchCriteria
            .Create(Led, Dme, new DateOnly(2026, 8, 20), null, 1, CabinClass.First, Rub)
            .Value;
        var query = new SearchFlightsQuery(criteria);

        var result = await SearchFlightsHandler.Handle(
            query,
            new IFlightSearchProvider[] { bad1, bad2 },
            _cache,
            PassthroughFx,
            new NoOpMetrics(),
            TimeProvider.System,
            NullLogger<SearchFlightsQuery>.Instance,
            ct
        );

        result.IsError.ShouldBeTrue();
        result.FirstError.Code.ShouldBe("Flights.ProviderUnavailable");
    }

    [Fact]
    public async Task Provider_throwing_non_cancellation_exception_becomes_partial_failure()
    {
        var ct = TestContext.Current.CancellationToken;

        var goodOffer = BuildBookable(4500m, "SU", "SU3001");
        var goodProvider = new FakeProvider(ProviderId.Duffel, new[] { goodOffer });
        var throwingProvider = new ThrowingProvider(
            ProviderId.Travelpayouts,
            new JsonException("Malformed Travelpayouts response")
        );

        var criteria = SearchCriteria
            .Create(Led, Dme, new DateOnly(2026, 9, 10), null, 1, CabinClass.Economy, Rub)
            .Value;
        var query = new SearchFlightsQuery(criteria);

        var result = await SearchFlightsHandler.Handle(
            query,
            new IFlightSearchProvider[] { goodProvider, throwingProvider },
            _cache,
            PassthroughFx,
            new NoOpMetrics(),
            TimeProvider.System,
            NullLogger<SearchFlightsQuery>.Instance,
            ct
        );

        result.IsError.ShouldBeFalse(
            "a single-provider exception should not fault the whole search"
        );
        result.Value.Offers.Count.ShouldBe(1);
        result.Value.Offers[0].TotalAmount.Amount.ShouldBe(4500m);
        result.Value.PartialFailures.Count.ShouldBe(1);
        result.Value.PartialFailures[0].Provider.ShouldBe(ProviderId.Travelpayouts.Value);
    }

    [Fact]
    public async Task Offers_are_normalized_to_requested_currency_before_ranking()
    {
        var ct = TestContext.Current.CancellationToken;

        // Provider 1: offer in EUR at 100 EUR
        var eurOffer = BuildBookable(100m, "SU", "SU4001", Eur);
        // Provider 2: offer in RUB at 8000 RUB
        var rubOffer = BuildBookable(8000m, "S7", "S74002", Rub);

        var provider1 = new FakeProvider(ProviderId.Duffel, new[] { (Offer)eurOffer });
        var provider2 = new FakeProvider(ProviderId.Travelpayouts, new[] { (Offer)rubOffer });

        // 1 EUR = 100 RUB in our fake rates; so eurOffer normalises to 10000 RUB
        var fxRates = new FakeIFxRates(
            new Dictionary<(CurrencyCode, CurrencyCode), decimal>
            {
                { (Eur, Rub), 100m },
                { (Rub, Rub), 1m },
            }
        );

        var criteria = SearchCriteria
            .Create(Led, Dme, new DateOnly(2026, 10, 1), null, 1, CabinClass.Economy, Rub)
            .Value;
        var query = new SearchFlightsQuery(criteria);

        var result = await SearchFlightsHandler.Handle(
            query,
            new IFlightSearchProvider[] { provider1, provider2 },
            _cache,
            fxRates,
            new NoOpMetrics(),
            TimeProvider.System,
            NullLogger<SearchFlightsQuery>.Instance,
            ct
        );

        result.IsError.ShouldBeFalse();
        result.Value.Offers.Count.ShouldBe(2);

        // rubOffer (8000 RUB) < eurOffer (10000 RUB after conversion) → rubOffer ranks first
        result.Value.Offers[0].TotalAmount.Currency.ShouldBe(Rub);
        result.Value.Offers[0].TotalAmount.Amount.ShouldBe(8000m);
        result.Value.Offers[1].TotalAmount.Amount.ShouldBe(10000m);
    }

    [Fact]
    public async Task Slow_provider_times_out_as_partial_failure()
    {
        var ct = TestContext.Current.CancellationToken;

        var fastOffer = BuildBookable(3000m, "SU", "SU5001");
        var fastProvider = new FakeProvider(ProviderId.Duffel, new[] { fastOffer });
        var slowProvider = new SlowProvider(ProviderId.Travelpayouts, Array.Empty<Offer>());

        var criteria = SearchCriteria
            .Create(Led, Dme, new DateOnly(2026, 11, 5), null, 1, CabinClass.Economy, Rub)
            .Value;
        var query = new SearchFlightsQuery(criteria);

        var result = await SearchFlightsHandler.Handle(
            query,
            new IFlightSearchProvider[] { fastProvider, slowProvider },
            _cache,
            PassthroughFx,
            new NoOpMetrics(),
            TimeProvider.System,
            NullLogger<SearchFlightsQuery>.Instance,
            ct
        );

        result.IsError.ShouldBeFalse("a timed-out provider should not fault the whole search");
        result.Value.Offers.Count.ShouldBe(1);
        result.Value.Offers[0].TotalAmount.Amount.ShouldBe(3000m);
        result.Value.PartialFailures.Count.ShouldBe(1);
        result.Value.PartialFailures[0].Provider.ShouldBe(ProviderId.Travelpayouts.Value);
        result.Value.PartialFailures[0].ErrorCode.ShouldBe("Timeout");
    }

    [Fact]
    public async Task Handler_dedups_and_ranks_mixed_list()
    {
        var ct = TestContext.Current.CancellationToken;

        // Same flight from both providers → deduped; cheaper one wins
        var bookable = BuildBookable(5000m, "SU", "SU6001");
        var deeplink = BuildDeeplink(4500m, "SU", "SU6001"); // same key → dedup winner (cheaper)
        var unique = BuildBookable(6000m, "S7", "S76002"); // different → kept

        var provider1 = new FakeProvider(ProviderId.Duffel, new Offer[] { bookable, unique });
        var provider2 = new FakeProvider(ProviderId.Travelpayouts, new Offer[] { deeplink });

        var criteria = SearchCriteria
            .Create(Led, Dme, new DateOnly(2026, 11, 10), null, 1, CabinClass.Economy, Rub)
            .Value;
        var query = new SearchFlightsQuery(criteria);

        var result = await SearchFlightsHandler.Handle(
            query,
            new IFlightSearchProvider[] { provider1, provider2 },
            _cache,
            PassthroughFx,
            new NoOpMetrics(),
            TimeProvider.System,
            NullLogger<SearchFlightsQuery>.Instance,
            ct
        );

        result.IsError.ShouldBeFalse();
        // 3 raw offers → dedup removes the more-expensive duplicate → 2 remain
        result.Value.Offers.Count.ShouldBe(2);
        // Ranked cheapest-first: 4500 < 6000
        result.Value.Offers[0].TotalAmount.Amount.ShouldBe(4500m);
        result.Value.Offers[1].TotalAmount.Amount.ShouldBe(6000m);
    }

    [Fact]
    public async Task Search_cache_entry_has_five_minute_ttl()
    {
        var ct = TestContext.Current.CancellationToken;

        var offer = BuildBookable(2500m, "SU", "SU7001");
        var provider = new FakeProvider(ProviderId.Duffel, new[] { offer });

        var criteria = SearchCriteria
            .Create(Led, Dme, new DateOnly(2026, 11, 20), null, 1, CabinClass.Economy, Rub)
            .Value;
        var query = new SearchFlightsQuery(criteria);

        await SearchFlightsHandler.Handle(
            query,
            new[] { provider },
            _cache,
            PassthroughFx,
            new NoOpMetrics(),
            TimeProvider.System,
            NullLogger<SearchFlightsQuery>.Instance,
            ct
        );

        // Inspect the TTL that was written to Redis
        var key = SearchCacheKey.Build(criteria);
        var db = _redis.GetDatabase();
        var ttl = await db.KeyTimeToLiveAsync(key);

        ttl.ShouldNotBeNull("cache key should exist after a successful search");
        ttl!.Value.TotalSeconds.ShouldBeGreaterThan(4 * 60, "TTL must be close to 5 minutes");
        ttl.Value.TotalSeconds.ShouldBeLessThanOrEqualTo(
            5 * 60 + 5,
            "TTL must not exceed 5 minutes by more than a few seconds"
        );
    }
}
