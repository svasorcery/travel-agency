using System.Reflection;
using System.Text.Json;
using Shouldly;
using Travel.Hotels.Fixture;

namespace Travel.Modules.Hotels.Tests.Integration;

public sealed class HotelH5PurchaseReplayTests
{
    private static readonly HotelM2LoadProfile Profile = new("S", new(2026, 11, 6));
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    internal static JsonElement Saved() =>
        JsonDocument
            .Parse(
                File.ReadAllText(
                    Path.Combine(
                        AppContext.BaseDirectory,
                        "Fixtures",
                        "h5-recorded-purchase-replay.json"
                    )
                )
            )
            .RootElement.Clone();

    [Theory]
    [InlineData("recordedH5", true)]
    [InlineData("recordedS", true)]
    [InlineData("confirmed", true)]
    [InlineData("unknown", false)]
    [InlineData("submitted", false)]
    [InlineData("quoted", false)]
    [InlineData("null-outcome", false)]
    [InlineData("wrong-source", false)]
    [InlineData("duffel-pending", false)]
    [InlineData("wrong-resource", false)]
    [InlineData("missing-resource", false)]
    [InlineData("not-admitted", false)]
    [InlineData("refused", false)]
    [InlineData("late", false)]
    [InlineData("cancelled", false)]
    [InlineData("http500", false)]
    public void Real_purchase_result_keeps_admission_source_resource_and_outcome_distinct(
        string variant,
        bool expected
    )
    {
        var key = variant == "recordedS" ? "operationsS" : "operationsH5";
        var result = Saved()
            .GetProperty(key)
            .Deserialize<HotelM2DispatchResult[]>(Json)!
            .Single(r => r.JobId == 26);
        result.Outcome.ShouldBe("ReservedAwaitingDetails");
        result = variant switch
        {
            "confirmed" => result with { Outcome = "Confirmed" },
            "unknown" => result with { Outcome = "Unknown" },
            "submitted" => result with { Outcome = "Submitted" },
            "quoted" => result with { Outcome = "Quoted" },
            "null-outcome" => result with { Outcome = null },
            "wrong-source" => result with
            {
                Outcome = "Confirmed",
                ResponseProvider = "duffel-stays",
            },
            "wrong-resource" => result with { Outcome = "Confirmed", ResourceId = Guid.NewGuid() },
            "missing-resource" => result with { Outcome = "Confirmed", ResourceId = null },
            "not-admitted" => result with { Outcome = "Confirmed", PurchaseAccepted = false },
            "refused" => result with { Outcome = "Confirmed", Refusal = "SemanticOutcome" },
            "late" => result with { Outcome = "Confirmed", Completed = 12.001 },
            "cancelled" => result with { Outcome = "Confirmed", Terminal = "Cancelled" },
            "http500" => result with { Outcome = "Confirmed", Status = 500 },
            _ => result,
        };
        var method = typeof(HotelsH5Controller).GetMethod(
            "Expected",
            BindingFlags.Static | BindingFlags.NonPublic
        )!;
        var job = HotelsH5Controller.Prefix(Profile).Single(j => j.Id == 26);
        if (variant == "duffel-pending")
        {
            job = job with { Provider = "duffel-stays" };
            result = result with { ResponseProvider = "duffel-stays" };
        }
        ((bool)method.Invoke(null, [job, result])!).ShouldBe(expected);
    }

    [Fact]
    public async Task Entire_controller_replays_recorded_pending_purchase_without_renaming_it_confirmed()
    {
        var port = new ReplayPort(Saved(), modeledIdleBoundary: true);
        var receipt = await Drive(port);
        receipt.AcquisitionComplete.ShouldBeTrue();
        receipt.CleanupComplete.ShouldBeTrue();
        receipt.IpcSamples.ShouldBe(35);
        receipt.Ledger.Counts["Response"].ShouldBe(56);
        receipt.Maps.ShouldBe(new[] { "P0", "P1", "P2" });
        receipt.ExecutionFailures!.First.ShouldBeNull();
        port.Operations!.Single(r => r.JobId == 26).Outcome.ShouldBe("ReservedAwaitingDetails");
        port.Operations!.Length.ShouldBe(56);
        port.LastSample.GetProperty("metrics").GetProperty("futureWork").GetInt32().ShouldBe(17);
        port.Calls.Count(x => x == "cleanup").ShouldBe(1);
    }

