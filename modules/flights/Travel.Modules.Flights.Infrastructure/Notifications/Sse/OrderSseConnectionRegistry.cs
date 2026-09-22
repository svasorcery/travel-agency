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

    private sealed class Connection(Channel<SseEvent> channel)
    {
        public Channel<SseEvent> Channel { get; } = channel;
        public long BufferedBytes { get; set; }
        public long LastVersion { get; set; }
        public bool Closed { get; set; }
    }

    private readonly Dictionary<Guid, List<Connection>> _orders = [];
    private readonly Dictionary<Channel<SseEvent>, Connection> _connections = [];
    private readonly object _lock = new();

    public void Register(Guid orderId, Channel<SseEvent> channel)
    {
        lock (_lock)
        {
            var connection = new Connection(channel);
            if (!_orders.TryGetValue(orderId, out var list))
                _orders.Add(orderId, list = []);
            list.Add(connection);
            _connections.Add(channel, connection);
        }
    }

    public void Unregister(Guid orderId, Channel<SseEvent> channel)
    {
        lock (_lock)
        {
            if (!_connections.Remove(channel, out var connection))
                return;
            lock (connection)
            {
                connection.Closed = true;
                connection.BufferedBytes = 0;
                channel.Writer.TryComplete();
            }
            if (_orders.TryGetValue(orderId, out var list))
            {
                list.Remove(connection);
                if (list.Count == 0)
                    _orders.Remove(orderId);
            }
        }
    }

    public void Publish(Guid orderId, SseEvent evt)
    {
        Connection[] snapshot;
        lock (_lock)
            snapshot = _orders.TryGetValue(orderId, out var list) ? [.. list] : [];
        var bytes = EstimateBytes(evt);
        foreach (var connection in snapshot)
        {
            lock (connection)
            {
                if (connection.Closed || evt.StreamVersion <= connection.LastVersion)
                    continue;
                if (
                    connection.BufferedBytes + bytes > MaxBufferedBytes
                    || !connection.Channel.Writer.TryWrite(evt)
                )
                {
                    connection.Closed = true;
                    connection.Channel.Writer.TryComplete();
                    continue;
                }
                connection.BufferedBytes += bytes;
                connection.LastVersion = evt.StreamVersion;
            }
        }
    }

    public void RecordBytesConsumed(Channel<SseEvent> channel, long bytes)
    {
        Connection? connection;
        lock (_lock)
            _connections.TryGetValue(channel, out connection);
        if (connection is null)
            return;
        lock (connection)
            connection.BufferedBytes = Math.Max(0, connection.BufferedBytes - Math.Max(0, bytes));
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
            return 200 + System.Text.Encoding.UTF8.GetByteCount(evt.Payload.GetRawText());
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
