using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Travel.Modules.Flights.Application.Notifications;
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

        registry.Register(orderId, channel);
        try
        {
            using var heartbeatTimer = new PeriodicTimer(TimeSpan.FromSeconds(15));

            await WriteCommentAsync(ctx.Response, "connected", ct);

            while (!ct.IsCancellationRequested)
            {
                // Wait for either an event or the heartbeat
                var readTask = channel.Reader.WaitToReadAsync(ct).AsTask();
                var timerTask = heartbeatTimer.WaitForNextTickAsync(ct).AsTask();

                var completed = await Task.WhenAny(readTask, timerTask);

                if (completed == timerTask)
                {
                    // Heartbeat: send SSE comment to keep connection alive
                    await WriteCommentAsync(ctx.Response, "", ct);
                    continue;
                }

                // Drain all available events
                while (channel.Reader.TryRead(out var evt))
                {
                    await WriteEventAsync(ctx.Response, evt, ct);
                }
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

        await response.WriteAsync(sb.ToString(), ct);
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
