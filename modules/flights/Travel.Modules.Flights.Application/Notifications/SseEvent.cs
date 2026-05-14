using System.Text.Json;
using System.Threading.Channels;

namespace Travel.Modules.Flights.Application.Notifications;

public sealed record SseEvent(string Type, Guid OrderId, JsonElement Payload, DateTimeOffset At);

public interface IOrderSseRegistry
{
    void Register(Guid orderId, Channel<SseEvent> channel);
    void Unregister(Guid orderId, Channel<SseEvent> channel);
    void Publish(Guid orderId, SseEvent evt);
    Task<Guid?> LookupOrderOwnerAsync(Guid orderId, CancellationToken ct);
}
