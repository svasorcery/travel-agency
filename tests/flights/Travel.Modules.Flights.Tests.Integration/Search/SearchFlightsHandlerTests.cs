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
        string flightNumber = "SU1234"
    ) =>
        new(
            OfferId.New(),
            BuildItinerary(carrier, flightNumber),
            Money.Create(amount, Rub).Value,
            ProviderId.Duffel,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddHours(1),
            new FareConditions(false, false, "YECO", "Economy"),
            "ref-" + Guid.NewGuid()
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

    private sealed class NoOpMetrics : ISearchMetrics
    {
        public void RecordSearchLatency(double elapsedMs, string provider, string status) { }
    }

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
            new NoOpMetrics(),
            TimeProvider.System,
            NullLogger<SearchFlightsQuery>.Instance,
            ct
        );

        result.IsError.ShouldBeTrue();
        result.FirstError.Code.ShouldBe("Flights.ProviderUnavailable");
    }
}
