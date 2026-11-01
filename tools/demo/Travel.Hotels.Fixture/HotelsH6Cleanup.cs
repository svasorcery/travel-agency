namespace Travel.Hotels.Fixture;

internal static class HotelsH6Cleanup
{
    internal static async Task RunAsync(
        Func<CancellationToken, Task> trace,
        Func<CancellationToken, Task> host,
        CancellationToken ct
    )
    {
        var errors = new List<Exception>();
        try
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limit.CancelAfter(TimeSpan.FromSeconds(60));
            await trace(limit.Token);
        }
        catch (Exception ex)
        {
            errors.Add(ex);
        }
        // Trace-local timeout/failure never cancels Host cleanup. Outer 118s still bounds both.
        try
        {
            await host(ct);
        }
        catch (Exception ex)
        {
            errors.Add(ex);
        }
        if (errors.Count != 0)
            throw new AggregateException(errors);
    }
}
