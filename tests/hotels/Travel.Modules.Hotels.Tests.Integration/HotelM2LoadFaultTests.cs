using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Travel.Modules.Hotels.Application;
using Travel.Modules.Hotels.Core;
using Travel.Modules.Hotels.Infrastructure.Providers.Duffel;

namespace Travel.Modules.Hotels.Tests.Integration;

public sealed class HotelM2LoadFaultTests
{
    [Fact]
    public async Task Caller_abort_keeps_uncooperative_source_owned_until_actual_completion_and_never_caches_late_result()
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = new HotelM2LoadTestClock(DateTimeOffset.UtcNow);
        var p = new HotelM2LoadProfile(
            "S",
            DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime).AddDays(7)
        );
        var schedule = new HotelM2LoadSchedule(p);
        await using var fixture = await HotelM2MemoryFixture.StartAsync(
            ct,
            time: clock,
            loadProfile: p
        );
        Begin(fixture);
        clock.Advance(TimeSpan.FromSeconds(162));
        var job = schedule.Jobs.Single(j => j.Kind == "search" && j.Due == 162);
        fixture.Client.DefaultRequestHeaders.Add("X-Travel-Fictional-Job", job.Id.ToString());
        using var cancelled = new CancellationTokenSource(TimeSpan.FromMilliseconds(50), clock);
        var pending = fixture.Client.PostAsJsonAsync(
            "/api/hotels/search",
            new
            {
                areaKey = "fictional",
                checkIn = p.CheckIn,
                checkOut = p.CheckOut,
                adults = p.Adults,
            },
            cancelled.Token
        );
        await clock.WaitForTimersAsync(ct, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(18));
        fixture.CaptureMeasurements().Sum(s => s.Starts).ShouldBe(2);
        clock.Advance(TimeSpan.FromMilliseconds(50));
        try
        {
            using var response = await pending.WaitAsync(TimeSpan.FromSeconds(2), ct);
        }
        catch (OperationCanceledException) { }
        var during = fixture.CaptureMeasurements();
        during.Single(s => s.Provider == "hotelbeds").Active.ShouldBe(1);
        var capacity = fixture.Services.GetRequiredService<HotelCapacity>();
        capacity
            .GetSourceSnapshots()
            .Single(s => s.Provider == "hotelbeds")
            .Capacity.ForegroundActive.ShouldBe(1);
        fixture.Services.GetRequiredService<HotelSnapshotCache>().Count.ShouldBe(0);
        clock.Advance(TimeSpan.FromSeconds(18));
        await WaitForForegroundReleaseAsync(capacity, "hotelbeds", ct);
        await WaitForForegroundReleaseAsync(capacity, "duffel-stays", ct);
        fixture.CaptureMeasurements().ShouldAllBe(s => s.Active == 0);
        fixture.Services.GetRequiredService<HotelSnapshotCache>().Count.ShouldBe(0);
    }

    [Fact]
    public async Task Cooperative_slow_source_reports_limited_timeout_and_releases_permit()
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = new HotelM2LoadTestClock(DateTimeOffset.UtcNow);
        var p = new HotelM2LoadProfile(
            "S",
            DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime).AddDays(7)
        );
        await using var fixture = await HotelM2MemoryFixture.StartAsync(
            ct,
            time: clock,
            loadProfile: p
        );
        Begin(fixture);
        fixture.StartMeasurements();
        clock.Advance(TimeSpan.FromSeconds(186));
        var job = fixture.LoadControl!.Schedule.Jobs.Single(j =>
            j.Kind == "search" && j.Due == 186
        );
        fixture.Client.DefaultRequestHeaders.Add(HotelM2LoadControl.Header, job.Id.ToString());
        var pending = fixture.Client.PostAsJsonAsync(
            "/api/hotels/search",
            new
            {
                areaKey = "fictional",
                checkIn = p.CheckIn,
                checkOut = p.CheckOut,
                adults = p.Adults,
            },
            ct
        );
        await clock.WaitForTimersAsync(
            ct,
            TimeSpan.FromMilliseconds(100),
            TimeSpan.FromSeconds(16)
        );
        fixture.CaptureMeasurements().Sum(s => s.Starts).ShouldBe(2);
        var capacity = fixture.Services.GetRequiredService<HotelCapacity>();
        clock.Advance(TimeSpan.FromMilliseconds(100));
        await WaitForForegroundReleaseAsync(capacity, "duffel-stays", ct);
        clock.Advance(TimeSpan.FromMilliseconds(14900));
        using var response = await pending.WaitAsync(TimeSpan.FromSeconds(2), ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var value = await response.Content.ReadFromJsonAsync<HotelSearchSnapshot>(ct);
        value!.Result.Kind.ShouldBe(ExecutionKind.Limited);
        Travel
            .Hotels.Fixture.HotelsM2LoadDispatch.ExpectedTimeout(
                value.Result.Kind.ToString(),
                value.Statuses
            )
            .ShouldBeTrue();
        var wrongStatuses = value
            .Statuses.Select(s => s.Provider == "hotelbeds" ? s with { Reason = "Other" } : s)
            .ToArray();
        Travel
            .Hotels.Fixture.HotelsM2LoadDispatch.ExpectedTimeout(
                value.Result.Kind.ToString(),
                wrongStatuses
            )
            .ShouldBeFalse();
        value.Statuses.Single(s => s.Provider == "hotelbeds").Reason.ShouldBe("Timeout");
        // Keep virtual time at the 15s deadline while cancellation unwinds the 16s wire delay.
        await WaitForForegroundReleaseAsync(capacity, "hotelbeds", ct);
        var sources = fixture.CaptureMeasurements();
        sources.ShouldAllBe(s => s.Active == 0);
        sources
            .Single(s => s.Provider == "hotelbeds")
            .Events.ShouldContain(e => e.Stage == "Cancelled");
    }

    private static async Task WaitForForegroundReleaseAsync(
        HotelCapacity capacity,
        string provider,
        CancellationToken ct
    )
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        while (true)
        {
            var snapshot = capacity.GetSourceSnapshots().Single(s => s.Provider == provider);
            if (snapshot.Capacity.ForegroundActive == 0)
            {
                snapshot.Releases.ShouldBe(1);
                return;
            }
            await Task.Delay(10, timeout.Token);
        }
    }

    private static void Begin(HotelM2MemoryFixture fixture) => fixture.LoadControl!.Begin();
}
