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
        if (!ctx.User.TryGetUserId(out var userId))
        {
            await ctx.WriteProblemDetailsAsync(IdentityProblemDetails.InvalidUserIdentity());
            return;
        }

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
            new BoundedChannelOptions(32) { FullMode = BoundedChannelFullMode.Wait }
        );

        // Unregister in finally even when writing the initial response fails.
        registry.Register(orderId, channel);
        try
        {
            using var heartbeatTimer = new PeriodicTimer(TimeSpan.FromSeconds(15), time);

            await WriteCommentAsync(ctx.Response, "connected", ct);

            var readTask = channel.Reader.WaitToReadAsync(ct).AsTask();
            var timerTask = heartbeatTimer.WaitForNextTickAsync(ct).AsTask();
            while (!ct.IsCancellationRequested)
            {
                await Task.WhenAny(readTask, timerTask);
                if (readTask.IsCompleted)
                {
                    if (!await readTask)
                        break;
                    while (channel.Reader.TryRead(out var evt))
                        await WriteEventAsync(ctx.Response, evt, registry, channel, ct);
                    readTask = channel.Reader.WaitToReadAsync(ct).AsTask();
                }
                if (timerTask.IsCompleted)
                {
                    if (!await timerTask)
                        break;
                    await WriteCommentAsync(ctx.Response, "", ct);
                    timerTask = heartbeatTimer.WaitForNextTickAsync(ct).AsTask();
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
        IOrderSseRegistry registry,
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
                streamVersion = evt.StreamVersion,
            }
        );

        var sb = new StringBuilder();
        sb.Append("event: ").AppendLine(evt.Type);
        sb.Append("data: ").AppendLine(data);
        sb.AppendLine();

        var serialized = sb.ToString();

        // Notify the registry that we consumed these bytes so the buffer estimate stays accurate.
        registry.RecordBytesConsumed(
            channel,
            200 + Encoding.UTF8.GetByteCount(evt.Payload.GetRawText())
        );

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