    [Fact]
    public async Task Unmodified_S_v2_t12_with_extra_search_is_an_active_source_failure_not_false_success()
    {
        var port = new ReplayPort(Saved(), modeledIdleBoundary: false);
        var receipt = await Drive(port);
        receipt.AcquisitionComplete.ShouldBeFalse();
        receipt.IpcSamples.ShouldBe(35);
        receipt.Ledger.Counts["Response"].ShouldBe(56);
        receipt.ExecutionFailures!.First.ShouldBeNull(); // No early failure at the recorded pending purchase.
        receipt.Maps.ShouldBe(new[] { "P0", "P1" });
        receipt.CleanupComplete.ShouldBeTrue();
    }

    [Theory]
    [InlineData("Unknown")]
    [InlineData("Submitted")]
    public async Task Unexpected_pending_state_stops_the_same_controller_and_keeps_partial_evidence(
        string outcome
    )
    {
        var port = new ReplayPort(Saved(), modeledIdleBoundary: true) { PurchaseOutcome = outcome };
        var receipt = await Drive(port);
        receipt.AcquisitionComplete.ShouldBeFalse();
        receipt.Ledger.Planned.ShouldBe(56);
        receipt.Ledger.Counts["NotRun"].ShouldBeGreaterThan(0);
        receipt.ExecutionFailures!.First!.JobId.ShouldBe(26);
        receipt.Maps.ShouldNotContain("P2");
        port.Operations!.Single(r => r.JobId == 26).Outcome.ShouldBe(outcome);
    }

