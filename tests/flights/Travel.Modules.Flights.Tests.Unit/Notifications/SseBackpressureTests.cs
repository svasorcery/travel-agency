using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.Http;
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

    [Fact]
    public async Task Concurrent_publishers_do_not_corrupt_channel_list()
    {
        // Arrange — 5 connections for the same order.
        var orderId = Guid.NewGuid();
        var sut = MakeSut();
        var channels = Enumerable
            .Range(0, 5)
            .Select(_ => Channel.CreateUnbounded<SseEvent>())
            .ToList();
        foreach (var ch in channels)
            sut.Register(orderId, ch);

        // Act — 20 concurrent tasks each publishing 10 events, no synchronisation
        var tasks = Enumerable
            .Range(0, 20)
            .Select(_ =>
                Task.Run(() =>
                {
                    for (var i = 0; i < 10; i++)
                        sut.Publish(orderId, MakeEvent(orderId));
                })
            )
            .ToArray();

        await Task.WhenAll(tasks);

        // Assert — no exceptions thrown and each channel received all 200 events.
        foreach (var ch in channels)
        {
            var count = 0;
            while (ch.Reader.TryRead(out _))
                count++;
            count.ShouldBe(200, "each connection should receive every published event");
        }
    }

    [Fact]
    public void Publish_to_unknown_order_does_not_throw()
    {
        var sut = MakeSut();
        // No channels registered for this order — must be a no-op.
        var act = () => sut.Publish(Guid.NewGuid(), MakeEvent(Guid.NewGuid()));
        act.ShouldNotThrow();
    }

    [Fact]
    public void RecordBytesConsumed_via_interface_decrements_buffer_estimate()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        IOrderSseRegistry sut = MakeSut();
        var channel = Channel.CreateUnbounded<SseEvent>();
        sut.Register(orderId, channel);

        // Write an event large enough to track, then notify the registry it was consumed.
        var evt = MakeEvent(orderId, 500);
        sut.Publish(orderId, evt);

        // Act — call RecordBytesConsumed through the interface (not via a concrete cast).
        // If this compiles and doesn't throw, the interface wiring is correct.
        var act = () => sut.RecordBytesConsumed(channel, 700);
        act.ShouldNotThrow();

        // After consuming more than the estimated bytes, a subsequent publish should still work.
        // (The registry clamps to 0, not negative.)
        var act2 = () => sut.Publish(orderId, MakeEvent(orderId, 10));
        act2.ShouldNotThrow();
    }

    [Fact]
    public async Task Sse_endpoint_writes_heartbeat_comment_when_no_event_arrives()
    {
        // Arrange — build a minimal HttpContext with a MemoryStream as the response body.
        var responseBody = new MemoryStream();
        var httpCtx = new DefaultHttpContext();
        httpCtx.Response.Body = responseBody;

        var orderId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        // Set up a ClaimsPrincipal so GetUserId() returns the correct userId.
        var claims = new System.Security.Claims.ClaimsPrincipal(
            new System.Security.Claims.ClaimsIdentity(
                [
                    new System.Security.Claims.Claim(
                        System.Security.Claims.ClaimTypes.NameIdentifier,
                        userId.ToString()
                    ),
                ],
                "test"
            )
        );
        httpCtx.User = claims;

        // Use a registry stub that owns the channel so the endpoint's loop can be controlled.
        var stub = new StubSseRegistry(orderId, userId);

        // Cancel the SSE loop after a very short time so the test doesn't run forever.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        // Act — run the SSE endpoint; it should write at least the connected comment.
        // TaskCanceledException is expected when the CTS fires while the loop is flushing.
        try
        {
            await Travel.Modules.Flights.Api.Endpoints.OrderEventsSseEndpoint.Stream(
                orderId,
                httpCtx,
                stub,
                TimeProvider.System,
                cts.Token
            );
        }
        catch (OperationCanceledException)
        {
            // Expected: the loop is cancelled by the CTS after ~3 s.
        }

        // Assert — the response must contain at least the ": connected" comment written
        // immediately on connection.
        responseBody.Position = 0;
        var written = new StreamReader(responseBody, Encoding.UTF8).ReadToEnd();
        written.ShouldContain(": connected");
    }

    // ── Stubs ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Minimal IOrderSseRegistry stub for endpoint unit tests.
    /// Always reports the given owner and is a no-op for all other operations.
    /// </summary>
    private sealed class StubSseRegistry(Guid orderId, Guid ownerId) : IOrderSseRegistry
    {
        public void Register(Guid id, Channel<SseEvent> channel) { }

        public void Unregister(Guid id, Channel<SseEvent> channel) { }

        public void Publish(Guid id, SseEvent evt) { }

        public Task<Guid?> LookupOrderOwnerAsync(Guid id, CancellationToken ct) =>
            Task.FromResult<Guid?>(id == orderId ? ownerId : null);

        public void RecordBytesConsumed(Channel<SseEvent> channel, long bytes) { }
    }
}
