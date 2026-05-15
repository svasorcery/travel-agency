using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Travel.Modules.Flights.Application.Notifications;
using Travel.Modules.Flights.Infrastructure.Notifications.Sse;
using Travel.Shared.Web;
using Wolverine.Http;

namespace Travel.Modules.Flights.Api.Endpoints;

public static class OrderEventsSseEndpoint
{
    [WolverineGet("/events/flights/orders/{orderId:guid}")]
    [Authorize]
    public static async Task Stream(
        Guid orderId,
        HttpContext ctx,
        IOrderSseRegistry registry,
        TimeProvider time,
        CancellationToken ct
    )
    {
        var userId = ctx.User.GetUserId();
        var owner = await registry.LookupOrderOwnerAsync(orderId, ct);
        if (owner is null || owner != userId)
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        ctx.Response.Headers["Content-Type"] = "text/event-stream";
        ctx.Response.Headers["Cache-Control"] = "no-cache";
        ctx.Response.Headers["X-Accel-Buffering"] = "no";

        var channel = Channel.CreateBounded<SseEvent>(
            new BoundedChannelOptions(32) { FullMode = BoundedChannelFullMode.DropOldest }
        );

        // Register inside try so any exception before the loop cannot leak a registration.
        registry.Register(orderId, channel);
        try
        {
            using var heartbeatTimer = new PeriodicTimer(TimeSpan.FromSeconds(15));

            await WriteCommentAsync(ctx.Response, "connected", ct);

            // Resolve the concrete registry to call RecordBytesConsumed if available.
            var concreteRegistry = registry as OrderSseConnectionRegistry;

            while (!ct.IsCancellationRequested)
            {
                // Wait for either an event or the heartbeat
                var readTask = channel.Reader.WaitToReadAsync(ct).AsTask();
                var timerTask = heartbeatTimer.WaitForNextTickAsync(ct).AsTask();

                await Task.WhenAny(readTask, timerTask);

                // Unconditionally drain all available events every iteration — this ensures
                // no event is left unread even when the heartbeat timer wins the race.
                while (channel.Reader.TryRead(out var evt))
                {
                    await WriteEventAsync(ctx.Response, evt, concreteRegistry, channel, ct);
                }

                // If no event was available, the timer won — send a heartbeat comment.
                if (!readTask.IsCompleted)
                    await WriteCommentAsync(ctx.Response, "", ct);
            }
        }
        finally
        {
            registry.Unregister(orderId, channel);
            channel.Writer.TryComplete();
        }
    }

    private static async Task WriteEventAsync(
        HttpResponse response,
        SseEvent evt,
        OrderSseConnectionRegistry? registry,
        Channel<SseEvent> channel,
        CancellationToken ct
    )
    {
        var data = JsonSerializer.Serialize(
            new
            {
                type = evt.Type,
                orderId = evt.OrderId,
                payload = evt.Payload,
                at = evt.At,
            }
        );

        var sb = new StringBuilder();
        sb.Append("event: ").AppendLine(evt.Type);
        sb.Append("data: ").AppendLine(data);
        sb.AppendLine();

        var serialized = sb.ToString();

        // Notify the registry that we consumed these bytes so the buffer estimate stays accurate.
        registry?.RecordBytesConsumed(channel, System.Text.Encoding.UTF8.GetByteCount(serialized));

        await response.WriteAsync(serialized, ct);
        await response.Body.FlushAsync(ct);
    }

    private static async Task WriteCommentAsync(
        HttpResponse response,
        string comment,
        CancellationToken ct
    )
    {
        var line = string.IsNullOrEmpty(comment) ? ":\n\n" : $": {comment}\n\n";
        await response.WriteAsync(line, ct);
        await response.Body.FlushAsync(ct);
    }
}
