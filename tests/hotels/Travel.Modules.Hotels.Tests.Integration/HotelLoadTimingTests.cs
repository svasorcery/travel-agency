using System.Diagnostics;
using Shouldly;
using Travel.Hotels.Fixture;

namespace Travel.Modules.Hotels.Tests.Integration;

public sealed class HotelLoadTimingTests
{
    [Fact]
    public async Task Early_timer_completion_is_rechecked_without_rounding_observed_start()
    {
        var now = 41.9995;
        var calls = 0;
        await HotelsLoadTiming.WaitUntilAsync(
            42,
            () => now,
            TestContext.Current.CancellationToken,
            (delay, ct) =>
            {
                calls++;
                now = calls == 1 ? 41.9999 : 42.0001;
                return Task.CompletedTask;
            }
        );
        now.ShouldBe(42.0001);
        calls.ShouldBe(2);
        var ledger = new HotelsLoadLedger([new(0, "search", 42)]);
        ledger.Begin(0, now).ShouldBeTrue();
        ledger.Snapshot().Entries[0].Started.ShouldBe(42.0001);
    }

    [Fact]
    public async Task Real_monotonic_sub_millisecond_deadlines_never_return_early()
    {
        var clock = Stopwatch.StartNew();
        for (var i = 0; i < 80; i++)
        {
            var due = clock.Elapsed.TotalSeconds + 0.00075;
            await HotelsLoadTiming.WaitUntilAsync(
                due,
                () => clock.Elapsed.TotalSeconds,
                TestContext.Current.CancellationToken
            );
            clock.Elapsed.TotalSeconds.ShouldBeGreaterThanOrEqualTo(due);
        }
    }

    [Theory]
    [InlineData("S")]
    [InlineData("L")]
    public async Task Every_fixed_profile_deadline_survives_timer_truncation(string code)
    {
        var plan = new HotelM2LoadSchedule(new(code, new(2026, 11, 6)));
        var ledger = new HotelsLoadLedger(
            plan.Jobs.Select(j => new HotelLoadJob(j.Id, j.Kind, j.Due, j.Provider))
        );
        var now = 0d;
        foreach (var job in plan.Jobs)
        {
            await HotelsLoadTiming.WaitUntilAsync(
                job.Due,
                () => now,
                TestContext.Current.CancellationToken,
                (delay, ct) =>
                {
                    // Runtime TimeSpan timer truncation; a zero delay advances no monotonic time.
                    now += Math.Floor(delay.TotalMilliseconds) / 1000;
                    return Task.CompletedTask;
                }
            );
            ledger.Begin(job.Id, now).ShouldBeTrue();
            ledger.Complete(job.Id, "Response", now, 200).ShouldBeTrue();
        }
        ledger.Seal(now);
        ledger.Snapshot().Counts["Response"].ShouldBe(3152);
    }

    [Fact]
    public async Task Cancellation_is_observed_even_if_deadline_is_already_due()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await Should.ThrowAsync<OperationCanceledException>(() =>
            HotelsLoadTiming.WaitUntilAsync(0, () => 1, cancelled.Token)
        );
    }
}
