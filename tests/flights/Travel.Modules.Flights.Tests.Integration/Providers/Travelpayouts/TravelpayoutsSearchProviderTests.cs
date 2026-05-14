using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Travel.Modules.Flights.Core.Errors;
using Travel.Modules.Flights.Core.ValueObjects;
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
        var http = new HttpClient { BaseAddress = new Uri(_server.Url!) };
        var tpClient = new TravelpayoutsClient(http, opts);
        var deeplink = new TravelpayoutsDeeplinkBuilder(opts);
        var cacheRepo = new DeeplinkOfferCacheRepository(_db, _time);

        _sut = new TravelpayoutsSearchProvider(
            tpClient,
            opts,
            deeplink,
            cacheRepo,
            _time,
            NullLogger<TravelpayoutsSearchProvider>.Instance
        );
    }

    protected override async ValueTask OnDisposingAsync()
    {
        await _db.DisposeAsync();
    }

    public void Dispose() => _server.Stop();

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

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
    public async Task SearchAsync_SecondCallSameCriteria_ServedFromCache()
    {
        StubTwoEntries();
        var ct = TestContext.Current.CancellationToken;
        var criteria = BuildCriteria();

        // First call — hits WireMock
        var first = await _sut.SearchAsync(criteria, ct);
        first.IsError.ShouldBeFalse();
        first.Value.Count.ShouldBe(2);

        // Second call — should be served from cache (WireMock still gets only 1 hit)
        var second = await _sut.SearchAsync(criteria, ct);
        second.IsError.ShouldBeFalse();
        second.Value.Count.ShouldBe(2);

        _server
            .LogEntries.Count(e => e.RequestMessage.Path?.Contains("prices_for_dates") == true)
            .ShouldBe(1);
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
}
