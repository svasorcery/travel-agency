using Microsoft.EntityFrameworkCore;
using Shouldly;
using Testcontainers.PostgreSql;
using Travel.Modules.Flights.Application.Queries;
using Travel.Modules.Flights.Infrastructure.Persistence;
using Travel.Modules.Flights.Infrastructure.Persistence.Entities;
using Xunit;

namespace Travel.Modules.Flights.Tests.Integration.Booking;

[Trait("Category", "Integration")]
public sealed class OrderQueriesTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder(
        "pgvector/pgvector:pg17"
    ).Build();

    private FlightsDbContext _db = default!;
    private OrderReadModelQueries _sut = default!;

    public async ValueTask InitializeAsync()
    {
        await _pg.StartAsync();

        var efOptions = new DbContextOptionsBuilder<FlightsDbContext>()
            .UseNpgsql(_pg.GetConnectionString())
            .UseSnakeCaseNamingConvention()
            .Options;

        _db = new FlightsDbContext(efOptions);
        await _db.Database.EnsureCreatedAsync();

        _sut = new OrderReadModelQueries(_db);
    }

    public async ValueTask DisposeAsync()
    {
        await _db.DisposeAsync();
        await _pg.DisposeAsync();
    }

    // ─── helpers ───────────────────────────────────────────────────────────────

    private static OrderReadModelEntity BuildOrder(
        Guid aggregateId,
        Guid userId,
        DateTimeOffset? bookedAt = null
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            AggregateId = aggregateId,
            UserId = userId,
            ProviderOrderId = "ord_" + Guid.NewGuid().ToString("N")[..8],
            Status = "Confirmed",
            TotalAmount = 5000m,
            Currency = "RUB",
            ItineraryJson = "{}",
            PassengerInfoJson = "null",
            TicketNumbers = Array.Empty<string>(),
            BookedAt = bookedAt ?? DateTimeOffset.UtcNow,
        };

    private async Task SeedAsync(params OrderReadModelEntity[] entities)
    {
        _db.Orders.AddRange(entities);
        await _db.SaveChangesAsync();
    }

    // ─── GetAsync ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAsync_ReturnsMatchingOrder_ForCorrectUser()
    {
        var ct = TestContext.Current.CancellationToken;
        var aggregateId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        await SeedAsync(BuildOrder(aggregateId, userId));

        var result = await _sut.GetAsync(aggregateId, userId, ct);

        result.ShouldNotBeNull();
        result.AggregateId.ShouldBe(aggregateId);
        result.UserId.ShouldBe(userId);
        result.Status.ShouldBe("Confirmed");
    }

    [Fact]
    public async Task GetAsync_ReturnsNull_WhenOrderBelongsToDifferentUser()
    {
        var ct = TestContext.Current.CancellationToken;
        var aggregateId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var callerId = Guid.NewGuid();

        await SeedAsync(BuildOrder(aggregateId, ownerId));

        var result = await _sut.GetAsync(aggregateId, callerId, ct);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task GetAsync_ReturnsNull_ForNonExistentAggregateId()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();

        var result = await _sut.GetAsync(Guid.NewGuid(), userId, ct);

        result.ShouldBeNull();
    }

    // ─── ListAsync ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task ListAsync_ReturnsOnlyCallersOrders_ExcludesOtherUsers()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        var otherId = Guid.NewGuid();

        var now = DateTimeOffset.UtcNow;
        await SeedAsync(
            BuildOrder(Guid.NewGuid(), userId, now),
            BuildOrder(Guid.NewGuid(), userId, now.AddMinutes(-1)),
            BuildOrder(Guid.NewGuid(), otherId, now)
        );

        var result = await _sut.ListAsync(userId, 50, 0, ct);

        result.Items.Count.ShouldBe(2);
        result.Items.ShouldAllBe(o => o.UserId == userId);
    }

    [Fact]
    public async Task ListAsync_ReturnsOrdersNewestFirst()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();

        var now = DateTimeOffset.UtcNow;
        var older = BuildOrder(Guid.NewGuid(), userId, now.AddHours(-2));
        var newest = BuildOrder(Guid.NewGuid(), userId, now);
        var middle = BuildOrder(Guid.NewGuid(), userId, now.AddHours(-1));
        await SeedAsync(older, newest, middle);

        var result = await _sut.ListAsync(userId, 50, 0, ct);

        result.Items.Count.ShouldBe(3);
        result.Items[0].BookedAt.ShouldBe(newest.BookedAt);
        result.Items[1].BookedAt.ShouldBe(middle.BookedAt);
        result.Items[2].BookedAt.ShouldBe(older.BookedAt);
    }

    [Fact]
    public async Task ListAsync_HonoursLimit()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();

        var now = DateTimeOffset.UtcNow;
        await SeedAsync(
            BuildOrder(Guid.NewGuid(), userId, now),
            BuildOrder(Guid.NewGuid(), userId, now.AddMinutes(-1)),
            BuildOrder(Guid.NewGuid(), userId, now.AddMinutes(-2))
        );

        var result = await _sut.ListAsync(userId, 2, 0, ct);

        result.Items.Count.ShouldBe(2);
        result.Limit.ShouldBe(2);
    }

    [Fact]
    public async Task ListAsync_HonoursOffset()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();

        var now = DateTimeOffset.UtcNow;
        var first = BuildOrder(Guid.NewGuid(), userId, now);
        var second = BuildOrder(Guid.NewGuid(), userId, now.AddMinutes(-1));
        var third = BuildOrder(Guid.NewGuid(), userId, now.AddMinutes(-2));
        await SeedAsync(first, second, third);

        // skip the first (newest) row
        var result = await _sut.ListAsync(userId, 50, 1, ct);

        result.Items.Count.ShouldBe(2);
        result.Offset.ShouldBe(1);
        result.Items[0].AggregateId.ShouldBe(second.AggregateId);
        result.Items[1].AggregateId.ShouldBe(third.AggregateId);
    }

    [Fact]
    public async Task ListAsync_ClampsLimitToMax200()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();

        var result = await _sut.ListAsync(userId, 9999, 0, ct);

        result.Limit.ShouldBe(200);
        result.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task ListAsync_ClampsLimitToMin1()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();

        var result = await _sut.ListAsync(userId, 0, 0, ct);

        result.Limit.ShouldBe(1);
    }
}
