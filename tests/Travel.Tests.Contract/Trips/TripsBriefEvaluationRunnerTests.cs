using Shouldly;
using Travel.IntegrationContracts.AI.TripsBriefExtraction;
using Travel.Modules.Trips.Application.BriefExtraction;
using Travel.Tests.AiEvals.TripsBriefExtraction;
using Xunit;

namespace Travel.Tests.Contract.Trips;

[Trait("Category", "Contract")]
public sealed class TripsBriefEvaluationRunnerTests
{
    private static string Fixture =>
        Path.Combine(AppContext.BaseDirectory, "TripsBriefExtraction", "qualification-cases.json");

    private static TripsBriefEvaluationBinding Binding() =>
        new(
            Guid.NewGuid(),
            "fictional-mechanics-only",
            true,
            TripsBriefEvaluationPacket.HashFile(Fixture),
            new string('a', 64),
            TripsBriefEvaluationRunner.EmbeddedPromptSha256(),
            "fictional-model",
            1m,
            5m,
            1m,
            48,
            RunReceiptDirectory: Path.Combine(
                Path.GetTempPath(),
                "mechanical-run-authorizations",
                Guid.NewGuid().ToString("N")
            )
        );

    [Fact]
    public async Task Unauthorized_or_unbound_run_has_zero_dispatches()
    {
        var clock = new Clock();
        var pipe = new Pipeline(clock);
        await Should.ThrowAsync<InvalidOperationException>(() =>
            TripsBriefEvaluationRunner.RunAuthorizedAsync(
                Fixture,
                Binding() with
                {
                    Authorized = false,
                },
                pipe,
                clock,
                new MechanicalJournal(),
                new TripsBriefEvaluationSchedule(clock),
                cancellationToken: TestContext.Current.CancellationToken
            )
        );
        await Should.ThrowAsync<InvalidOperationException>(() =>
            TripsBriefEvaluationRunner.RunAuthorizedAsync(
                Fixture,
                Binding() with
                {
                    PacketSha256 = new string('0', 64),
                },
                pipe,
                clock,
                new MechanicalJournal(),
                new TripsBriefEvaluationSchedule(clock),
                cancellationToken: TestContext.Current.CancellationToken
            )
        );
        await Should.ThrowAsync<InvalidOperationException>(() =>
            TripsBriefEvaluationRunner.RunAuthorizedAsync(
                Fixture,
                Binding() with
                {
                    PromptSha256 = new string('0', 64),
                },
                pipe,
                clock,
                new MechanicalJournal(),
                new TripsBriefEvaluationSchedule(clock),
                cancellationToken: TestContext.Current.CancellationToken
            )
        );
        pipe.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task Fake_full_run_keeps48_and24_denominators_and_six_paced_windows()
    {
        var clock = new Clock();
        var pipe = new Pipeline(clock);
        var result = await Run(Binding(), pipe, clock);
        result.Cases.Length.ShouldBe(48);
        result.Dispatched.ShouldBe(48);
        result.QualificationExact.ShouldBe(24);
        result.UsefulExact.ShouldBe(12);
        result.UnsafeCases.ShouldBe(0);
        result.Passed.ShouldBeTrue();
        result.ComponentP95Milliseconds.ShouldBe(100);
        result.Duration.ShouldBeGreaterThan(TimeSpan.FromMinutes(325));
        result.Duration.ShouldBeLessThan(TimeSpan.FromHours(6));
        for (var i = 1; i < pipe.Starts.Count; i++)
            (pipe.Starts[i] - pipe.Starts[i - 1]).ShouldBeGreaterThanOrEqualTo(
                TimeSpan.FromSeconds(11)
            );
        pipe.ReferenceInstants.Distinct()
            .Single()
            .ShouldBe(DateTimeOffset.Parse("2026-10-27T21:30:00Z"));
        pipe.Starts[0].ShouldNotBe(pipe.ReferenceInstants[0]);
    }

    [Theory]
    [InlineData("UnknownCost")]
    [InlineData("WrongModel")]
    public async Task Accounting_uncertainty_stops_without_replay_and_keeps_unexecuted_cases(
        string mode
    )
    {
        var clock = new Clock();
        var pipe = new Pipeline(clock) { Mode = mode };
        var result = await Run(Binding(), pipe, clock);
        pipe.Calls.ShouldBe(1);
        result.Cases.Length.ShouldBe(48);
        result.Cases.Count(x => x.Status == "NotRun").ShouldBe(47);
        result.ActualSpendUsd.ShouldBeNull();
        result.Passed.ShouldBeFalse();
    }

    [Fact]
    public async Task Total_allowance_and_stop_marker_are_checked_before_dispatch()
    {
        var clock = new Clock();
        var pipe = new Pipeline(clock);
        var result = await Run(Binding() with { MaxTotalCostUsd = .000001m }, pipe, clock);
        pipe.Calls.ShouldBe(0);
        result.Cases.Length.ShouldBe(48);
        result.Passed.ShouldBeFalse();
        var stopped = await TripsBriefEvaluationRunner.RunAuthorizedAsync(
            Fixture,
            Binding(),
            pipe,
            clock,
            new MechanicalJournal(),
            new TripsBriefEvaluationSchedule(clock),
            (d, ct) =>
            {
                clock.Advance(d);
                return Task.CompletedTask;
            },
            () => true,
            TestContext.Current.CancellationToken
        );
        pipe.Calls.ShouldBe(0);
        stopped.StopReason.ShouldBe("StopRequested");
    }

    [Fact]
    public async Task Authorized_call_count_does_not_shrink_original_denominator()
    {
        var clock = new Clock();
        var pipe = new Pipeline(clock);
        var result = await Run(Binding() with { MaxCalls = 1 }, pipe, clock);
        result.Dispatched.ShouldBe(1);
        result.Cases.Length.ShouldBe(48);
        result.Cases.Count(x => x.Status == "NotRun").ShouldBe(47);
        result.Passed.ShouldBeFalse();
    }

    [Fact]
    public async Task Always_missing_pipeline_cannot_win_quality_by_safe_fallback()
    {
        var clock = new Clock();
        var pipe = new Pipeline(clock) { Mode = "Missing" };
        var result = await Run(Binding(), pipe, clock);
        result.Dispatched.ShouldBe(48);
        result.UsefulExact.ShouldBe(0);
        result.Passed.ShouldBeFalse();
    }

    [Fact]
    public async Task Changed_model_after_scheduled_wait_is_rejected_before_dispatch()
    {
        var clock = new Clock();
        var pipe = new Pipeline(clock);
        var result = await TripsBriefEvaluationRunner.RunAuthorizedAsync(
            Fixture,
            Binding(),
            pipe,
            clock,
            new MechanicalJournal(),
            new TripsBriefEvaluationSchedule(clock),
            (delay, ct) =>
            {
                clock.Advance(delay);
                if (delay > TimeSpan.FromMinutes(1))
                    pipe.ModelId = "different-model";
                return Task.CompletedTask;
            },
            cancellationToken: TestContext.Current.CancellationToken
        );
        pipe.Calls.ShouldBe(8);
        result.Passed.ShouldBeFalse();
    }

    [Fact]
    public async Task Wait_callback_cannot_silently_skip_the_predeclared_pacing()
    {
        var clock = new Clock();
        var pipe = new Pipeline(clock);
        var result = await TripsBriefEvaluationRunner.RunAuthorizedAsync(
            Fixture,
            Binding(),
            pipe,
            clock,
            new MechanicalJournal(),
            new TripsBriefEvaluationSchedule(clock),
            (delay, ct) => Task.CompletedTask,
            cancellationToken: TestContext.Current.CancellationToken
        );
        pipe.Calls.ShouldBe(1);
        result.Passed.ShouldBeFalse();
    }

    [Fact]
    public async Task Future24_case_packet_requires_independent_custody_binding_and_uses_three_windows()
    {
        // Schema simulation reuses public inputs; this is NOT a new independent holdout.
        var known = TripsBriefEvaluationPacket.Load(Fixture);
        var simulated = known with
        {
            Kind = "sealed-fictional-holdout",
            SourceCorpusRowsSha256 = new string('c', 64),
            Cases = known
                .Cases.Skip(24)
                .Select(
                    (c, i) => c with { Id = "E" + (i + 1).ToString("D2"), Partition = "holdout" }
                )
                .ToArray(),
        };
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "mechanical-24-" + Guid.NewGuid().ToString("N") + ".json"
        );
        await File.WriteAllTextAsync(
            path,
            System.Text.Json.JsonSerializer.Serialize(
                simulated,
                new System.Text.Json.JsonSerializerOptions(
                    System.Text.Json.JsonSerializerDefaults.Web
                )
            ),
            TestContext.Current.CancellationToken
        );
        try
        {
            var binding = Binding() with
            {
                PacketSha256 = TripsBriefEvaluationPacket.HashFile(path),
                MaxCalls = 24,
            };
            var clock = new Clock();
            var pipe = new Pipeline(clock);
            await Should.ThrowAsync<InvalidOperationException>(() =>
                TripsBriefEvaluationRunner.RunAuthorizedAsync(
                    path,
                    binding,
                    pipe,
                    clock,
                    new MechanicalJournal(),
                    new TripsBriefEvaluationSchedule(clock),
                    cancellationToken: TestContext.Current.CancellationToken
                )
            );
            pipe.Calls.ShouldBe(0);
            var result = await TripsBriefEvaluationRunner.RunAuthorizedAsync(
                path,
                binding with
                {
                    HoldoutAttestationId = "SCHEMA_TEST_ONLY_NOT_INDEPENDENT",
                },
                pipe,
                clock,
                new MechanicalJournal(),
                new TripsBriefEvaluationSchedule(clock),
                (d, ct) =>
                {
                    clock.Advance(d);
                    return Task.CompletedTask;
                },
                cancellationToken: TestContext.Current.CancellationToken
            );
            result.Cases.Length.ShouldBe(24);
            result.Dispatched.ShouldBe(24);
            result.Duration.ShouldBeLessThan(TimeSpan.FromHours(3));
            result.Duration.ShouldBeGreaterThan(TimeSpan.FromMinutes(130));
            result.QualificationExact.ShouldBe(24);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Same_authorization_cannot_be_reused_after_failure_or_process_restart()
    {
        var clock = new Clock();
        var pipe = new Pipeline(clock) { Mode = "UnknownCost" };
        var binding = Binding();
        var first = await Run(binding, pipe, clock);
        first.Passed.ShouldBeFalse();
        pipe.Calls.ShouldBe(1);
        await Should.ThrowAsync<InvalidOperationException>(() => Run(binding, pipe, clock));
        await Should.ThrowAsync<InvalidOperationException>(() =>
            Run(binding with { RunId = Guid.NewGuid() }, pipe, clock)
        );
        pipe.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task New_authorization_can_target_exact_remaining_ids_without_replaying_prior_case()
    {
        var clock = new Clock();
        var pipe = new Pipeline(clock);
        var first = Binding() with { MaxCalls = 1 };
        await Run(first, pipe, clock);
        var second = await Run(
            first with
            {
                RunId = Guid.NewGuid(),
                AuthorizationId = "new-fictional-approval",
                ApprovedCaseIds = ["D02"],
            },
            pipe,
            clock
        );
        pipe.Calls.ShouldBe(2);
        second.Cases.Single(x => x.Dispatched).Id.ShouldBe("D02");
        second.Cases.Length.ShouldBe(48);
        second.Passed.ShouldBeFalse();
    }

    private static Task<TripsBriefEvaluationReceipt> Run(
        TripsBriefEvaluationBinding b,
        Pipeline p,
        Clock c
    ) =>
        TripsBriefEvaluationRunner.RunAuthorizedAsync(
            Fixture,
            b,
            p,
            c,
            new MechanicalJournal(),
            new TripsBriefEvaluationSchedule(c),
            (d, ct) =>
            {
                c.Advance(d);
                return Task.CompletedTask;
            },
            cancellationToken: TestContext.Current.CancellationToken
        );

    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset now = new(2026, 11, 30, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => now;

        public override long GetTimestamp() => now.UtcTicks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public void Advance(TimeSpan value) => now += value;
    }

    private sealed class Pipeline(Clock clock) : ITripsBriefEvaluationPipeline
    {
        public string ModelId { get; set; } = "fictional-model";
        public int Calls;
        public string Mode = "Ready";
        public readonly List<DateTimeOffset> Starts = [];
        public readonly List<DateTimeOffset> ReferenceInstants = [];

        public Task<TripsBriefParsed> ExtractAsync(
            TripsBriefRequested request,
            CancellationToken ct
        )
        {
            Calls++;
            Starts.Add(clock.GetUtcNow());
            ReferenceInstants.Add(request.ReferenceInstant);
            clock.Advance(TimeSpan.FromMilliseconds(100));
            var candidate = TripBriefCandidateValidator.Analyze(
                new(
                    request.CorrelationId,
                    request.ProcessingPolicyVersion,
                    request.Text,
                    request.Locale,
                    request.ReferenceInstant,
                    request.ReferenceZone,
                    request.Deadline
                )
            );
            var missing = new TripsBriefField("missing", null, [], []);
            var fields =
                Mode == "Missing"
                    ? new TripsBriefFields(
                        missing,
                        missing,
                        missing,
                        missing,
                        missing,
                        missing,
                        missing
                    )
                    : candidate.Fields;
            return Task.FromResult(
                new TripsBriefParsed(
                    request.CorrelationId,
                    request.ProcessingPolicyVersion,
                    "Ready",
                    fields,
                    Mode == "Missing" ? [] : candidate.Issues,
                    1,
                    1,
                    Mode == "UnknownCost" ? null : .000006m,
                    Mode == "WrongModel" ? "other-model" : "fictional-model"
                )
            );
        }
    }
}
