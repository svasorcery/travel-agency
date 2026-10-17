using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Travel.Modules.Hotels.Api.Middleware;

public sealed class HotelsRequestPolicyMiddleware(
    RequestDelegate next,
    IOptions<HotelRequestOptions> options
)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (
            !context.Request.Path.StartsWithSegments(
                "/api/hotels",
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            await next(context);
            return;
        }
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.Pragma = "no-cache";
        context.Response.Headers.XContentTypeOptions = "nosniff";
        if (HttpMethods.IsPost(context.Request.Method) || HttpMethods.IsPut(context.Request.Method))
        {
            var maximum = options.Value.RequestBytes;
            if (context.Request.ContentLength > maximum)
            {
                await Results.Problem(statusCode: 413).ExecuteAsync(context);
                return;
            }
            // A bounded RAM-only buffer before binding. Never EnableBuffering (which can spill private content to disk).
            using var body = new MemoryStream();
            var chunk = new byte[4096];
            int read;
            while ((read = await context.Request.Body.ReadAsync(chunk, context.RequestAborted)) > 0)
            {
                if (body.Length + read > maximum)
                {
                    await Results.Problem(statusCode: 413).ExecuteAsync(context);
                    return;
                }
                body.Write(chunk, 0, read);
            }
            body.Position = 0;
            var original = context.Request.Body;
            context.Request.Body = body;
            try
            {
                await next(context);
            }
            finally
            {
                context.Request.Body = original;
            }
            return;
        }
        await next(context);
    }
}
