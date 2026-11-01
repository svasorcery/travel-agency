using Shouldly;
using Travel.Hotels.Fixture;
using Travel.Modules.Hotels.Application;
using Travel.Modules.Hotels.Core;

namespace Travel.Modules.Hotels.Tests.Integration;

public sealed class HotelM2LoadLossTests
{
    [Theory]
    [InlineData("S")]
    [InlineData("L")]
    public async Task Fixed_loss_chain_retains_one_effect_and_replay_cannot_resend(string code)
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = new HotelM2LoadTestClock(DateTimeOffset.UtcNow);
        var began = clock.GetUtcNow();
        var profile = new HotelM2LoadProfile(
            code,
            DateOnly.FromDateTime(began.UtcDateTime).AddDays(7)
        );
        var plan = new HotelM2LoadSchedule(profile);
        await using var fixture = await HotelM2MemoryFixture.StartAsync(
            ct,
            time: clock,
            loadProfile: profile
        );
        await HotelM2LoadSeed.PrepareMemoryAsync(fixture, profile, ct);
        var before = fixture.CaptureMeasurements().Sum(s => s.Effects.Values.Sum());
        var dispatcher = new HotelsM2LoadDispatch(
            fixture.Client,
            plan,
            () => (clock.GetUtcNow() - began).TotalSeconds,
            (_, _) => throw new InvalidOperationException("No callback in the loss chain.")
        );
        fixture.LoadControl!.Begin();
        var create = plan.Jobs.Single(j => j.Kind == "create" && j.Due == 154.5);
        var prepare = plan.Jobs[create.Prerequisite!.Value];
        var rates = plan.Jobs[prepare.Prerequisite!.Value];
        var search = plan.Jobs[rates.Prerequisite!.Value];
        foreach (var job in new[] { search, rates, prepare, create })
        {
            clock.Advance(TimeSpan.FromSeconds(job.Due - (clock.GetUtcNow() - began).TotalSeconds));
            var started = fixture.CaptureMeasurements().Sum(s => s.Starts);
            var pending = dispatcher.ExecuteAsync(job, ct);
            var required = job.Kind is "search" or "rates" ? 2 : 1;
            await clock.WaitForTimersAsync(
                ct,
                Enumerable.Repeat(TimeSpan.FromMilliseconds(100), required).ToArray()
            );
            fixture.CaptureMeasurements().Sum(s => s.Starts).ShouldBe(started + required);
            clock.Advance(TimeSpan.FromMilliseconds(100));
            var result = await pending.WaitAsync(TimeSpan.FromSeconds(5), ct);
            result.Status.ShouldBe(200);
            if (job.Kind == "create")
            {
                result.Outcome.ShouldBe("Unknown");
                result.PurchaseAccepted.ShouldBeTrue();
            }
            else
                result.Useful.ShouldBeTrue();
        }
        fixture.CaptureMeasurements().Sum(s => s.Effects.Values.Sum()).ShouldBe(before + 1);
        var row = fixture.Store.Snapshot(create.ReservationId!.Value)!;
        row.State.Outcome.ShouldBe(ReservationOutcome.Unknown);
        row.State.BoundBookingId.ShouldBeNull();
        var starts = fixture.CaptureMeasurements().Sum(s => s.Starts);
        var replay = await dispatcher.ExecuteAsync(create, ct);
        replay.Outcome.ShouldBe("Unknown");
        fixture.CaptureMeasurements().Sum(s => s.Starts).ShouldBe(starts);
        fixture.CaptureMeasurements().Sum(s => s.Effects.Values.Sum()).ShouldBe(before + 1);
    }

    [Theory]
    [InlineData("S")]
    [InlineData("L")]
    public async Task Hotelbeds_reference_is_confirmed_by_actual_owned_get_under_recovery_permit(
        string code
    )
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = new HotelM2LoadTestClock(DateTimeOffset.UtcNow);
        var began = clock.GetUtcNow();
        var profile = new HotelM2LoadProfile(
            code,
            DateOnly.FromDateTime(began.UtcDateTime).AddDays(7)
        );
        var plan = new HotelM2LoadSchedule(profile);
        await using var fixture = await HotelM2MemoryFixture.StartAsync(
            ct,
            time: clock,
            loadProfile: profile
        );
        await HotelM2LoadSeed.PrepareMemoryAsync(fixture, profile, ct);
        var dispatcher = new HotelsM2LoadDispatch(
            fixture.Client,
            plan,
            () => (clock.GetUtcNow() - began).TotalSeconds,
            (_, _) => throw new InvalidOperationException("No callback in this chain.")
        );
        fixture.LoadControl!.Begin();
        var create = plan.Jobs.First(j => j.Kind == "create" && j.Provider == "hotelbeds");
        var prepare = plan.Jobs[create.Prerequisite!.Value];
        var rates = plan.Jobs[prepare.Prerequisite!.Value];
        var search = plan.Jobs[rates.Prerequisite!.Value];
        foreach (var job in new[] { search, rates, prepare, create })
        {
            clock.Advance(TimeSpan.FromSeconds(job.Due - (clock.GetUtcNow() - began).TotalSeconds));
            var started = fixture.CaptureMeasurements().Sum(s => s.Starts);
            var pending = dispatcher.ExecuteAsync(job, ct);
            var required = job.Kind is "search" or "rates" ? 2 : 1;
            await clock.WaitForTimersAsync(
                ct,
                Enumerable.Repeat(TimeSpan.FromMilliseconds(100), required).ToArray()
            );
            fixture.CaptureMeasurements().Sum(s => s.Starts).ShouldBe(started + required);
            clock.Advance(TimeSpan.FromMilliseconds(100));
            var result = await pending.WaitAsync(TimeSpan.FromSeconds(5), ct);
            result.Status.ShouldBe(200);
            if (job.Kind == "create")
                result.Outcome.ShouldBe("ReservedAwaitingDetails");
        }
        var row = fixture.Store.Snapshot(create.ReservationId!.Value)!;
        row.State.BoundBookingId.ShouldNotBeNull();
        var reads = fixture.Reads("hotelbeds");
        var runner =
            Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<Travel.Modules.Hotels.Infrastructure.Observations.HotelObservationRunner>(
                fixture.Services
            );
        var readTask = runner.RunAsync(new(row.Id, false, clock.GetUtcNow()), ct);
        await clock.WaitForTimersAsync(ct, TimeSpan.FromMilliseconds(100));
        fixture.CaptureMeasurements().Single(s => s.Provider == "hotelbeds").Active.ShouldBe(1);
        clock.Advance(TimeSpan.FromMilliseconds(100));
        await readTask.WaitAsync(TimeSpan.FromSeconds(5), ct);
        fixture.Reads("hotelbeds").ShouldBe(reads + 1);
        var confirmed = fixture.Store.Snapshot(row.Id)!;
        confirmed.State.Outcome.ShouldBe(ReservationOutcome.Confirmed);
        confirmed.State.ReadsClaimed.ShouldBe(1);
        fixture.LoadControl.Failure.ShouldBeNull();
    }
}
