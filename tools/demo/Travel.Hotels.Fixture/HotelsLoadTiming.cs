namespace Travel.Hotels.Fixture;

internal static class HotelsLoadTiming
{
    // Recheck the actual monotonic clock after every wake. Never round observed timestamps.
    internal static async Task WaitUntilAsync(
        double due,
        Func<double> elapsed,
        CancellationToken ct,
        Func<TimeSpan, CancellationToken, Task>? delay = null
    )
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var now = elapsed();
            if (!double.IsFinite(due) || due < 0 || !double.IsFinite(now) || now < 0)
                throw new InvalidOperationException("Invalid monotonic deadline.");
            var left = due - now;
            if (left <= 0)
                return;
            // TimeSpan-based timers truncate sub-millisecond delays. Avoid a completed-task spin.
            await (delay ?? Task.Delay)(
                TimeSpan.FromMilliseconds(Math.Max(1, Math.Ceiling(left * 1000))),
                ct
            );
        }
    }
}
