using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Travel.Modules.Flights.Api.Endpoints;
using Travel.Modules.Flights.Application.Notifications;
using Travel.Modules.Flights.Infrastructure.Notifications.Sse;
using Xunit;

namespace Travel.Modules.Flights.Tests.Unit.Notifications;

public sealed class SseEndpointLifecycleTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Completed_or_full_channel_exits_and_unregisters(bool full)
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var registry = new Registry(services, full);
        var ctx = Context(registry.Owner);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken
        );
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        await OrderEventsSseEndpoint.Stream(
            registry.Id,
            ctx,
            registry,
            TimeProvider.System,
            timeout.Token
        );
        registry.Unregistered.ShouldBeTrue();
        if (full)
            Encoding
                .UTF8.GetString(((MemoryStream)ctx.Response.Body).ToArray())
                .ShouldContain("\"streamVersion\":32");
    }

    [Fact]
    public async Task Events_do_not_create_overlapping_heartbeat_waits()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var registry = new Registry(services, null);
        var ctx = Context(registry.Owner);
        var time = new FakeTimeProvider();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken
        );
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        var run = OrderEventsSseEndpoint.Stream(registry.Id, ctx, registry, time, timeout.Token);
        registry.Publish(registry.Id, Event(registry.Id, 5));
        await WaitUntil(() => Text(ctx).Contains("OrderConfirmed"), timeout.Token);
        time.Advance(TimeSpan.FromSeconds(15));
        await WaitUntil(() => Text(ctx).Contains(":\n\n"), timeout.Token);
        registry.Publish(registry.Id, Event(registry.Id, 6));
        await WaitUntil(() => Text(ctx).Contains("\"streamVersion\":6"), timeout.Token);
        registry.Channel!.Writer.TryComplete();
        await run;
        registry.Unregistered.ShouldBeTrue();
    }

    private static string Text(HttpContext ctx) =>
        Encoding.UTF8.GetString(((MemoryStream)ctx.Response.Body).ToArray());

    private static async Task WaitUntil(Func<bool> ready, CancellationToken ct)
    {
        while (!ready())
            await Task.Delay(10, ct);
    }

    private static DefaultHttpContext Context(Guid owner) =>
        new()
        {
            User = new ClaimsPrincipal(
                new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, owner.ToString())], "test")
            ),
            Response = { Body = new MemoryStream() },
        };

    private static SseEvent Event(Guid id, long version) =>
        new(
            "OrderConfirmed",
            id,
            JsonSerializer.SerializeToElement(new { status = "Confirmed" }),
            DateTimeOffset.UnixEpoch,
            version
        );

    private sealed class Registry(ServiceProvider services, bool? full) : IOrderSseRegistry
    {
        private readonly OrderSseConnectionRegistry _inner = new(
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<OrderSseConnectionRegistry>.Instance
        );
        public Guid Id { get; } = Guid.NewGuid();
        public Guid Owner { get; } = Guid.NewGuid();
        public Channel<SseEvent>? Channel { get; private set; }
        public bool Unregistered { get; private set; }

        public void Register(Guid orderId, Channel<SseEvent> channel)
        {
            Channel = channel;
            _inner.Register(orderId, channel);
            if (full == true)
                for (var i = 1; i <= 33; i++)
                    _inner.Publish(orderId, Event(orderId, i));
            else if (full == false)
                channel.Writer.TryComplete();
        }

        public void Unregister(Guid orderId, Channel<SseEvent> channel)
        {
            _inner.Unregister(orderId, channel);
            Unregistered = true;
        }

        public void Publish(Guid orderId, SseEvent evt) => _inner.Publish(orderId, evt);

        public Task<Guid?> LookupOrderOwnerAsync(Guid orderId, CancellationToken ct) =>
            Task.FromResult<Guid?>(Owner);

        public void RecordBytesConsumed(Channel<SseEvent> channel, long bytes) =>
            _inner.RecordBytesConsumed(channel, bytes);
    }
}
