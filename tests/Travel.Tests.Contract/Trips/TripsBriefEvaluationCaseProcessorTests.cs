using System.Text.Json;
using Shouldly;
using Travel.IntegrationContracts.AI.TripsBriefExtraction;
using Travel.Modules.Trips.Application.BriefExtraction;
using Travel.Tests.AiEvals.TripsBriefExtraction;
using Xunit;
using static Travel.Tests.Contract.Trips.TripsBriefEvaluationTestFixture;

namespace Travel.Tests.Contract.Trips;

public sealed class TripsBriefEvaluationCaseProcessorTests
{
    [Fact]
    public async Task Normalization_time_and_final_deadline_are_included()
    {
        var clock = new Clock();
        var c = Case("D01");
        var input = TripsBriefEvaluationPacket.Input(c, Now);
        var processor = new TripsBriefEvaluationCaseProcessor(
            Binding(),
            clock,
            (i, r) =>
            {
                clock.Advance(TimeSpan.FromMilliseconds(5100));
                return TripBriefCandidateValidator.Validate(i, r);
            }
        );
        var result = await processor.ProcessAsync(
            c,
            input,
            new Pipeline(clock),
            TestContext.Current.CancellationToken
        );
        result.ComponentMilliseconds.ShouldBe(5200);
        result.Exact.ShouldBeFalse();
        result.Fields.ShouldBeNull();
        result.Status.ShouldBe("ComponentDeadlineFailure");
        result.CostUsd.ShouldBe(.000006m);
    }

    [Fact]
    public async Task Rejected_H09_is_not_raw_safe_zero()
    {
        var c = Case("H09");
        var clock = new Clock();
        var r = await new TripsBriefEvaluationCaseProcessor(Binding(), clock).ProcessAsync(
            c,
            TripsBriefEvaluationPacket.Input(c, Now),
            new Pipeline(clock) { HallucinateCity = true },
            TestContext.Current.CancellationToken
        );
        r.Exact.ShouldBeFalse();
        r.Unsafe.ShouldBeFalse();
        r.Diagnostics!.Observation.ShouldBe("structured-proposal-observed");
        r.Diagnostics.RejectedProposal.ShouldBeTrue();
    }

    [Fact]
    public async Task Recovered_issue_is_attributed_to_normalizer()
    {
        var c = Case("D08");
        var clock = new Clock();
        var r = await new TripsBriefEvaluationCaseProcessor(Binding(), clock).ProcessAsync(
            c,
            TripsBriefEvaluationPacket.Input(c, Now),
            new Pipeline(clock) { OmitIssues = true },
            TestContext.Current.CancellationToken
        );
        r.Exact.ShouldBeTrue();
        r.Diagnostics!.NormalizerAddedIssueCodes.ShouldContain("reversedDates");
    }

    [Fact]
    public void Existing_stricter_aggregate_guards_are_preserved()
    {
        var all = Enumerable
            .Range(0, 48)
            .Select(i => new TripsBriefEvalCaseReceipt(
                "D01",
                i < 24 ? "development" : "qualification",
                "Ready",
                true,
                true,
                i < 12,
                false,
                i == 0 ? 5001 : 1,
                1,
                1,
                .000006m
            ))
            .ToArray();
        TripsBriefEvaluationCaseProcessor
            .Accepted(all, null, false, TimeSpan.FromHours(5), TimeSpan.FromHours(6))
            .ShouldBeFalse();
        all[0] = all[0] with { ComponentMilliseconds = 1, Unsafe = true };
        TripsBriefEvaluationCaseProcessor
            .Accepted(all, null, false, TimeSpan.FromHours(5), TimeSpan.FromHours(6))
            .ShouldBeFalse();
    }

    private static TripsBriefEvalCase Case(string id) =>
        TripsBriefEvaluationPacket.Load(Packet).Cases.Single(c => c.Id == id);

    private sealed class Pipeline(Clock clock) : ITripsBriefEvaluationPipeline
    {
        public string ModelId => "fictional-model";
        public bool OmitIssues;
        public bool HallucinateCity;

        public Task<TripsBriefParsed> ExtractAsync(TripsBriefRequested r, CancellationToken ct)
        {
            clock.Advance(TimeSpan.FromMilliseconds(100));
            var analyzed = TripBriefCandidateValidator.Analyze(
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
            var fields = analyzed.Fields;
            if (HallucinateCity)
                fields = fields with
                {
                    DestinationId = new(
                        "extracted",
                        JsonSerializer.SerializeToElement("paris"),
                        [new(2, 8)],
                        []
                    ),
                };
            return Task.FromResult(
                new TripsBriefParsed(
                    r.CorrelationId,
                    r.ProcessingPolicyVersion,
                    "Ready",
                    fields,
                    OmitIssues ? [] : analyzed.Issues,
                    1,
                    1,
                    .000006m,
                    ModelId
                )
            );
        }
    }
}
