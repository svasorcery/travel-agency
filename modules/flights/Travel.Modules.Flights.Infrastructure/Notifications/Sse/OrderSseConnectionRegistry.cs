using System.Collections.Concurrent;
using System.Text.Json;
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
/// Implements 1 MB per-connection backpressure: if a slow consumer's buffered bytes
/// would exceed the limit, its channel is completed (disconnected) instead of silently
/// dropping events.
/// </summary>
public sealed class OrderSseConnectionRegistry(
    IServiceScopeFactory scopeFactory,
    ILogger<OrderSseConnectionRegistry> logger
) : IOrderSseRegistry
{
    private const long MaxBufferedBytes = 1 * 1024 * 1024; // 1 MB

    private readonly ConcurrentDictionary<Guid, List<Channel<SseEvent>>> _channels = new();

    // Tracks estimated buffered bytes per channel writer instance.
    private readonly ConcurrentDictionary<Channel<SseEvent>, long> _bufferedBytes = new();

    private readonly object _lock = new();

    public void Register(Guid orderId, Channel<SseEvent> channel)
    {
        var list = _channels.GetOrAdd(orderId, _ => []);
        lock (_lock)
        {
            list.Add(channel);
        }

        _bufferedBytes.TryAdd(channel, 0);

        logger.LogDebug("SSE channel registered for order {OrderId}.", orderId);
    }

    public void Unregister(Guid orderId, Channel<SseEvent> channel)
    {
        if (!_channels.TryGetValue(orderId, out var list))
            return;

        lock (_lock)
        {
            list.Remove(channel);
            // Clean up the dictionary key when no connections remain for this order.
            if (list.Count == 0)
                _channels.TryRemove(orderId, out _);
        }

        _bufferedBytes.TryRemove(channel, out _);

        logger.LogDebug("SSE channel unregistered for order {OrderId}.", orderId);
    }

    public void Publish(Guid orderId, SseEvent evt)
    {
        if (!_channels.TryGetValue(orderId, out var list))
            return;

        // Estimate the serialised size of the event so we can track buffered bytes.
        var estimatedBytes = EstimateBytes(evt);

        List<Channel<SseEvent>> snapshot;
        lock (_lock)
        {
            snapshot = [.. list];
        }

        foreach (var ch in snapshot)
        {
            // Check if publishing this event would push the buffer past 1 MB.
            var current = _bufferedBytes.GetOrAdd(ch, 0);
            if (current + estimatedBytes > MaxBufferedBytes)
            {
                logger.LogWarning(
                    "SSE channel for order {OrderId} exceeded 1 MB buffer — disconnecting slow consumer.",
                    orderId
                );
                // Complete (disconnect) the channel instead of silently dropping.
                ch.Writer.TryComplete();
                continue;
            }

            if (ch.Writer.TryWrite(evt))
            {
                // Increment buffered byte count; the endpoint drains and decrements when it reads.
                _bufferedBytes.AddOrUpdate(ch, estimatedBytes, (_, v) => v + estimatedBytes);
            }
            else
            {
                logger.LogDebug("SSE channel full for order {OrderId} — event dropped.", orderId);
            }
        }
    }

    /// <summary>
    /// Called by the SSE endpoint after draining an event to decrement the tracked buffer size.
    /// </summary>
    public void RecordBytesConsumed(Channel<SseEvent> channel, long bytes)
    {
        _bufferedBytes.AddOrUpdate(channel, 0, (_, v) => Math.Max(0, v - bytes));
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

    private long EstimateBytes(SseEvent evt)
    {
        // Rough estimate: type + orderId + payload JSON + timestamp ≈ 200 bytes overhead + payload.
        try
        {
            return 200 + evt.Payload.GetRawText().Length;
        }
        catch (InvalidOperationException ex)
        {
            // JsonElement.GetRawText() can fail if the element has been disposed or is of
            // an unexpected kind. Fall back to a conservative 4 KB estimate so the buffer
            // tracker does not under-count and let slow consumers grow unbounded.
            logger.LogDebug(
                ex,
                "Failed to estimate SSE payload size for event {EventType}; using 4096 byte fallback.",
                evt.Type
            );
            return 4096;
        }
    }
}
