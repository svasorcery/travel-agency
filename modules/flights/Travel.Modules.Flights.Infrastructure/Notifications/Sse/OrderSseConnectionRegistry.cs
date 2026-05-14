using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Travel.Modules.Flights.Application.Notifications;
using Travel.Modules.Flights.Infrastructure.Persistence;

namespace Travel.Modules.Flights.Infrastructure.Notifications.Sse;

/// <summary>
/// Singleton in-process SSE connection registry.
/// Uses <see cref="IServiceScopeFactory"/> to create a short-lived EF scope for
/// owner lookups, avoiding the "scoped service inside singleton" problem.
/// </summary>
public sealed class OrderSseConnectionRegistry(
    IServiceScopeFactory scopeFactory,
    ILogger<OrderSseConnectionRegistry> logger
) : IOrderSseRegistry
{
    private readonly ConcurrentDictionary<Guid, List<Channel<SseEvent>>> _channels = new();

    private readonly object _lock = new();

    public void Register(Guid orderId, Channel<SseEvent> channel)
    {
        var list = _channels.GetOrAdd(orderId, _ => []);
        lock (_lock)
        {
            list.Add(channel);
        }

        logger.LogDebug("SSE channel registered for order {OrderId}.", orderId);
    }

    public void Unregister(Guid orderId, Channel<SseEvent> channel)
    {
        if (!_channels.TryGetValue(orderId, out var list))
            return;

        lock (_lock)
        {
            list.Remove(channel);
        }

        logger.LogDebug("SSE channel unregistered for order {OrderId}.", orderId);
    }

    public void Publish(Guid orderId, SseEvent evt)
    {
        if (!_channels.TryGetValue(orderId, out var list))
            return;

        List<Channel<SseEvent>> snapshot;
        lock (_lock)
        {
            snapshot = [.. list];
        }

        foreach (var ch in snapshot)
        {
            if (!ch.Writer.TryWrite(evt))
            {
                logger.LogDebug(
                    "SSE channel full for order {OrderId} — event dropped (DropOldest configured at channel creation).",
                    orderId
                );
            }
        }
    }

    public async Task<Guid?> LookupOrderOwnerAsync(Guid orderId, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FlightsDbContext>();

        var entity = await db
            .Orders.AsNoTracking()
            .Where(o => o.AggregateId == orderId)
            .Select(o => new { o.UserId })
            .FirstOrDefaultAsync(ct);

        return entity?.UserId;
    }
}
