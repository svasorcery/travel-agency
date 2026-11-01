using System.Text.Json;
using Shouldly;
using Travel.Hotels.Fixture;

namespace Travel.Modules.Hotels.Tests.Integration;

public sealed class HotelH6PolicyTests
{
    private const string Nonce = "0123456789abcdef0123456789abcdef";
    private static readonly HotelH6Target Target = new(42, 123456);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Exactly_two_snapshots_are_pid_bound_and_only_the_owned_instance_is_stopped()
    {
        var sys = new Fake();
        var trace = new HotelsH6Trace(sys, Nonce, Path.Combine(Path.GetTempPath(), "owned"));
        await trace.AttachAsync(Target, Ct);
        await trace.SnapshotAsync("A", Ct);
        await trace.SnapshotAsync("B", Ct);
        var result = await trace.FinishAsync(Ct);
        result.TrackingClosed.ShouldBeTrue();
        result.SessionClosed.ShouldBeTrue();
        result.Snapshots.ShouldBe(2);
        result.EtlSaved.ShouldBeTrue();
        result.Errors.ShouldBeEmpty();
        sys.Calls.Select(c => c.Op)
            .ShouldBe(new[] { "enable", "start", "A", "B", "disable", "stop" });
        sys.Calls.Single(c => c.Op == "enable")
            .Args.ShouldBe(new[] { "-snapshotconfig", "heap", "-pid", "42", "enable" });
        foreach (var c in sys.Calls.Where(c => c.Op is "start" or "A" or "B" or "stop"))
            c.Args.TakeLast(2).ShouldBe(new[] { "-instancename", "TravelHotelsH6-" + Nonce });
        sys.Calls.ShouldAllBe(c => !c.Args.Contains("-name") && !c.Args.Contains("-markerflush"));
    }

    [Theory]
    [InlineData("before-attach")]
    [InlineData("B-first")]
    [InlineData("repeat-A")]
    [InlineData("third")]
    public async Task Invalid_sequence_performs_no_additional_wpr_action(string kind)
    {
        var sys = new Fake();
        var trace = new HotelsH6Trace(sys, Nonce, Path.Combine(Path.GetTempPath(), "owned"));
        if (kind != "before-attach")
            await trace.AttachAsync(Target, Ct);
        if (kind is "repeat-A" or "third")
            await trace.SnapshotAsync("A", Ct);
        if (kind == "third")
            await trace.SnapshotAsync("B", Ct);
        var count = sys.Calls.Count;
        await Should.ThrowAsync<InvalidOperationException>(() =>
            trace.SnapshotAsync(kind == "B-first" ? "B" : "A", Ct)
        );
        sys.Calls.Count.ShouldBe(count);
        await trace.FinishAsync(Ct);
    }

    [Theory]
    [InlineData("enable")]
    [InlineData("start")]
    [InlineData("A")]
    [InlineData("B")]
    public async Task Ambiguous_action_failure_keeps_cleanup_independent_and_never_retries(
        string fault
    )
    {
        var sys = new Fake { Fault = fault };
        var trace = new HotelsH6Trace(sys, Nonce, Path.Combine(Path.GetTempPath(), "owned"));
        await Should.ThrowAsync<IOException>(async () =>
        {
            await trace.AttachAsync(Target, Ct);
            await trace.SnapshotAsync("A", Ct);
            await trace.SnapshotAsync("B", Ct);
        });
        var result = await trace.FinishAsync(Ct);
        sys.Calls.Count(c => c.Op == fault).ShouldBe(1);
        sys.Calls.ShouldContain(c => c.Op == "disable");
        result.Snapshots.ShouldBeLessThan(2);
    }

