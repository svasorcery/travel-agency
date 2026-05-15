using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Travel.Modules.Flights.Application.Notifications;
using Travel.Modules.Flights.Infrastructure.Notifications.Sse;
using Xunit;

namespace Travel.Modules.Flights.Tests.Unit.Notifications;

/// <summary>
/// Unit tests for SSE backpressure: a slow consumer exceeding 1 MB should be disconnected;
/// the registry should clean up its entry when the last connection unregisters.
/// No containers needed — all in-memory.
/// </summary>
public sealed class SseBackpressureTests
{
    private static SseEvent MakeEvent(Guid orderId, int payloadBytes = 100)
    {
        var payload = new string('x', payloadBytes);
        using var doc = JsonDocument.Parse($"\"{payload}\"");
        return new SseEvent(
            "OrderConfirmed",
            orderId,
            doc.RootElement.Clone(),
            DateTimeOffset.UtcNow
        );
    }

    private static OrderSseConnectionRegistry MakeSut()
    {
        // No DB needed for these tests — LookupOrderOwnerAsync is not exercised.
        var services = new ServiceCollection();
        // Provide a minimal IServiceScopeFactory so the registry can be constructed.
        var sp = services.BuildServiceProvider();
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
        return new OrderSseConnectionRegistry(
            scopeFactory,
            NullLogger<OrderSseConnectionRegistry>.Instance
        );
    }

    [Fact]
    public void Slow_consumer_exceeding_1mb_is_disconnected()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var sut = MakeSut();

        // Use an unbounded channel so TryWrite never drops.
        var channel = Channel.CreateUnbounded<SseEvent>();
        sut.Register(orderId, channel);

        // Calculate how many events to push past 1 MB.
        // SseEvent JSON is roughly: type(~20) + orderId(~40) + payload + at(~30) = ~200 + payloadBytes.
        // Use 2 KB payloads → each event ~2 KB in serialized form.
        // 1 MB / 2 KB = 512 events. Push 520 to be safely over.
        const int payloadBytes = 2048;
        const int eventCount = 520;

        // Act — publish enough events to exceed 1 MB
        for (var i = 0; i < eventCount; i++)
            sut.Publish(orderId, MakeEvent(orderId, payloadBytes));

        // Assert — the channel writer should be completed (disconnected) after exceeding 1 MB.
        // We do NOT call TryComplete ourselves; the registry should have done it.
        // After TryComplete(), no further writes are accepted.
        var canStillWrite = channel.Writer.TryWrite(MakeEvent(orderId, 1));
        canStillWrite.ShouldBeFalse(
            "The channel should be completed (consumer disconnected) after exceeding 1 MB buffer — no further writes should be accepted."
        );
    }

    [Fact]
    public void Registry_removes_empty_order_entry()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var sut = MakeSut();
        var ch1 = Channel.CreateUnbounded<SseEvent>();
        var ch2 = Channel.CreateUnbounded<SseEvent>();

        sut.Register(orderId, ch1);
        sut.Register(orderId, ch2);

        // Act
        sut.Unregister(orderId, ch1);
        sut.Unregister(orderId, ch2);

        // After the last connection is removed, the registry should clean up the entry.
        // Publish to the order — should be a no-op (no channels, no exceptions).
        var act = () => sut.Publish(orderId, MakeEvent(orderId));
        act.ShouldNotThrow();

        // The channel that was removed should not receive any events.
        ch1.Reader.TryRead(out _).ShouldBeFalse();
        ch2.Reader.TryRead(out _).ShouldBeFalse();
    }
}
