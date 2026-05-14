using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Travel.Modules.Flights.Application.Idempotency;
using Travel.Modules.Flights.Infrastructure.Persistence;
using Travel.Modules.Flights.Infrastructure.Persistence.Repositories;
using Travel.Shared.TestInfrastructure;
using Xunit;

namespace Travel.Modules.Flights.Tests.Integration.Idempotency;

[Trait("Category", "Integration")]
public sealed class IdempotencyStoreTests : IntegrationTestBase
{
    private FlightsDbContext _db = default!;
    private FakeTimeProvider _time = default!;
    private IdempotencyStore _store = default!;

    protected override async ValueTask OnInitializedAsync()
    {
        var opts = new DbContextOptionsBuilder<FlightsDbContext>()
            .UseNpgsql(ConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
        _db = new FlightsDbContext(opts);
        await _db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        _time = new FakeTimeProvider();
        _store = new IdempotencyStore(_db, _time);
    }

    protected override async ValueTask OnDisposingAsync()
    {
        await _db.DisposeAsync();
    }

    [Fact]
    public async Task Save_then_TryGet_returns_record()
    {
        var ct = TestContext.Current.CancellationToken;
        var key = new IdempotencyKey("key-save-get");
        var userId = Guid.NewGuid();
        const string route = "/api/bookings";

        await _store.SaveAsync(key, userId, route, "bHash", "rHash", 201, """{"id":"x"}""", ct);

        var result = await _store.TryGetAsync(key, userId, route, ct);

        result.ShouldNotBeNull();
        result!.ResponseHash.ShouldBe("rHash");
        result.ResponseStatus.ShouldBe(201);
        result.ResponseBody.ShouldBe("""{"id":"x"}""");
    }

    [Fact]
    public async Task CheckOrConflict_returns_Success_when_no_prior_record()
    {
        var ct = TestContext.Current.CancellationToken;
        var key = new IdempotencyKey("key-no-prior");
        var userId = Guid.NewGuid();

        var result = await _store.CheckOrConflictAsync(key, userId, "/api/bookings", "bHash", ct);

        result.IsError.ShouldBeFalse();
    }

    [Fact]
    public async Task CheckOrConflict_returns_Success_when_same_bodyHash()
    {
        var ct = TestContext.Current.CancellationToken;
        var key = new IdempotencyKey("key-same-hash");
        var userId = Guid.NewGuid();
        const string route = "/api/bookings";
        const string bodyHash = "same-body";

        await _store.SaveAsync(key, userId, route, bodyHash, "rHash", 201, "{}", ct);

        var result = await _store.CheckOrConflictAsync(key, userId, route, bodyHash, ct);

        result.IsError.ShouldBeFalse();
    }

    [Fact]
    public async Task CheckOrConflict_returns_error_when_different_bodyHash()
    {
        var ct = TestContext.Current.CancellationToken;
        var key = new IdempotencyKey("key-diff-hash");
        var userId = Guid.NewGuid();
        const string route = "/api/bookings";

        await _store.SaveAsync(key, userId, route, "original-hash", "rHash", 201, "{}", ct);

        var result = await _store.CheckOrConflictAsync(key, userId, route, "different-hash", ct);

        result.IsError.ShouldBeTrue();
        result.FirstError.Code.ShouldBe("Flights.IdempotencyConflict");
    }

    [Fact]
    public async Task PurgeExpired_deletes_only_expired_rows()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();

        // Save a record that will expire
        var expiredKey = new IdempotencyKey("key-expired");
        await _store.SaveAsync(expiredKey, userId, "/route", "bHash1", "rHash1", 200, "{}", ct);

        // Advance time past 24-hour TTL
        _time.Advance(TimeSpan.FromHours(25));

        // Save a fresh record (not expired)
        var freshKey = new IdempotencyKey("key-fresh");
        await _store.SaveAsync(freshKey, userId, "/route", "bHash2", "rHash2", 200, "{}", ct);

        await _store.PurgeExpiredAsync(ct);

        // Expired record should be gone
        var expired = await _store.TryGetAsync(expiredKey, userId, "/route", ct);
        expired.ShouldBeNull();

        // Fresh record should still exist
        var fresh = await _store.TryGetAsync(freshKey, userId, "/route", ct);
        fresh.ShouldNotBeNull();
    }
}
