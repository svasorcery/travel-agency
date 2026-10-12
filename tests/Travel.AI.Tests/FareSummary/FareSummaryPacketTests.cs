using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Travel.AI.FareSummary;
using Travel.IntegrationContracts.AI.FareSummary;
using Travel.Modules.Flights.Application.FareRules;
using Travel.Tests.AiEvals.Flights.FareSummary;
using Xunit;

namespace Travel.AI.Tests.FareSummary;

public sealed class FareSummaryPacketTests
{
    public static IEnumerable<object[]> Cases =>
        FareSummaryEvalPacket.Load().Cases.Select(c => new object[] { c.Id });
    public static IEnumerable<object[]> AdversarialCases =>
        FareSummaryEvalPacket.LoadAdversarial().Cases.Select(c => new object[] { c.Id });

    [Fact]
    public void Frozen_manifest_has_the_required_split_and_no_paid_acceptance()
    {
        var manifest = FareSummaryEvalPacket.Load();
        FareSummaryEvalPacket.ValidateManifest(manifest);
        manifest.Model.ShouldBe("UNSELECTED");
        manifest.Pricing.ShouldBe("UNVERIFIED");
        manifest.PaidRunExecuted.ShouldBeFalse();
        FareSummaryEvalPacket.LoadAdversarial().Cases.Length.ShouldBeGreaterThanOrEqualTo(12);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task All_fictional_cases_preserve_exact_source_knowledge_and_expected_admission(
        string id
    )
    {
        var c = FareSummaryEvalPacket.Load().Cases.Single(c => c.Id == id);
        var time = new FakeTimeProvider(FareSummaryEvalPacket.Now);
        var request = FareSummaryEvalPacket.Request(c, time.GetUtcNow());
        var chat = new PacketChat(request.Facts)
        {
            ExtraOutput = c.Configuration == "extra-output",
            MissingUsage = c.Configuration == "missing-usage",
        };
        var result = await new FareSummaryExtractor(
            chat,
            FareSummaryEvalPacket.FictionalOptions(c),
            new(time),
            time
        ).ExtractAsync(request, TestContext.Current.CancellationToken);
        result.Status.ShouldBe(c.ExpectedStatus, c.Scenario);
        chat.Calls.ShouldBe(c.Eligible ? 1 : 0, c.Scenario);
        var snapshot = FareSummaryEvalPacket.Snapshot(c);
        var summary = snapshot is null
            ? FareSummaryFormatter.Unavailable(Guid.Empty, time.GetUtcNow(), c.Adults)
            : FareSummaryFormatter.Current(
                snapshot,
                time.GetUtcNow(),
                hasSelectedServices: true,
                plan: result.Sections
            );
        summary.State.ShouldBe(c.ExpectedKnowledge.GetProperty("summaryState").GetString());
        summary
            .Notices.Any(n => n.Contains("полный текст правил", StringComparison.Ordinal))
            .ShouldBeTrue();
        if (snapshot is null)
            return;
        snapshot.Evidence.Validate().IsError.ShouldBeFalse(c.Scenario);
        summary.PassengerCount.ShouldBe(c.Adults);
        summary.SegmentCounts.Length.ShouldBe(c.Legs);
        summary.Passengers.Length.ShouldBe(c.Adults * summary.SegmentCounts.Sum());
        summary
            .ChangeBeforeDeparture!.Permission.ToString()
            .ShouldBe(c.ExpectedKnowledge.GetProperty("changePermission").GetString());
        summary
            .RefundBeforeDeparture!.Permission.ToString()
            .ShouldBe(c.ExpectedKnowledge.GetProperty("refundPermission").GetString());
        (summary.ChangeBeforeDeparture.Penalty?.Amount).ShouldBe(
            FareSummaryEvalPacket.Amount(c.ExpectedKnowledge.GetProperty("changeAmount"))
        );
        summary.ChangeBeforeDeparture.Penalty?.Currency.Value.ShouldBe(
            c.ExpectedKnowledge.GetProperty("changeCurrency").GetString()
        );
        (summary.RefundBeforeDeparture.Penalty?.Amount).ShouldBe(
            FareSummaryEvalPacket.Amount(c.ExpectedKnowledge.GetProperty("refundAmount"))
        );
        summary.RefundBeforeDeparture.Penalty?.Currency.Value.ShouldBe(
            c.ExpectedKnowledge.GetProperty("refundCurrency").GetString()
        );
        foreach (var expected in c.ExpectedKnowledge.GetProperty("legChanges").EnumerateArray())
        {
            var leg = summary.Legs.Single(l => l.Leg == expected.GetProperty("leg").GetInt32());
            leg.ChangeBeforeDeparture.Permission.ToString()
                .ShouldBe(expected.GetProperty("permission").GetString());
            (leg.ChangeBeforeDeparture.Penalty?.Amount).ShouldBe(
                FareSummaryEvalPacket.Amount(expected.GetProperty("amount"))
            );
            leg.ChangeBeforeDeparture.Penalty?.Currency.Value.ShouldBe(
                expected.GetProperty("currency").GetString()
            );
        }
        foreach (
            var expected in c.ExpectedKnowledge.GetProperty("includedBaggage").EnumerateArray()
        )
        {
            var pair = summary.Passengers.Single(p =>
                p.Leg == expected.GetProperty("leg").GetInt32()
                && p.Segment == expected.GetProperty("segment").GetInt32()
                && p.Adult == expected.GetProperty("adult").GetInt32()
            );
            pair.CheckedBaggageQuantity.ShouldBe(NullableInt(expected.GetProperty("checked")));
            pair.CarryOnBaggageQuantity.ShouldBe(NullableInt(expected.GetProperty("carryOn")));
        }
        summary
            .Notices.Any(n => n.Contains("разницы тарифа", StringComparison.Ordinal))
            .ShouldBeTrue();
        summary
            .Notices.Any(n => n.Contains("зачисление денег в банк", StringComparison.Ordinal))
            .ShouldBeTrue();
        summary
            .Notices.Any(n => n.Contains("дополнительных услуг", StringComparison.Ordinal))
            .ShouldBeTrue();
        summary.MissingInformation.Length.ShouldBeGreaterThanOrEqualTo(4);
        if (result.Status == "Ready")
            summary.Presentation.ShouldBe("AiOrganized");
        else
            summary.Presentation.ShouldBe("Deterministic");
    }

    [Fact]
    public async Task All_twelve_declared_mixed_holdout_cases_fit_the_actual_root_catalog_with_prompt_and_schema()
    {
        var cases = FareSummaryEvalPacket
            .Load()
            .Cases.Where(c => c.Partition == "Holdout" && c.Eligible)
            .ToArray();
        cases.Length.ShouldBeGreaterThanOrEqualTo(12);
        foreach (var c in cases)
        {
            var time = new FakeTimeProvider(FareSummaryEvalPacket.Now);
            var request = FareSummaryEvalPacket.Request(
                c,
                time.GetUtcNow(),
                injectCatalogFaults: false
            );
            var estimate = FareSummaryExtractor.EstimateInput(request);
            estimate.WithinBounds.ShouldBeTrue(c.Id);
            estimate.EstimatedInputTokens.ShouldBeLessThanOrEqualTo(2048);
            estimate.PayloadBytes.ShouldBeLessThanOrEqualTo(16 * 1024);
            var chat = new PacketChat(request.Facts);
            (
                await new FareSummaryExtractor(
                    chat,
                    FareSummaryEvalPacket.FictionalOptions(c),
                    new(time),
                    time
                ).ExtractAsync(request, TestContext.Current.CancellationToken)
            ).Status.ShouldBe("Ready", c.Id);
            chat.Calls.ShouldBe(1);
            chat.Messages.ShouldNotContain(m =>
                m.Text.Contains("Fictional fare label", StringComparison.Ordinal)
                || m.Text.Contains("Fictional marketing", StringComparison.Ordinal)
                || m.Text.Contains("FIC", StringComparison.Ordinal)
            );
            chat.Messages.Single(m => m.Role == ChatRole.User)
                .Text.ShouldNotContain(request.CorrelationId.ToString());
        }
    }

    [Theory]
    [MemberData(nameof(AdversarialCases))]
    public async Task Every_adversarial_output_is_rejected_without_losing_known_usage(string id)
    {
        var a = FareSummaryEvalPacket.LoadAdversarial().Cases.Single(c => c.Id == id);
        var c = FareSummaryEvalPacket.Load().Cases.Single(c => c.Id == a.InputCaseId);
        var time = new FakeTimeProvider(FareSummaryEvalPacket.Now);
        var request = FareSummaryEvalPacket.Request(c, time.GetUtcNow());
        var chat = new PacketChat(request.Facts) { OverrideJson = a.Json, NonText = a.NonText };
        var reply = await new FareSummaryExtractor(
            chat,
            FareSummaryEvalPacket.FictionalOptions(c),
            new(time),
            time
        ).ExtractAsync(request, TestContext.Current.CancellationToken);
        reply.Status.ShouldBe("InvalidOutput", a.Scenario);
        reply.Sections.ShouldBeNull();
        reply.InputTokens.ShouldBe(10);
        reply.CostUsd.ShouldBe(0.000015m);
        chat.Calls.ShouldBe(1);
    }

    [Theory]
    [InlineData("InvalidOutput")]
    [InlineData("DeadlineExceeded")]
    [InlineData("Disabled")]
    public void Eligible_fallback_timeout_or_skipped_attempt_is_in_the_failure_denominator(
        string status
    )
    {
        var manifest = FareSummaryEvalPacket.Load();
        var outcomes = SuccessfulFakeOutcomes(manifest);
        var first = outcomes.FindIndex(o => manifest.Cases.Single(c => c.Id == o.CaseId).Eligible);
        outcomes[first] = outcomes[first] with
        {
            Status = status,
            Dispatches = status == "Disabled" ? 0 : 1,
        };
        var score = FareSummaryEvalPacket.Score(manifest, outcomes, Votes(manifest));
        score.EligibleDenominator.ShouldBe(12);
        score.ValidAiNumerator.ShouldBe(11);
        score.CanEnable.ShouldBeFalse();
        score.HumanGate.ShouldBeFalse();
    }

    [Fact]
    public void Missing_outcome_is_not_removed_and_ineligible_cases_never_become_model_passes()
    {
        var manifest = FareSummaryEvalPacket.Load();
        var outcomes = SuccessfulFakeOutcomes(manifest);
        outcomes.RemoveAt(
            outcomes.FindIndex(o => manifest.Cases.Single(c => c.Id == o.CaseId).Eligible)
        );
        var score = FareSummaryEvalPacket.Score(manifest, outcomes, Votes(manifest));
        score.EligibleDenominator.ShouldBe(12);
        score.ValidAiNumerator.ShouldBe(11);
        score.P95Seconds.ShouldBeNull();
        score.CanEnable.ShouldBeFalse();
        outcomes = SuccessfulFakeOutcomes(manifest);
        var first = outcomes.FindIndex(o => !manifest.Cases.Single(c => c.Id == o.CaseId).Eligible);
        outcomes[first] = outcomes[first] with { Status = "Ready", Dispatches = 1 };
        score = FareSummaryEvalPacket.Score(manifest, outcomes, Votes(manifest));
        score.ValidAiNumerator.ShouldBe(12);
        score.IneligibleZeroDispatch.ShouldBeFalse();
        score.CanEnable.ShouldBeFalse();
    }

    [Fact]
    public void Fake_score_requires_known_cost_correctness_latency_and_two_blind_readers()
    {
        var manifest = FareSummaryEvalPacket.Load();
        var outcomes = SuccessfulFakeOutcomes(manifest);
        FareSummaryEvalPacket.Score(manifest, outcomes, Votes(manifest)).CanEnable.ShouldBeTrue();
        FareSummaryEvalPacket.Score(manifest, outcomes, []).CanEnable.ShouldBeFalse();
        var index = outcomes.FindIndex(o => manifest.Cases.Single(c => c.Id == o.CaseId).Eligible);
        outcomes[index] = outcomes[index] with
        {
            CostUsd = null,
            DisplayedUnsupportedClaims = 1,
            OptionalLatencySeconds = 5,
        };
        var score = FareSummaryEvalPacket.Score(manifest, outcomes, Votes(manifest));
        score.ActualCostGate.ShouldBeFalse();
        score.CorrectnessGate.ShouldBeFalse();
        score.CanEnable.ShouldBeFalse();
    }

    [Fact]
    public void Extractor_only_timing_cannot_enable_the_candidate()
    {
        var manifest = FareSummaryEvalPacket.Load();
        var outcomes = SuccessfulFakeOutcomes(manifest)
            .Select(o => o with { WholeOptionalStageMeasured = false })
            .ToArray();
        var score = FareSummaryEvalPacket.Score(manifest, outcomes, Votes(manifest));
        score.ValidAiNumerator.ShouldBe(12);
        score.WholeOptionalStageGate.ShouldBeFalse();
        score.P95Seconds.ShouldBeNull();
        score.CanEnable.ShouldBeFalse();
    }

    private static List<FareSummaryEvalOutcome> SuccessfulFakeOutcomes(
        FareSummaryEvalManifest manifest
    ) =>
        manifest
            .Cases.Where(c => c.Partition == "Holdout")
            .Select(c => new FareSummaryEvalOutcome(
                c.Id,
                c.Eligible ? "Ready" : c.ExpectedStatus,
                c.Eligible ? 1 : 0,
                c.Eligible ? 0.01m : null,
                1,
                true,
                true,
                0,
                WholeOptionalStageMeasured: true
            ))
            .ToList();

    private static FareSummaryBlindVote[] Votes(FareSummaryEvalManifest manifest) =>
        new[] { "fictional-reader-a", "fictional-reader-b" }
            .SelectMany(reader =>
                manifest
                    .Cases.Where(c => c.Partition == "Holdout" && c.Eligible)
                    .Select(
                        (c, index) =>
                            new FareSummaryBlindVote(
                                reader,
                                c.Id,
                                index < 8 ? "Win" : "Tie",
                                true,
                                true,
                                true
                            )
                    )
            )
            .ToArray();

    private static int? NullableInt(JsonElement e) =>
        e.ValueKind == JsonValueKind.Null ? null : e.GetInt32();

    private sealed class PacketChat(FareSummaryFact[] facts) : IChatClient
    {
        public int Calls;
        public bool ExtraOutput;
        public bool MissingUsage;
        public bool NonText;
        public string? OverrideJson;
        public List<ChatMessage> Messages = [];

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default
        )
        {
            Calls++;
            Messages.AddRange(messages);
            var sections = facts
                .GroupBy(f => f.Section)
                .Select(g => new FareSummaryPlanSection(
                    g.Key,
                    g.Select(f => new FareSummaryPlanGroup([f.Id])).ToArray()
                ))
                .ToArray();
            var json =
                OverrideJson
                ?? (
                    ExtraOutput
                        ? "{\"sections\":[],\"text\":\"unsupported guarantee\"}"
                        : JsonSerializer.Serialize(
                            new { sections },
                            new JsonSerializerOptions(JsonSerializerDefaults.Web)
                        )
                );
            var message = new ChatMessage(ChatRole.Assistant, json);
            if (NonText)
                message.Contents.Add(
                    new FunctionCallContent(
                        "fictional-call",
                        "never_execute",
                        new Dictionary<string, object?>()
                    )
                );
            return Task.FromResult(
                new ChatResponse(message)
                {
                    ModelId = "fictional-model",
                    Usage = MissingUsage
                        ? null
                        : new UsageDetails { InputTokenCount = 10, OutputTokenCount = 5 },
                }
            );
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }
}
