using System.Text.Json;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Testcontainers.PostgreSql;
using Travel.Modules.Flights.Application.Contracts;
using Travel.Modules.Flights.Application.Handlers.Notifications;
using Travel.Modules.Flights.Application.Notifications;
using Travel.Modules.Flights.Infrastructure.Notifications.Sse;
using Travel.Modules.Flights.Infrastructure.Persistence;
using Travel.Modules.Flights.Infrastructure.Persistence.Entities;
using Xunit;

namespace Travel.Modules.Flights.Tests.Integration.Notifications;

[Trait("Category", "Integration")]
public sealed class SseRegistryTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder(
        "pgvector/pgvector:pg17"
    ).Build();

    private FlightsDbContext _db = default!;
    private IServiceScopeFactory _scopeFactory = default!;
    private OrderSseConnectionRegistry _sut = default!;

    public async ValueTask InitializeAsync()
    {
        await _pg.StartAsync();

        var services = new ServiceCollection();
        services.AddDbContext<FlightsDbContext>(opts =>
            opts.UseNpgsql(_pg.GetConnectionString()).UseSnakeCaseNamingConvention()
        );
        var sp = services.BuildServiceProvider();

        _scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();

        // Ensure schema created
        await using var scope = _scopeFactory.CreateAsyncScope();
        _db = scope.ServiceProvider.GetRequiredService<FlightsDbContext>();
        await _db.Database.EnsureCreatedAsync();

        _sut = new OrderSseConnectionRegistry(
            _scopeFactory,
            NullLogger<OrderSseConnectionRegistry>.Instance
        );
    }

    public async ValueTask DisposeAsync()
    {
        await _db.DisposeAsync();
        await _pg.DisposeAsync();
    }

    // ─── helpers ────────────────────────────────────────────────────────────────

    private async Task<(Guid AggregateId, Guid UserId)> SeedOrderAsync()
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FlightsDbContext>();
        var aggId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        db.Orders.Add(
            new OrderReadModelEntity
            {
                Id = Guid.NewGuid(),
                AggregateId = aggId,
                UserId = userId,
                Status = "Confirmed",
                TotalAmount = 1000m,
                Currency = "RUB",
                ItineraryJson = "{}",
                PassengerInfoJson = "null",
                TicketNumbers = Array.Empty<string>(),
                BookedAt = DateTimeOffset.UtcNow,
            }
        );
        await db.SaveChangesAsync();
        return (aggId, userId);
    }

    private static Channel<SseEvent> MakeBoundedChannel() =>
        Channel.CreateBounded<SseEvent>(
            new BoundedChannelOptions(32) { FullMode = BoundedChannelFullMode.DropOldest }
        );

    private static SseEvent MakeEvent(Guid orderId, string type = "OrderConfirmed") =>
        new(type, orderId, JsonDocument.Parse("{}").RootElement, DateTimeOffset.UtcNow);

    // ─── Registry tests ─────────────────────────────────────────────────────────

    [Fact]
    public void Register_ThenPublish_ChannelReceivesEvent()
    {
        var orderId = Guid.NewGuid();
        var channel = MakeBoundedChannel();

        _sut.Register(orderId, channel);
        _sut.Publish(orderId, MakeEvent(orderId));

        channel.Reader.TryRead(out var received).ShouldBeTrue();
        received.ShouldNotBeNull();
        received!.OrderId.ShouldBe(orderId);
        received.Type.ShouldBe("OrderConfirmed");
    }

    [Fact]
    public void Unregister_ThenPublish_ChannelDoesNotReceiveEvent()
    {
        var orderId = Guid.NewGuid();
        var channel = MakeBoundedChannel();

        _sut.Register(orderId, channel);
        _sut.Unregister(orderId, channel);
        _sut.Publish(orderId, MakeEvent(orderId));

        channel.Reader.TryRead(out _).ShouldBeFalse();
    }

    [Fact]
    public void Publish_WithNoRegisteredChannels_DoesNotThrow()
    {
        // Should be a no-op
        var act = () => _sut.Publish(Guid.NewGuid(), MakeEvent(Guid.NewGuid()));
        act.ShouldNotThrow();
    }

    [Fact]
    public async Task LookupOrderOwnerAsync_KnownOrder_ReturnsUserId()
    {
        var ct = TestContext.Current.CancellationToken;
        var (aggId, userId) = await SeedOrderAsync();

        var result = await _sut.LookupOrderOwnerAsync(aggId, ct);

        result.ShouldBe(userId);
    }

    [Fact]
    public async Task LookupOrderOwnerAsync_UnknownOrder_ReturnsNull()
    {
        var ct = TestContext.Current.CancellationToken;

        var result = await _sut.LookupOrderOwnerAsync(Guid.NewGuid(), ct);

        result.ShouldBeNull();
    }
}

/// <summary>Unit tests for PublishOrderSseHandler — no containers needed.</summary>
public sealed class PublishOrderSseHandlerUnitTests
{
    private readonly SseHandlerFakeRegistry _registry = new();
    private readonly TimeProvider _time = TimeProvider.System;

    [Fact]
    public void Handle_OrderConfirmed_PublishesOrderConfirmedEvent()
    {
        var aggId = Guid.NewGuid();
        PublishOrderSseHandler.Handle(
            new OrderConfirmedNotification(aggId, Guid.NewGuid()),
            _registry,
            _time
        );

        _registry.Published.Count.ShouldBe(1);
        _registry.Published[0].Type.ShouldBe("OrderConfirmed");
        _registry.Published[0].OrderId.ShouldBe(aggId);
    }

    [Fact]
    public void Handle_OrderTicketed_PublishesOrderTicketedEvent()
    {
        var aggId = Guid.NewGuid();
        PublishOrderSseHandler.Handle(
            new OrderTicketedNotification(aggId, Guid.NewGuid()),
            _registry,
            _time
        );

        _registry.Published.Count.ShouldBe(1);
        _registry.Published[0].Type.ShouldBe("OrderTicketed");
        _registry.Published[0].OrderId.ShouldBe(aggId);
    }

    [Fact]
    public void Handle_OrderCancelled_PublishesOrderCancelledEvent()
    {
        var aggId = Guid.NewGuid();
        PublishOrderSseHandler.Handle(
            new OrderCancelledNotification(aggId, Guid.NewGuid()),
            _registry,
            _time
        );

        _registry.Published.Count.ShouldBe(1);
        _registry.Published[0].Type.ShouldBe("OrderCancelled");
        _registry.Published[0].OrderId.ShouldBe(aggId);
    }
}

internal sealed class SseHandlerFakeRegistry : IOrderSseRegistry
{
    public List<SseEvent> Published { get; } = [];

    public void Register(Guid orderId, Channel<SseEvent> channel) { }

    public void Unregister(Guid orderId, Channel<SseEvent> channel) { }

    public void Publish(Guid orderId, SseEvent evt) => Published.Add(evt);

    public Task<Guid?> LookupOrderOwnerAsync(Guid orderId, CancellationToken ct) =>
        Task.FromResult<Guid?>(null);
}
