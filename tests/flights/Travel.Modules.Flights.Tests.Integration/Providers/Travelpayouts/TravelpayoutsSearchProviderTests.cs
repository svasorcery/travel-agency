using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Travel.Modules.Flights.Application;
using Travel.Modules.Flights.Application.Search;
using Travel.Modules.Flights.Core.Errors;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Travel.Modules.Flights.Core.ValueObjects.Offer;
using Travel.Modules.Flights.Infrastructure.Persistence;
using Travel.Modules.Flights.Infrastructure.Persistence.Repositories;
using Travel.Modules.Flights.Infrastructure.Providers.Travelpayouts;
using Travel.Shared.TestInfrastructure;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace Travel.Modules.Flights.Tests.Integration.Providers.Travelpayouts;

[Trait("Category", "Integration")]
public sealed class TravelpayoutsSearchProviderTests : IntegrationTestBase, IDisposable
{
    private readonly WireMockServer _server;
    private FlightsDbContext _db = default!;
    private TravelpayoutsSearchProvider _sut = default!;
    private FakeTimeProvider _time = default!;
    private TravelpayoutsOptions _tpOpts = default!;

    public TravelpayoutsSearchProviderTests()
    {
        _server = WireMockServer.Start();
    }

    protected override async ValueTask OnInitializedAsync()
    {
        var dbOpts = new DbContextOptionsBuilder<FlightsDbContext>()
            .UseNpgsql(ConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
        _db = new FlightsDbContext(dbOpts);
        await _db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);

        _time = new FakeTimeProvider(new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero));

        _tpOpts = new TravelpayoutsOptions
        {
            BaseUrl = _server.Url!,
            ApiVersion = "v3",
            ApiToken = "test_token",
            PartnerMarker = "test_marker_42",
        };

