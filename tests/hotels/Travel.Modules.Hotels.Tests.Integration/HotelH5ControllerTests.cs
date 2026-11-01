using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Travel.Hotels.Fixture;

namespace Travel.Modules.Hotels.Tests.Integration;

public sealed class HotelH5ControllerTests
{
    private static readonly HotelM2LoadProfile Profile = new("S", new(2026, 11, 6));

    [Fact]
    public void Prefix_retains_original_ids_dependencies_sources_and_exact_56_denominator()
    {
        var jobs = HotelsH5Controller.Prefix(Profile);
        jobs.ShouldBe(new HotelM2LoadSchedule(Profile).Jobs.Where(j => j.Due < 10).ToArray());
        jobs.Length.ShouldBe(56);
        jobs.Count(j => j.Kind == "status").ShouldBe(50);
        jobs.Single(j => j.Kind == "create").Provider.ShouldBe("hotelbeds");
        jobs.Where(j => j.Prerequisite != null)
            .ShouldAllBe(j => jobs.Any(p => p.Id == j.Prerequisite));
        Should.Throw<InvalidDataException>(() =>
            HotelsH5Controller.Prefix(new("L", Profile.CheckIn))
        );
    }

    [Fact]
    public async Task Complete_flow_has_exact_maps_samples_and_no_public_work_before_boundary()
    {
        var port = new Port();
        var receipt = await Drive(port);
        receipt.AcquisitionComplete.ShouldBeTrue();
        receipt.CleanupComplete.ShouldBeTrue();
        receipt.IpcSamples.ShouldBe(35);
        receipt.Maps.ShouldBe(new[] { "P0", "P1", "P2" });
        receipt.Ledger.Planned.ShouldBe(56);
        receipt.Ledger.Counts["Response"].ShouldBe(56);
        port.Calls.IndexOf("begin").ShouldBeGreaterThan(port.Calls.IndexOf("sample-21"));
        port.Calls.Count(x => x == "cleanup").ShouldBe(1);
        port.Calls.ShouldNotContain("facts");
    }

    [Theory]
    [InlineData("control")]
    [InlineData("boundary")]
    public async Task Background_source_work_prevents_begin_and_all_56_jobs_stay_visible(
        string stage
    )
    {
        var port = new Port { BackgroundAt = stage == "control" ? 1 : 21 };
        var receipt = await Drive(port);
        receipt.AcquisitionComplete.ShouldBeFalse();
        receipt.Ledger.Counts["NotRun"].ShouldBe(56);
        port.Calls.ShouldNotContain("begin");
        receipt.CleanupComplete.ShouldBeTrue();
    }

    [Theory]
    [InlineData("active")]
    [InlineData("due")]
    [InlineData("inbox")]
    [InlineData("unfinished")]
    [InlineData("semantic")]
    public async Task Non_quiescent_or_unexpected_terminal_at_12_omits_P2_without_extending(
        string kind
    )
    {
        var port = new Port { Fault = kind };
        var receipt = await Drive(port);
        receipt.AcquisitionComplete.ShouldBeFalse();
        receipt.Maps.ShouldNotContain("P2");
        receipt.Ledger.Planned.ShouldBe(56);
        port.WarmupAtCleanup.ShouldBeLessThan(13);
        receipt.CleanupComplete.ShouldBeTrue();
    }

    [Theory]
    [InlineData("setup")]
    [InlineData("P0")]
    [InlineData("P1")]
    [InlineData("P2")]
    [InlineData("sample")]
    [InlineData("dispatch")]
    [InlineData("save")]
    [InlineData("resources")]
    [InlineData("cleanup")]
    public async Task Each_failure_preserves_denominator_and_never_claims_complete(string stage)
    {
        var port = new Port { Fault = stage };
        var receipt = await Drive(port);
        (receipt.AcquisitionComplete && receipt.CleanupComplete).ShouldBeFalse();
        receipt.Ledger.Planned.ShouldBe(56);
        port.Calls.Count(x => x == "cleanup").ShouldBe(1);
        if (stage == "cleanup")
            receipt.CleanupComplete.ShouldBeFalse();
    }

    [Fact]
    public async Task Cancelled_owner_stops_dispatch_and_still_attempts_cleanup()
    {
        var port = new Port();
        using var cts = new CancellationTokenSource();
        var pending = HotelsH5Controller.RunAsync(port, Profile, cts.Token);
        cts.Cancel();
        var receipt = await pending;
        receipt.AcquisitionComplete.ShouldBeFalse();
        receipt.CleanupComplete.ShouldBeTrue();
        receipt.Ledger.Counts["NotRun"].ShouldBe(56);
    }

    [Fact]
    public async Task Setup_failure_after_preflight_retains_the_observed_sample_count()
    {
        var port = new Port { Fault = "after-preflight" };
        var receipt = await Drive(port);
        receipt.IpcSamples.ShouldBe(1);
        receipt.AcquisitionComplete.ShouldBeFalse();
    }

