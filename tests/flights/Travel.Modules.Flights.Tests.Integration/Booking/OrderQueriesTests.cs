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
    public async Task ListAsync_UsesAggregateIdToBreakTiesAcrossOwnerScopedPages()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        var bookedAt = DateTimeOffset.Parse("2030-06-01T10:00:00Z");
        var firstId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var secondId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var thirdId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        await SeedAsync(BuildOrder(firstId, userId, bookedAt));
        await SeedAsync(BuildOrder(secondId, userId, bookedAt));
        await SeedAsync(BuildOrder(thirdId, userId, bookedAt));
        await SeedAsync(
            BuildOrder(Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"), Guid.NewGuid(), bookedAt)
        );

        var firstPage = await _sut.ListAsync(userId, 2, 0, ct);
        var secondPage = await _sut.ListAsync(userId, 2, 2, ct);

        firstPage.Items.Select(o => o.AggregateId).ShouldBe([thirdId, secondId]);
        secondPage.Items.Select(o => o.AggregateId).ShouldBe([firstId]);
        var combined = firstPage.Items.Concat(secondPage.Items).ToArray();
        combined.ShouldAllBe(o => o.UserId == userId);
        combined.Select(o => o.AggregateId).Distinct().Count().ShouldBe(3);
        secondPage.Offset.ShouldBe(2);
    }

    [Fact]
    public async Task ListAsync_ReturnsLookaheadAndEmptyPageBeyondTheEnd()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        var bookedAt = DateTimeOffset.Parse("2030-06-01T10:00:00Z");
        await SeedAsync(
            Enumerable
                .Range(0, 22)
                .Select(i => BuildOrder(Guid.NewGuid(), userId, bookedAt.AddMinutes(-i)))
                .ToArray()
        );

        var firstPage = await _sut.ListAsync(userId, 21, 0, ct);
        var pastEnd = await _sut.ListAsync(userId, 21, 40, ct);

        firstPage.Items.Count.ShouldBe(21);
        firstPage.Limit.ShouldBe(21);
        firstPage.Offset.ShouldBe(0);
        pastEnd.Items.ShouldBeEmpty();
        pastEnd.Limit.ShouldBe(21);
        pastEnd.Offset.ShouldBe(40);
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

    [Fact]
    public async Task List_with_negative_offset_does_not_throw()
    {
        // A negative offset must be treated as 0 — no exception, returns first page.
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        await SeedAsync(BuildOrder(Guid.NewGuid(), userId));

        var result = await _sut.ListAsync(userId, 50, -5, ct);

        result.ShouldNotBeNull();
        result.Items.Count.ShouldBe(1);
        result.Offset.ShouldBe(0); // clamped
    }
}