    [Fact]
    public async Task Active_real_API_profile_returns_reference_only_purchase_that_H5_accepts()
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = new HotelM2LoadTestClock(Profile.FixtureEpoch);
        var began = clock.GetUtcNow();
        await using var fixture = await HotelM2MemoryFixture.StartAsync(
            ct,
            time: clock,
            loadProfile: Profile
        );
        await HotelM2LoadSeed.PrepareMemoryAsync(fixture, Profile, ct);
        var beforeCreates = fixture.Creates("hotelbeds");
        var beforeReads = fixture.Reads("hotelbeds");
        fixture.LoadControl!.Begin(); // The old dispatch tests omitted this actual reference-only branch.
        var schedule = new HotelM2LoadSchedule(Profile);
        var dispatcher = new HotelsM2LoadDispatch(
            fixture.Client,
            schedule,
            () => (clock.GetUtcNow() - began).TotalSeconds,
            (_, _) =>
                throw new InvalidOperationException(
                    "No callback in this API dependency-chain test."
                )
        );
        var create = schedule.Jobs.Single(j => j.Kind == "create" && j.Due == 4.5);
        var prepare = schedule.Jobs[create.Prerequisite!.Value];
        var rates = schedule.Jobs[prepare.Prerequisite!.Value];
        var search = schedule.Jobs[rates.Prerequisite!.Value];
        HotelM2DispatchResult? result = null;
        foreach (var job in new[] { search, rates, prepare, create })
        {
            var current = (clock.GetUtcNow() - began).TotalSeconds;
            if (current < job.Due)
                clock.Advance(TimeSpan.FromSeconds(job.Due - current));
            var operation = dispatcher.ExecuteAsync(job, ct);
            var required = job.Kind is "search" or "rates" ? 2 : 1;
            await clock.WaitForTimersAsync(
                ct,
                Enumerable.Repeat(TimeSpan.FromMilliseconds(100), required).ToArray()
            );
            clock.Advance(TimeSpan.FromMilliseconds(100));
            result = await operation.WaitAsync(TimeSpan.FromSeconds(5), ct);
            result.Status.ShouldBe(200);
            result.DriverFailure.ShouldBeNull();
            if (job.Kind != "create")
                result.Useful.ShouldBeTrue();
        }
        result!.Outcome.ShouldBe("ReservedAwaitingDetails");
        result.PurchaseAccepted.ShouldBeTrue();
        result.ResourceId.ShouldBe(create.ReservationId);
        result.ResponseProvider.ShouldBe("hotelbeds");
        fixture.Creates("hotelbeds").ShouldBe(beforeCreates + 1);
        fixture.Reads("hotelbeds").ShouldBe(beforeReads);
        fixture.LoadControl.Failure.ShouldBeNull();
        var expected = typeof(HotelsH5Controller).GetMethod(
            "Expected",
            BindingFlags.Static | BindingFlags.NonPublic
        )!;
        ((bool)expected.Invoke(null, [create, result])!).ShouldBeTrue();
    }

    internal static async Task<HotelH5Receipt> Drive(ReplayPort port, IHotelsH6Hooks? h6 = null)
    {
        var run = HotelsH5Controller.RunAsync(
            port,
            Profile,
            TestContext.Current.CancellationToken,
            h6
        );
        for (var i = 0; !run.IsCompleted && i < 1000; i++)
        {
            port.AdvanceNext();
            await Task.Delay(1, TestContext.Current.CancellationToken);
        }
        return await run.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    // No HTTP, OS process, database or collector. Actual saved response facts are released at their
    // recorded completion time, using virtual deadlines instead of retiming them to convenient values.
    internal sealed class ReplayPort(JsonElement saved, bool modeledIdleBoundary) : IHotelsH5Port
    {
        private readonly SortedDictionary<double, List<TaskCompletionSource>> waits = [];
        private readonly Dictionary<int, HotelM2DispatchResult> results = saved
            .GetProperty("operationsS")
            .Deserialize<HotelM2DispatchResult[]>(Json)!
            .ToDictionary(r => r.JobId);
        private double origin;
        private int sample;
        public double Elapsed { get; private set; }
        public int PreflightSamples => 1;
        public int PreflightSampleAttempts => 1;
        public string? PurchaseOutcome { get; init; }
        public HotelM2DispatchResult[]? Operations { get; private set; }
        public JsonElement LastSample { get; private set; }
        public List<string> Calls { get; } = [];

        public async Task WaitAsync(double due, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (due <= Elapsed)
                return;
            var done = new TaskCompletionSource();
            if (!waits.TryGetValue(due, out var list))
                waits[due] = list = [];
            list.Add(done);
            using var registration = ct.Register(() => done.TrySetCanceled(ct));
            await done.Task;
        }

        public void AdvanceNext()
        {
            if (waits.Count == 0)
                return;
            var next = waits.First();
            waits.Remove(next.Key);
            if (next.Value.All(t => t.Task.IsCompleted))
                return;
            // A virtual clock tick just after the deadline avoids subtraction rounding below Due.
            Elapsed = Math.Max(Elapsed, Math.BitIncrement(next.Key));
            foreach (var done in next.Value)
                done.TrySetResult();
        }

        public Task<JsonElement> SetupAsync(CancellationToken ct) =>
            Task.FromResult(saved.GetProperty("setup"));

        public Task<JsonElement> SampleAsync(CancellationToken ct)
        {
            // Existing preflight sample belongs to Setup, so the port supplies 20control+1boundary+13warmup.
            LastSample =
                sample >= 34 ? LastSample
                : sample < 20 ? saved.GetProperty("control")[sample]
                : sample == 20 ? saved.GetProperty("boundary")
                : saved.GetProperty("warmup")[
                    sample == 33 && modeledIdleBoundary ? 11 : sample - 21
                ];
            sample++;
            return Task.FromResult(LastSample);
        }

        public Task CaptureAsync(string name, CancellationToken ct)
        {
            Calls.Add(name);
            return Task.CompletedTask;
        }

        public Task BeginAsync(double began, CancellationToken ct)
        {
            origin = began;
            return Task.CompletedTask;
        }

        public async Task<HotelM2DispatchResult> DispatchAsync(
            HotelM2PlannedJob job,
            CancellationToken ct
        )
        {
            var result = results[job.Id];
            await WaitAsync(origin + result.Completed, ct);
            return job.Id == 26 && PurchaseOutcome is not null
                ? result with
                {
                    Outcome = PurchaseOutcome,
                }
                : result;
        }

        public void CheckResources() { }

        public Task SaveAsync(string name, object value)
        {
            if (name == "operations.json")
                Operations = (HotelM2DispatchResult[])value;
            return Task.CompletedTask;
        }

        public Task CleanupAsync(CancellationToken ct)
        {
            Calls.Add("cleanup");
            return Task.CompletedTask;
        }
    }
}