        var opts = Options.Create(_tpOpts);
        var featureFlags = new FlightsFeatureFlags();
        _sut = BuildSut(opts, featureFlags);
    }

    private TravelpayoutsSearchProvider BuildSut(
        IOptions<TravelpayoutsOptions> opts,
        FlightsFeatureFlags featureFlags
    )
    {
        var http = new HttpClient { BaseAddress = new Uri(_server.Url!) };
        var tpClient = new TravelpayoutsClient(http, opts);
        var deeplink = new TravelpayoutsDeeplinkBuilder(opts);
        var cacheRepo = new DeeplinkOfferCacheRepository(_db, _time);

        return new TravelpayoutsSearchProvider(
            tpClient,
            opts,
            deeplink,
            cacheRepo,
            _time,
            new FakeOptionsMonitor<FlightsFeatureFlags>(featureFlags),
            NullLogger<TravelpayoutsSearchProvider>.Instance
        );
    }

    protected override async ValueTask OnDisposingAsync()
    {
        await _db.DisposeAsync();
    }

    public void Dispose() => _server.Stop();

    // -------------------------------------------------------------------------
    // Helpers / fakes
    // -------------------------------------------------------------------------

    private sealed class FakeOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;

        public T Get(string? name) => value;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private static SearchCriteria BuildCriteria() =>
        SearchCriteria
            .Create(
                IataCode.Create("LED").Value,
                IataCode.Create("DME").Value,
                new DateOnly(2026, 7, 15),
                returnDate: null,
                passengerCount: 1,
                CabinClass.Economy,
                CurrencyCode.Create("RUB").Value
            )
            .Value;

    private const string TwoEntryResponse = """
        {
          "success": true,
          "data": [
            {
              "origin": "LED",
              "destination": "DME",
              "price": 5420,
              "airline": "SU",
              "flight_number": "100",
              "departure_at": "2026-07-15T09:20:00+03:00",
              "return_at": null,
              "transfers": 0,
              "duration": 200,
              "link": "/search/LED1507DME1?token=test"
            },
            {
              "origin": "LED",
              "destination": "DME",
              "price": 6100,
              "airline": "U6",
              "flight_number": "201",
              "departure_at": "2026-07-15T14:45:00+03:00",
              "return_at": null,
              "transfers": 0,
              "duration": 180,
              "link": "/search/LED1507DME2?token=test"
            }
          ],
          "currency": "rub"
        }
        """;

    private void StubTwoEntries()
    {
        _server
            .Given(Request.Create().WithPath("/aviasales/v3/prices_for_dates").UsingGet())
            .RespondWith(
                Response
                    .Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody(TwoEntryResponse)
            );
    }

    // -------------------------------------------------------------------------
    // Tests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SearchAsync_TwoEntries_ReturnsTwoDeeplinkOffers()
    {
        StubTwoEntries();
        var ct = TestContext.Current.CancellationToken;

        var result = await _sut.SearchAsync(BuildCriteria(), ct);

        result.IsError.ShouldBeFalse();
        result.Value.Count.ShouldBe(2);

        foreach (var offer in result.Value)
        {
            var dl = offer.ShouldBeOfType<DeeplinkOffer>();
            dl.DeeplinkUrl.ToString().ShouldContain("test_marker_42");
            dl.PartnerName.ShouldBe("Aviasales");
        }
    }

    [Fact]
    public async Task SearchAsync_SecondCallSameCriteria_AlwaysCallsApi()
    {
        // Deeplink cache is audit-only; every call goes to the API
        StubTwoEntries();
        var ct = TestContext.Current.CancellationToken;
        var criteria = BuildCriteria();

        var first = await _sut.SearchAsync(criteria, ct);
        first.IsError.ShouldBeFalse();
        first.Value.Count.ShouldBe(2);

        var second = await _sut.SearchAsync(criteria, ct);
        second.IsError.ShouldBeFalse();
        second.Value.Count.ShouldBe(2);

        // Both calls hit the real API (deeplink cache is write-only / audit)
        _server
            .LogEntries.Count(e => e.RequestMessage?.Path?.Contains("prices_for_dates") == true)
            .ShouldBe(2);
    }

    [Fact]
    public async Task SearchAsync_500Response_ReturnsProviderUnavailable()
    {
        _server
            .Given(Request.Create().WithPath("/aviasales/v3/prices_for_dates").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(500));

        var ct = TestContext.Current.CancellationToken;
        var result = await _sut.SearchAsync(BuildCriteria(), ct);

        result.IsError.ShouldBeTrue();
        result.FirstError.Code.ShouldBe(FlightsErrors.ProviderUnavailable("Travelpayouts").Code);
    }

    [Fact]
    public async Task Disabled_travelpayouts_returns_empty_not_failure()
    {
        // With Travelpayouts disabled, SearchAsync should return an empty success (not a failure)
        // so the partial_failure[] is not polluted with a deliberate disable.
        var ct = TestContext.Current.CancellationToken;
        var disabledFlags = new FlightsFeatureFlags
        {
            Travelpayouts = new FlightsFeatureFlags.ProviderFlag { Enabled = false },
        };
        var disabledSut = BuildSut(Options.Create(_tpOpts), disabledFlags);

        // Stub so that if the provider (wrongly) calls the API it returns something
        StubTwoEntries();

        var result = await disabledSut.SearchAsync(BuildCriteria(), ct);

        result.IsError.ShouldBeFalse("a disabled provider must return success (empty list)");
        result.Value.Count.ShouldBe(0, "disabled provider must return empty list");
        _server
            .LogEntries.Count(e => e.RequestMessage?.Path?.Contains("prices_for_dates") == true)
            .ShouldBe(0, "disabled provider must not call the API");
    }

    [Fact]
    public async Task Search_does_not_serve_from_deeplink_audit_cache()
    {
        // Seed the deeplink EF cache for this criteria hash
        var criteria = BuildCriteria();
        var hash = SearchCacheKey.Build(criteria);
        var cacheRepo = new DeeplinkOfferCacheRepository(_db, _time);
        var segment = Segment
            .Create(
                IataCode.Create("LED").Value,
                IataCode.Create("DME").Value,
                new DateTimeOffset(2026, 7, 15, 9, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 7, 15, 12, 0, 0, TimeSpan.Zero),
                "SU",
                "SU999",
                CabinClass.Economy
            )
            .Value;
        var cachedOffer = new DeeplinkOffer(
            OfferId.New(),
            Itinerary.Create(new[] { Slice.Create(new[] { segment }).Value }).Value,
            Money.Create(9999m, CurrencyCode.Create("RUB").Value).Value,
            ProviderId.Travelpayouts,
            _time.GetUtcNow(),
            new Uri("https://tp.example.com/cached"),
            "Aviasales"
        );
        await cacheRepo.SetAsync(
            hash,
            new[] { cachedOffer },
            TestContext.Current.CancellationToken
        );

        // Stub WireMock to return 2 fresh offers
        StubTwoEntries();
        var ct = TestContext.Current.CancellationToken;

        var result = await _sut.SearchAsync(criteria, ct);

        // Must call the API (not serve from cache), so WireMock gets 1 hit and returns 2 offers
        result.IsError.ShouldBeFalse();
        result.Value.Count.ShouldBe(2);
        _server
            .LogEntries.Count(e => e.RequestMessage?.Path?.Contains("prices_for_dates") == true)
            .ShouldBe(1, "SearchAsync should always call the API; deeplink cache is audit-only");
    }
}
