using System.Threading.Channels;
using Travel.Modules.Flights.Application.Notifications;

namespace Travel.Host.Tests.Integration.Flights;

public sealed class FakeOrderSseRegistry : IOrderSseRegistry
{
    private int _lookupCount;
    public Guid? Owner { get; set; }
    public Action<Channel<SseEvent>>? OnRegister { get; set; }
    public int Unregistered { get; private set; }

    public int LookupCount => Volatile.Read(ref _lookupCount);

    public void Reset()
    {
        Volatile.Write(ref _lookupCount, 0);
        Owner = null;
        OnRegister = null;
        Unregistered = 0;
    }

    public void Register(Guid orderId, Channel<SseEvent> channel) => OnRegister?.Invoke(channel);

    public void Unregister(Guid orderId, Channel<SseEvent> channel) => Unregistered++;

    public void Publish(Guid orderId, SseEvent evt) { }

    public Task<Guid?> LookupOrderOwnerAsync(Guid orderId, CancellationToken ct)
    {
        Interlocked.Increment(ref _lookupCount);
        return Task.FromResult(Owner);
    }

    public void RecordBytesConsumed(Channel<SseEvent> channel, long bytes) { }
}