    [Fact]
    public async Task Changed_target_is_rejected_before_enable()
    {
        var sys = new Fake { IdentityValid = false };
        var trace = new HotelsH6Trace(sys, Nonce, Path.Combine(Path.GetTempPath(), "owned"));
        await Should.ThrowAsync<InvalidDataException>(() => trace.AttachAsync(Target, Ct));
        sys.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Reused_or_exited_pid_is_not_disabled_but_own_trace_is_stopped()
    {
        var sys = new Fake();
        var trace = new HotelsH6Trace(sys, Nonce, Path.Combine(Path.GetTempPath(), "owned"));
        await trace.AttachAsync(Target, Ct);
        sys.Exited = true;
        var result = await trace.FinishAsync(Ct);
        sys.Calls.ShouldNotContain(c => c.Op == "disable");
        sys.Calls.ShouldContain(c => c.Op == "stop");
        result.TrackingClosed.ShouldBeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Stop_failure_cancels_only_when_own_session_is_confirmed(bool unknown)
    {
        var sys = new Fake { Fault = "stop" };
        var trace = new HotelsH6Trace(sys, Nonce, Path.Combine(Path.GetTempPath(), "owned"));
        await trace.AttachAsync(Target, Ct);
        sys.Unknown = unknown;
        var result = await trace.FinishAsync(Ct);
        sys.Calls.Count(c => c.Op == "cancel").ShouldBe(unknown ? 0 : 1);
        result.Errors.ShouldNotBeEmpty();
        result.SessionClosed.ShouldBe(!unknown);
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData(" -name dotnet.exe")]
    [InlineData("")]
    public async Task Nonce_injection_is_rejected_before_wpr(string nonce)
    {
        var sys = new Fake();
        await Should.ThrowAsync<ArgumentException>(() =>
            new HotelsH6Trace(sys, nonce, Path.Combine(Path.GetTempPath(), "owned")).AttachAsync(
                Target,
                Ct
            )
        );
        sys.Calls.ShouldBeEmpty();
    }

    [Fact]
    public void Comparison_ignores_observer_memory_and_event_drain_but_preserves_future_work()
    {
        var before = Sample();
        var after = Sample(memory: 999);
        HotelsH6Comparison.Compare(before, after).Comparable.ShouldBeTrue();
        var changed = HotelsH6Comparison.Compare(before, Sample(future: 16));
        changed.Comparable.ShouldBeFalse();
        changed.Changed.ShouldContain("metrics.futureWork");
    }

    [Theory]
    [InlineData("starts")]
    [InlineData("effects")]
    [InlineData("retained")]
    [InlineData("active")]
    [InlineData("due")]
    [InlineData("inbox")]
    public void Background_activity_makes_post_capture_comparison_ineligible(string changed)
    {
        HotelsH6Comparison.Compare(Sample(), Sample(changed: changed)).Comparable.ShouldBeFalse();
    }

    internal static JsonElement Sample(int memory = 1, int future = 17, string? changed = null) =>
        JsonSerializer.SerializeToElement(
            new
            {
                sourceMeasurements = new[] { "duffel-stays", "hotelbeds" }.Select(p => new
                {
                    provider = p,
                    starts = changed == "starts" ? 2 : 1,
                    completions = 1,
                    active = changed == "active" ? 1 : 0,
                    overflow = false,
                    unknownEffects = 0,
                    effects = new Dictionary<string, int>
                    {
                        ["reference"] = changed == "effects" ? 2 : 1,
                    },
                    retained = new
                    {
                        rates = changed == "retained" ? 3 : 2,
                        quotes = 1,
                        bookings = 1,
                    },
                    events = Array.Empty<object>(),
                }),
                sourceCapacity = new[] { "duffel-stays", "hotelbeds" }.Select(p => new
                {
                    provider = p,
                    releases = 1,
                    capacity = new
                    {
                        totalStarts = 1,
                        foregroundActive = 0,
                        recoveryActive = 0,
                    },
                }),
                metrics = new
                {
                    reservations = 38,
                    retainedInboxRows = 1,
                    retainedInboxCiphertextBytes = 476,
                    cacheEntries = 128,
                    cacheBytes = 100,
                    pendingInbox = changed == "inbox" ? 1 : 0,
                    dueWork = changed == "due" ? 1 : 0,
                    futureWork = future,
                    managedHeap = memory,
                },
            }
        );

    internal sealed class Fake : IHotelH6TraceSystem
    {
        public List<(string Op, string[] Args)> Calls { get; } = [];
        public string? Fault { get; init; }
        public bool IdentityValid { get; init; } = true;
        public bool Exited { get; set; }
        public bool Unknown { get; set; }
        private bool active;
        public bool EtlExists { get; private set; }

        public void ValidateTarget(HotelH6Target target, bool cleanup)
        {
            if (!IdentityValid)
                throw new InvalidDataException();
        }

        public bool TargetExited(HotelH6Target target) => Exited;

        public Task RunAsync(
            string operation,
            string[] arguments,
            int seconds,
            CancellationToken ct
        )
        {
            Calls.Add((operation, arguments));
            if (operation == "start")
                active = true; // A failed/timeout start may have taken effect.
            if (Fault == operation)
                throw new IOException(operation);
            if (operation is "stop" or "cancel")
                active = false;
            if (operation == "stop")
                EtlExists = true;
            return Task.CompletedTask;
        }

        public Task<HotelH6SessionStatus> SessionAsync(string instance, CancellationToken ct) =>
            Task.FromResult(
                Unknown ? HotelH6SessionStatus.Unknown
                : active ? HotelH6SessionStatus.Owned
                : HotelH6SessionStatus.Absent
            );
    }
}
