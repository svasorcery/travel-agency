using Shouldly;
using Travel.IntegrationContracts.AI.TripsBriefExtraction;
using Travel.Modules.Trips.Application.BriefExtraction;
using Travel.Tests.AiEvals.TripsBriefExtraction;
using Xunit;
using static Travel.Tests.Contract.Trips.TripsBriefEvaluationTestFixture;

namespace Travel.Tests.Contract.Trips;

public sealed class TripsBriefEvaluationDeadlineTests
{
    [Fact]
    public async Task Impossible_next_window_stops_without_waiting_past_authority()
    {
        var clock = new Clock();
        var journal = new SlowJournal(clock);
        var r = await Run(clock, journal, TimeSpan.FromMinutes(10));
        r.Dispatched.ShouldBe(8);
        r.Duration.ShouldBeLessThan(TimeSpan.FromMinutes(10));
        r.Passed.ShouldBeFalse();
    }

    [Fact]
    public async Task Last_result_io_cannot_publish_pass_after_effective_deadline()
    {
        var clock = new Clock();
        var journal = new SlowJournal(clock) { LastResultDelay = TimeSpan.FromMinutes(2) };
        var r = await Run(clock, journal, TimeSpan.FromMinutes(327));
        r.Passed.ShouldBeFalse();
        journal.Stored!.Passed.ShouldBeFalse();
    }

    [Fact]
    public async Task Final_summary_io_is_measured_and_cannot_publish_stale_pass()
    {
        var clock = new Clock();
        var journal = new SlowJournal(clock) { CompleteDelay = TimeSpan.FromMinutes(2) };
        var r = await Run(clock, journal, TimeSpan.FromMinutes(327));
        r.Duration.ShouldBe(TimeSpan.FromTicks(clock.GetTimestamp()));
        r.Passed.ShouldBeFalse();
        journal.Stored!.Passed.ShouldBeFalse();
    }

    private static Task<TripsBriefEvaluationReceipt> Run(
        Clock clock,
        SlowJournal journal,
        TimeSpan authorization
    ) =>
        TripsBriefEvaluationRunner.RunAuthorizedAsync(
            Packet,
            Binding(),
            new Pipeline(clock),
            clock,
            journal,
            new(clock, authorization),
            (delay, ct) =>
            {
                clock.Advance(delay);
                return Task.CompletedTask;
            },
            cancellationToken: TestContext.Current.CancellationToken
        );

    private sealed class SlowJournal(Clock clock) : ITripsBriefEvaluationJournal
    {
        public TimeSpan LastResultDelay;
        public TimeSpan CompleteDelay;
        public TripsBriefEvaluationReceipt? Stored;
        public string EvidenceKind => "mechanical-memory-only";
        public string ManifestSha256 => new('a', 64);

        public Task RecordAttemptAsync(TripsBriefEvalAttempt a, CancellationToken ct) =>
            Task.CompletedTask;

        public Task RecordResultAsync(TripsBriefEvalCaseReceipt r, CancellationToken ct)
        {
            if (r.Id == "H24")
                clock.Advance(LastResultDelay);
            return Task.CompletedTask;
        }

        public Task CompleteAsync(TripsBriefEvaluationReceipt r, CancellationToken ct)
        {
            Stored = r;
            clock.Advance(CompleteDelay);
            return Task.CompletedTask;
        }
    }

    private sealed class Pipeline(Clock clock) : ITripsBriefEvaluationPipeline
    {
        public string ModelId => "fictional-model";

        public Task<TripsBriefParsed> ExtractAsync(TripsBriefRequested r, CancellationToken ct)
        {
            clock.Advance(TimeSpan.FromMilliseconds(100));
            var c = TripBriefCandidateValidator.Analyze(
                new(
                    r.CorrelationId,
                    r.ProcessingPolicyVersion,
                    r.Text,
                    r.Locale,
                    r.ReferenceInstant,
                    r.ReferenceZone,
                    r.Deadline
                )
            );
            return Task.FromResult(
                new TripsBriefParsed(
                    r.CorrelationId,
                    r.ProcessingPolicyVersion,
                    "Ready",
                    c.Fields,
                    c.Issues,
                    1,
                    1,
                    .000006m,
                    ModelId
                )
            );
        }
    }
}
