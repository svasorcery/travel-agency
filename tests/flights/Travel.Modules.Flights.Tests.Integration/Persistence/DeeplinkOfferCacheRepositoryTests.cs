using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Travel.Modules.Flights.Core.ValueObjects.Offer;
using Travel.Modules.Flights.Infrastructure.Persistence;
using Travel.Modules.Flights.Infrastructure.Persistence.Repositories;
using Travel.Shared.TestInfrastructure;
using Xunit;

namespace Travel.Modules.Flights.Tests.Integration.Persistence;

[Trait("Category", "Integration")]
public sealed class DeeplinkOfferCacheRepositoryTests : IntegrationTestBase
{
    private FlightsDbContext _db = default!;
    private FakeTimeProvider _time = default!;
    private DeeplinkOfferCacheRepository _repo = default!;

    protected override async ValueTask OnInitializedAsync()
    {
        var opts = new DbContextOptionsBuilder<FlightsDbContext>()
            .UseNpgsql(ConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
        _db = new FlightsDbContext(opts);
        await _db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        _time = new FakeTimeProvider();
        _repo = new DeeplinkOfferCacheRepository(_db, _time);
    }

    protected override async ValueTask OnDisposingAsync()
    {
        await _db.DisposeAsync();
    }

    private static DeeplinkOffer BuildOffer()
    {
        var origin = IataCode.Create("LED").Value;
        var destination = IataCode.Create("DME").Value;
        var depart = DateTimeOffset.UtcNow.AddDays(7);
        var arrive = depart.AddHours(2);

        var segment = Segment
            .Create(origin, destination, depart, arrive, "SU", "SU100", CabinClass.Economy)
            .Value;

        var slice = Slice.Create([segment]).Value;
        var itinerary = Itinerary.Create([slice]).Value;

        var currency = CurrencyCode.Create("RUB").Value;
        var money = Money.Create(5000m, currency).Value;

        return new DeeplinkOffer(
            Id: OfferId.New(),
            Itinerary: itinerary,
            TotalAmount: money,
            Provider: ProviderId.Travelpayouts,
            FetchedAt: DateTimeOffset.UtcNow,
            DeeplinkUrl: new Uri("https://aviasales.ru/?marker=X"),
            PartnerName: "Aviasales"
        );
    }

    [Fact]
    public async Task Set_then_TryGet_round_trips_offer()
    {
        var ct = TestContext.Current.CancellationToken;
        const string hash = "criteria-hash-roundtrip";
        var offer = BuildOffer();

        await _repo.SetAsync(hash, [offer], ct);
        var result = await _repo.TryGetAsync(hash, ct);

        result.ShouldNotBeNull();
        result!.Count.ShouldBe(1);
        result[0].PartnerName.ShouldBe("Aviasales");
        result[0].DeeplinkUrl.ShouldBe(new Uri("https://aviasales.ru/?marker=X"));
        result[0].TotalAmount.Amount.ShouldBe(5000m);
    }

    [Fact]
    public async Task TryGet_returns_null_for_expired_entry()
    {
        var ct = TestContext.Current.CancellationToken;
        const string hash = "criteria-hash-expired";
        var offer = BuildOffer();

        await _repo.SetAsync(hash, [offer], ct);

        // Advance time past the 1-hour TTL
        _time.Advance(TimeSpan.FromHours(2));

        var result = await _repo.TryGetAsync(hash, ct);
        result.ShouldBeNull();
    }

    [Fact]
    public async Task PurgeExpired_deletes_expired_only()
    {
        var ct = TestContext.Current.CancellationToken;
        const string expiredHash = "criteria-hash-purge-expired";
        const string freshHash = "criteria-hash-purge-fresh";
        var offer = BuildOffer();

        // Save first entry — will expire
        await _repo.SetAsync(expiredHash, [offer], ct);

        // Advance past TTL
        _time.Advance(TimeSpan.FromHours(2));

        // Save second entry — fresh at the advanced time
        await _repo.SetAsync(freshHash, [offer], ct);

        await _repo.PurgeExpiredAsync(ct);

        var expired = await _repo.TryGetAsync(expiredHash, ct);
        expired.ShouldBeNull();

        var fresh = await _repo.TryGetAsync(freshHash, ct);
        fresh.ShouldNotBeNull();
    }
}
