using System.Net;
using Shouldly;

namespace Travel.Modules.Hotels.Tests.Integration;

public sealed class HotelM2LoadTestClockTests
{
    [Fact]
    public async Task Wire_start_is_not_timer_registration_and_does_not_allow_early_clock_advance()
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken
        );
        var ct = stop.Token;
        var epoch = DateTimeOffset.UnixEpoch;
        var clock = new HotelM2LoadTestClock(epoch);
        var measurement = new HotelWireMeasurements("duffel-stays");
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var register = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = measurement.ObserveAsync(
            "search",
            async () =>
            {
                started.SetResult();
                await register.Task.WaitAsync(ct);
                await Task.Delay(TimeSpan.FromMilliseconds(100), clock, ct);
                return new HttpResponseMessage(HttpStatusCode.OK);
            }
        );
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
            measurement.Capture(new(0, 0, 0, null)).Starts.ShouldBe(1);
            var ready = clock.WaitForTimersAsync(ct, TimeSpan.FromMilliseconds(100));
            ready.IsCompleted.ShouldBeFalse();
            clock.GetUtcNow().ShouldBe(epoch);
            register.SetResult();
            await ready.WaitAsync(TimeSpan.FromSeconds(5), ct);
            clock.Advance(TimeSpan.FromMilliseconds(100));
            using var result = await pending.WaitAsync(TimeSpan.FromSeconds(5), ct);
            result.StatusCode.ShouldBe(HttpStatusCode.OK);
            clock.GetUtcNow().ShouldBe(epoch.AddMilliseconds(100));
        }
        finally
        {
            register.TrySetResult();
            await stop.CancelAsync();
            try
            {
                using var result = await pending;
            }
            catch (OperationCanceledException) { }
        }
    }
}
