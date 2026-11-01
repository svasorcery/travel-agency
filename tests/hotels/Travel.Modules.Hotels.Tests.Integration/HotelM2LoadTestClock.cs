using System.Collections.Concurrent;
using Microsoft.Extensions.Time.Testing;

namespace Travel.Modules.Hotels.Tests.Integration;

// Test-only clock: request-start counters precede timer creation and cannot
// establish that advancing fake time will release the intended wire delay.
internal sealed class HotelM2LoadTestClock(DateTimeOffset start) : TimeProvider
{
    private readonly FakeTimeProvider inner = new(start);
    private readonly ConcurrentDictionary<TimeSpan, SemaphoreSlim> registered = new();

    public override DateTimeOffset GetUtcNow() => inner.GetUtcNow();

    public override long GetTimestamp() => inner.GetTimestamp();

    public override long TimestampFrequency => inner.TimestampFrequency;
    public override TimeZoneInfo LocalTimeZone => inner.LocalTimeZone;

    public void Advance(TimeSpan amount) => inner.Advance(amount);

    public override ITimer CreateTimer(
        TimerCallback callback,
        object? state,
        TimeSpan dueTime,
        TimeSpan period
    )
    {
        var timer = inner.CreateTimer(callback, state, dueTime, period);
        if (period == Timeout.InfiniteTimeSpan)
            registered.GetOrAdd(dueTime, _ => new(0)).Release();
        return timer;
    }

    public async Task WaitForTimersAsync(CancellationToken ct, params TimeSpan[] delays)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        foreach (var delay in delays)
            await registered.GetOrAdd(delay, _ => new(0)).WaitAsync(deadline.Token);
    }
}
