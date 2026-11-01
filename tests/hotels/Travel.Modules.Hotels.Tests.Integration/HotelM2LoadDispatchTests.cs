using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Travel.Hotels.Fixture;

namespace Travel.Modules.Hotels.Tests.Integration;

public sealed class HotelM2LoadDispatchTests
{
    [Theory]
    [InlineData("S")]
    [InlineData("L")]
    public async Task Frozen_dependencies_route_both_sources_and_missing_prerequisite_never_falls_back(
        string code
    )
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var start = clock.GetUtcNow();
        var profile = new HotelM2LoadProfile(
            code,
            DateOnly.FromDateTime(start.UtcDateTime).AddDays(7)
        );
        var plan = new HotelM2LoadSchedule(profile);
        await using var fixture = await HotelM2MemoryFixture.StartAsync(
            ct,
            time: clock,
            loadProfile: profile
        );
        await HotelM2LoadSeed.PrepareMemoryAsync(fixture, profile, ct);
        var type = typeof(HotelsLoadLedger).Assembly.GetType(
            "Travel.Hotels.Fixture.HotelsM2LoadDispatch"
        );
        type.ShouldNotBeNull();
        Func<double> elapsed = () => (clock.GetUtcNow() - start).TotalSeconds;
        Func<HotelM2PlannedJob, CancellationToken, Task<HttpResponseMessage>> callback = (_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        var dispatcher = Activator.CreateInstance(type, fixture.Client, plan, elapsed, callback)!;
        var execute = type.GetMethod("ExecuteAsync")!;
        async Task<JsonElement> Run(HotelM2PlannedJob job)
        {
            var task = (Task)execute.Invoke(dispatcher, [job, ct])!;
            await task;
            return JsonSerializer.SerializeToElement(
                task.GetType().GetProperty("Result")!.GetValue(task)
            );
        }
        var missing = plan.Jobs.First(j => j.Kind == "prepare");
        var denied = await Run(missing);
        denied.GetProperty("Terminal").GetString().ShouldBe("NotDispatched");
        foreach (var purchase in plan.Jobs.Where(j => j.Kind == "create").Take(2))
        {
            var prepare = plan.Jobs[purchase.Prerequisite!.Value];
            var rates = plan.Jobs[prepare.Prerequisite!.Value];
            var search = plan.Jobs[rates.Prerequisite!.Value];
            foreach (var job in new[] { search, rates, prepare, purchase })
            {
                clock.Advance(TimeSpan.FromSeconds(job.Due - elapsed()));
                var result = await Run(job);
                result.GetProperty("Status").GetInt32().ShouldBe(200);
                result.GetProperty("Terminal").GetString().ShouldBe("Response");
                if (job.Kind == "create")
                {
                    result.GetProperty("ResponseProvider").GetString().ShouldBe(job.Provider);
                    result.GetProperty("PurchaseAccepted").GetBoolean().ShouldBeTrue();
                    result.GetProperty("Outcome").GetString().ShouldBe("Confirmed");
                }
                else
                    result.GetProperty("Useful").GetBoolean().ShouldBeTrue();
            }
        }
        fixture.Creates("duffel-stays").ShouldBe(14); //13seed + one timed-shaped request
        fixture.Creates("hotelbeds").ShouldBe(2); //1seed + one timed-shaped request
    }
}
