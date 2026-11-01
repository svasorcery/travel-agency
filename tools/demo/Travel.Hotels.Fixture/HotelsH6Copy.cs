namespace Travel.Hotels.Fixture;

internal static class HotelsH6Copy
{
    internal static async Task FileAsync(
        string source,
        string destination,
        Func<long> remaining,
        Action resources,
        CancellationToken ct
    )
    {
        ct.ThrowIfCancellationRequested();
        resources();
        await using var input = File.OpenRead(source);
        // Refuse predictable duplication overflow before creating any copy.
        if (input.Length > remaining())
            throw new InvalidOperationException("H6 projected evidence bound.");
        await using var output = new FileStream(
            destination,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.Read
        );
        var buffer = new byte[1024 * 1024];
        int count;
        while ((count = await input.ReadAsync(buffer, ct)) != 0)
        {
            ct.ThrowIfCancellationRequested();
            resources();
            if (count > remaining())
                throw new InvalidOperationException("H6 evidence bound during copy.");
            await output.WriteAsync(buffer.AsMemory(0, count), ct);
            await output.FlushAsync(ct);
            resources();
            ct.ThrowIfCancellationRequested();
        }
    }
}
