using System.Runtime.CompilerServices;
using System.Text.Json;
using Shouldly;
using Travel.AI.TripsBriefExtraction;
using Travel.Modules.Trips.Application.BriefExtraction;
using Travel.Tests.AiEvals.TripsBriefExtraction;
using Xunit;

namespace Travel.Tests.Contract.Trips;

[Trait("Category", "Contract")]
public sealed class TripsBriefEvaluationPacketTests
{
    private static string Fixture =>
        Path.Combine(AppContext.BaseDirectory, "TripsBriefExtraction", "qualification-cases.json");

    [Fact]
    public void Frozen_corpus_is_exactly48_unique_fictional_cases_and12_useful_cases()
    {
        var packet = TripsBriefEvaluationPacket.Load(Fixture);
        packet.Cases.Length.ShouldBe(48);
        packet.Cases.Select(x => x.Id).Distinct().Count().ShouldBe(48);
        packet.Cases.Count(x => x.Useful).ShouldBe(12);
        packet.Cases.Count(x => x.Hazardous).ShouldBe(10);
        packet.SourceCorpusRowsSha256.ShouldBe(
            "ed0ef5cb4a3a407b4b4d988bf4be466ae79ec549b129f7e2f0fa2f75de446052"
        );
    }

    [Fact]
    public void Machine_transcription_matches_every_original_text_and_oracle()
    {
        var source = Path.Combine(
            SourceRoot(),
            "openspec/changes/trips-m3-brief-confirmation/evaluation-plan.md"
        );
        var lines = File.ReadAllLines(source)
            .Where(x => System.Text.RegularExpressions.Regex.IsMatch(x, @"^\| [DH]\d{2} \|"))
            .ToArray();
        var packet = TripsBriefEvaluationPacket.Load(Fixture);
        lines.Length.ShouldBe(packet.Cases.Length);
        foreach (var c in packet.Cases)
        {
            var row = lines.Single(x => x.StartsWith("| " + c.Id + " |", StringComparison.Ordinal));
            row.ShouldBe("| " + c.Id + " | " + c.Text + " | " + c.ExpectedSummary + " |");
        }
    }

    [Fact]
    public void Mechanical_grammar_is_compared_with_all_predeclared_oracles_without_model()
    {
        var packet = TripsBriefEvaluationPacket.Load(Fixture);
        var failures = new List<string>();
        foreach (var c in packet.Cases)
        {
            var candidate = TripBriefCandidateValidator.Analyze(
                TripsBriefEvaluationPacket.Input(c, DateTimeOffset.UtcNow)
            );
            if (!TripsBriefEvaluationPacket.Matches(c, candidate))
                failures.Add(
                    c.Id
                        + ":"
                        + JsonSerializer.Serialize(candidate.Fields)
                        + " issues="
                        + string.Join(",", candidate.Issues.Select(x => x.Code))
                );
        }
        failures.ShouldBeEmpty("fictional mechanical qualification only; not model quality");
    }

    [Fact]
    public void Every_frozen_case_fits_the_actual_complete_prompt_before_paid_execution()
    {
        foreach (var c in TripsBriefEvaluationPacket.Load(Fixture).Cases)
        {
            var request = TripsBriefEvaluationPacket.Request(c, DateTimeOffset.UtcNow);
            TripsBriefExtractor
                .EstimateInput(request, "fictional-model")
                .WithinBounds.ShouldBeTrue(c.Id);
        }
    }

    [Fact]
    public void Corpus_loader_rejects_duplicate_ids_and_wrong_denominators()
    {
        var packet = TripsBriefEvaluationPacket.Load(Fixture);
        Should.Throw<InvalidOperationException>(() =>
            TripsBriefEvaluationPacket.Validate(
                packet with
                {
                    Cases = [.. packet.Cases.Skip(1), packet.Cases[1]],
                }
            )
        );
        Should.Throw<InvalidOperationException>(() =>
            TripsBriefEvaluationPacket.Validate(packet with { Cases = packet.Cases[..47] })
        );
    }

    private static string SourceRoot([CallerFilePath] string source = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "../../.."));
}
