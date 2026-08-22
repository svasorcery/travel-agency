using System.Threading.Channels;
using Travel.Modules.Flights.Application.Notifications;

namespace Travel.Host.Tests.Integration.Flights;

public sealed class FakeOrderSseRegistry : IOrderSseRegistry
{
    private int _lookupCount;

    public int LookupCount => Volatile.Read(ref _lookupCount);

    public void Reset() => Volatile.Write(ref _lookupCount, 0);

    public void Register(Guid orderId, Channel<SseEvent> channel) { }

    public void Unregister(Guid orderId, Channel<SseEvent> channel) { }

    public void Publish(Guid orderId, SseEvent evt) { }

    public Task<Guid?> LookupOrderOwnerAsync(Guid orderId, CancellationToken ct)
    {
        Interlocked.Increment(ref _lookupCount);
        return Task.FromResult<Guid?>(null);
    }

    public void RecordBytesConsumed(Channel<SseEvent> channel, long bytes) { }
}
