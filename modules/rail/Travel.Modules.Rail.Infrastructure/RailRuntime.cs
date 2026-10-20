using Microsoft.Extensions.Options;
using Travel.Modules.Rail.Application;

namespace Travel.Modules.Rail.Infrastructure;

public sealed class RailOptions
{
    public bool Enabled { get; set; }
    public bool Fictional { get; set; }
    public string ApiKey { get; set; } = "";
    public bool RightsConfirmed { get; set; }
    public bool BudgetConfirmed { get; set; }
    public bool RestartAllowanceConfirmed { get; set; }
    public int MinuteAllowance { get; set; }
    public int DayAllowance { get; set; }
    public DateTimeOffset? BudgetConfirmedAt { get; set; }
}

public sealed class RailRuntime : IRailRuntimeProfile, IDisposable
{
    private readonly object gate = new();
    private readonly IDisposable? subscription;
    private CancellationTokenSource cancellation = new();
    private RailProfile current;
    private RailOptions options;
    private readonly TimeProvider clock;
    private readonly DateTimeOffset startedAt;
    private int disposed;

    public RailRuntime(IOptionsMonitor<RailOptions> monitor, TimeProvider? clock = null)
    {
        this.clock = clock ?? TimeProvider.System;
        startedAt = this.clock.GetUtcNow();
        options = monitor.CurrentValue;
        current = Profile(options, false);
        subscription = monitor.OnChange(value =>
        {
            CancellationTokenSource previous;
            lock (gate)
            {
                previous = cancellation;
                cancellation = new();
                options = value;
                current = Profile(
                    value,
                    value.BudgetConfirmedAt > startedAt
                        && value.BudgetConfirmedAt <= this.clock.GetUtcNow()
                );
            }
            previous.Cancel();
            previous.Dispose();
        });
    }

    private RailProfile Profile(RailOptions value, bool freshlyConfirmed) =>
        new(
            Guid.NewGuid().ToString("N"),
            value.Fictional ? "fictional" : "real",
            value.Enabled
                && (
                    value.Fictional
                    || freshlyConfirmed
                        && value.RightsConfirmed
                        && value.BudgetConfirmed
                        && value.RestartAllowanceConfirmed
                        && !string.IsNullOrWhiteSpace(value.ApiKey)
                        && value.MinuteAllowance is > 0 and <= 100000
                        && value.DayAllowance is > 0 and <= 100000
                ),
            cancellation.Token
        );

    public RailProfile Current
    {
        get
        {
            lock (gate)
                return current;
        }
    }
    internal RailOptions Options
    {
        get
        {
            lock (gate)
                return options;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
            return;
        subscription?.Dispose();
        cancellation.Cancel();
        cancellation.Dispose();
    }
}

public sealed class RailQuotaLedger(TimeProvider clock)
{
    private readonly object gate = new();
    private readonly Queue<DateTimeOffset> dispatched = [];
    private DateTimeOffset blockedUntil;
    private bool permanentlyClosed;

    public bool TryDebit(int minuteAllowance, int dayAllowance)
    {
        lock (gate)
        {
            var now = clock.GetUtcNow();
            while (dispatched.TryPeek(out var oldest) && oldest <= now.AddDays(-1))
                dispatched.Dequeue();
            if (
                permanentlyClosed
                || now < blockedUntil
                || minuteAllowance <= 0
                || dayAllowance <= 0
                || dispatched.Count >= Math.Min(dayAllowance, 100000)
                || dispatched.Count(x => x > now.AddSeconds(-60)) >= minuteAllowance
            )
                return false;
            dispatched.Enqueue(now);
            return true;
        }
    }

    public void RemoteThrottled(string? retryAfter)
    {
        lock (gate)
        {
            var now = clock.GetUtcNow();
            var until = now.AddSeconds(60);
            if (!string.IsNullOrEmpty(retryAfter) && retryAfter.All(char.IsAsciiDigit))
            {
                if (
                    !long.TryParse(retryAfter, out var seconds)
                    || seconds > (DateTimeOffset.MaxValue - now).TotalSeconds
                )
                {
                    permanentlyClosed = true;
                    return;
                }
                until = now.AddSeconds(seconds);
            }
            else if (
                DateTimeOffset.TryParse(
                    retryAfter,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeUniversal,
                    out var date
                )
            )
                until = date > now ? date : now;
            if (until > blockedUntil)
                blockedUntil = until;
        }
    }
}
