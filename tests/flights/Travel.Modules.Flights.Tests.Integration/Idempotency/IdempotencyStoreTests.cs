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

        var result = await _store.TryGetAsync(key, userId, route, "bHash", ct);

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
        var expired = await _store.TryGetAsync(expiredKey, userId, "/route", "bHash1", ct);
        expired.ShouldBeNull();

        // Fresh record should still exist
        var fresh = await _store.TryGetAsync(freshKey, userId, "/route", "bHash2", ct);
        fresh.ShouldNotBeNull();
    }

    [Fact]
    public async Task TryBegin_treats_expired_inflight_row_as_absent()
    {
        // Arrange: seed an in-flight row (ResponseHash = null) that is already expired.
        var ct = TestContext.Current.CancellationToken;
        var key = new IdempotencyKey("key-expired-inflight");
        var userId = Guid.NewGuid();
        const string route = "/api/bookings";
        const string originalBodyHash = "original-body-hash";
        const string newBodyHash = "new-body-hash";

        // Start with time at T0, create an in-flight row.
        var startTime = DateTimeOffset.UtcNow;
        _time.SetUtcNow(startTime);
        var beginResult = await _store.TryBeginAsync(key, userId, route, originalBodyHash, ct);
        beginResult.Outcome.ShouldBe(BeginOutcome.Started);

        // Advance time past the 24-hour TTL so the row is expired.
        _time.Advance(TimeSpan.FromHours(25));

        // Act: call TryBeginAsync again with the same key but a new body hash.
        var result = await _store.TryBeginAsync(key, userId, route, newBodyHash, ct);

        // Assert: expired in-flight row is treated as absent — the new request should start.
        result.Outcome.ShouldBe(
            BeginOutcome.Started,
            "an expired in-flight row must be treated as absent so the request can proceed"
        );
        result.Replay.ShouldBeNull();

        // The row in the DB must now belong to the new request (new body hash, null ResponseHash).
        var now = _time.GetUtcNow();
        var freshRow = await _db
            .IdempotencyKeys.AsNoTracking()
            .Where(x => x.Key == key.Value && x.UserId == userId && x.Route == route)
            .FirstOrDefaultAsync(ct);
        freshRow.ShouldNotBeNull();
        freshRow!.BodyHash.ShouldBe(newBodyHash, "the row must now belong to the new request");
        freshRow.ResponseHash.ShouldBeNull("the new row must still be in-flight");
        freshRow.ExpiresAt.ShouldBeGreaterThan(now, "the new row must have a fresh TTL");
    }
}