    [Fact]
    public async Task Failed_warmup_keeps_completed_response_details_and_denominator()
    {
        var port = new Port { Fault = "semantic" };
        var receipt = await Drive(port);
        receipt.AcquisitionComplete.ShouldBeFalse();
        port.Operations.ShouldNotBeNull();
        port.Operations!.Length.ShouldBeGreaterThan(0);
        port.Operations[0].Status.ShouldBe(200);
        receipt.Ledger.Planned.ShouldBe(56);
    }

    private static async Task<HotelH5Receipt> Drive(Port port)
    {
        var run = HotelsH5Controller.RunAsync(port, Profile, TestContext.Current.CancellationToken);
        for (var i = 0; !run.IsCompleted && i < 280; i++)
        {
            port.Time.Advance(TimeSpan.FromMilliseconds(250));
            await Task.Delay(1, TestContext.Current.CancellationToken);
        }
        return await run.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    private sealed class Port : IHotelsH5Port
    {
        public FakeTimeProvider Time { get; } = new();
        private readonly DateTimeOffset start;
        public List<string> Calls { get; } = [];
        public int PreflightSamples { get; private set; }
        public int PreflightSampleAttempts => PreflightSamples;
        public HotelM2DispatchResult[]? Operations { get; private set; }
        public string? Fault { get; init; }
        public int BackgroundAt { get; init; } = -1;
        private int samples;
        private double begin = -1;
        public double WarmupAtCleanup { get; private set; }

        public Port()
        {
            start = Time.GetUtcNow();
        }

        public double Elapsed => (Time.GetUtcNow() - start).TotalSeconds;

        public Task WaitAsync(double due, CancellationToken ct) =>
            HotelsLoadTiming.WaitUntilAsync(
                due,
                () => Elapsed,
                ct,
                (delay, token) => Task.Delay(delay, Time, token)
            );

        private void Fail(string stage)
        {
            if (Fault == stage)
                throw new InvalidDataException(stage);
        }

        public Task<JsonElement> SetupAsync(CancellationToken ct)
        {
            Calls.Add("setup");
            Fail("setup");
            PreflightSamples = 1;
            Fail("after-preflight");
            return Task.FromResult(
                JsonSerializer.SerializeToElement(new { initialSources = Sources(false) }, Json)
            );
        }

        public Task<JsonElement> SampleAsync(CancellationToken ct)
        {
            samples++;
            Calls.Add("sample-" + samples);
            Fail("sample");
            return Task.FromResult(
                JsonSerializer.SerializeToElement(
                    new
                    {
                        sourceMeasurements = Sources(samples == BackgroundAt),
                        sourceCapacity = new[] { "duffel-stays", "hotelbeds" }.Select(p => new
                        {
                            provider = p,
                            capacity = new
                            {
                                totalStarts = 0,
                                foregroundActive = 0,
                                recoveryActive = 0,
                            },
                        }),
                        metrics = new
                        {
                            at = begin < 0 ? 0 : Elapsed - begin,
                            pendingInbox = Fault == "inbox" && begin >= 0 ? 1 : 0,
                            dueWork = Fault == "due" && begin >= 0 ? 1 : 0,
                            futureWork = 12,
                        },
                    },
                    Json
                )
            );
        }

        private object[] Sources(bool changed) =>
            new[] { "duffel-stays", "hotelbeds" }
                .Select(p =>
                    (object)
                        new
                        {
                            provider = p,
                            starts = changed ? 57 : 56,
                            completions = 56,
                            active = Fault == "active" && begin >= 0 ? 1 : 0,
                            overflow = false,
                            unknownEffects = 0,
                            effects = new Dictionary<string, int> { ["seed"] = 1 },
                        }
                )
                .ToArray();

        public Task CaptureAsync(string name, CancellationToken ct)
        {
            Calls.Add(name);
            Fail(name);
            return Task.CompletedTask;
        }

        public Task BeginAsync(double origin, CancellationToken ct)
        {
            Calls.Add("begin");
            begin = origin;
            return Task.CompletedTask;
        }

        public async Task<HotelM2DispatchResult> DispatchAsync(
            HotelM2PlannedJob job,
            CancellationToken ct
        )
        {
            Fail("dispatch");
            if (Fault == "unfinished" && job.Id == 0)
                await Task.Delay(Timeout.Infinite, ct);
            return new(
                job.Id,
                "Response",
                Elapsed - begin,
                Elapsed - begin,
                200,
                job.Kind == "create" ? "Confirmed" : null,
                Fault != "semantic",
                null,
                job.ReservationId,
                job.Provider,
                true
            );
        }

        public void CheckResources()
        {
            Fail("resources");
        }

        public Task SaveAsync(string name, object value)
        {
            Fail("save");
            if (name == "operations.json")
                Operations = (HotelM2DispatchResult[])value;
            return Task.CompletedTask;
        }

        public Task CleanupAsync(CancellationToken ct)
        {
            Calls.Add("cleanup");
            WarmupAtCleanup = begin < 0 ? 0 : Elapsed - begin;
            Fail("cleanup");
            return Task.CompletedTask;
        }

        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    }
}
