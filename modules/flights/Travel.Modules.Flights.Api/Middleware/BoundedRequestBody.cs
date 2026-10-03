using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;

namespace Travel.Modules.Flights.Api.Middleware;

/// <summary>Temporary request plaintext stays in a capped memory buffer and is wiped on every exit.</summary>
internal static class BoundedRequestBody
{
    internal const int MaximumBytes = 16 * 1024;

    internal static async Task ReadAsync(
        HttpContext context,
        Func<ReadOnlyMemory<byte>, Task> consume,
        Func<Task> tooLarge
    )
    {
        if (context.Request.ContentLength is > MaximumBytes)
        {
            await tooLarge();
            return;
        }
        var original = context.Request.Body;
        var bytes = new byte[MaximumBytes + 1];
        try
        {
            var length = 0;
            while (length < bytes.Length)
            {
                var read = await original.ReadAsync(bytes.AsMemory(length), context.RequestAborted);
                if (read == 0)
                    break;
                length += read;
            }
            if (length > MaximumBytes)
            {
                await tooLarge();
                return;
            }
            using var body = new MemoryStream(bytes, 0, length, writable: false);
            context.Request.Body = body;
            await consume(bytes.AsMemory(0, length));
        }
        finally
        {
            context.Request.Body = original;
            CryptographicOperations.ZeroMemory(bytes);
        }
    }
}
