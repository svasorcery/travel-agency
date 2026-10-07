using System.Text.Json;

namespace Travel.Modules.Flights.Infrastructure.Providers.Duffel;

internal static class DuffelBoundedJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        MaxDepth = 32,
    };

    internal static async Task<T?> Read<T>(
        HttpResponseMessage response,
        int maximumBytes,
        CancellationToken ct
    )
    {
        if (response.Content.Headers.ContentLength > maximumBytes)
            throw new JsonException("Supplier response too large.");
        await using var input = await response.Content.ReadAsStreamAsync(ct);
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = await input.ReadAsync(buffer, ct)) > 0)
        {
            if (output.Length + count > maximumBytes)
                throw new JsonException("Supplier response too large.");
            output.Write(buffer, 0, count);
        }
        return JsonSerializer.Deserialize<T>(
            output.GetBuffer().AsSpan(0, checked((int)output.Length)),
            Options
        );
    }
}
