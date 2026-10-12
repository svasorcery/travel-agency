using System.Globalization;
using System.Text.Json;
using Travel.AI.FareSummary;
using Travel.IntegrationContracts.AI.FareSummary;
using Travel.Modules.Flights.Application.FareRules;
using Travel.Modules.Flights.Core.ValueObjects;

namespace Travel.Tests.AiEvals.Flights.FareSummary;

public sealed record FareSummaryEvalCase(
    string Id,
    string Partition,
    string PrimaryCategory,
    string Scenario,
    string[] Tags,
    int Adults,
    int Legs,
    bool Eligible,
    string ExpectedStatus,
    string Configuration,
    int DeadlineOffsetMs,
    string AiInputMode,
    FareSummaryFact[] Facts,
    JsonElement Snapshot,
    JsonElement NormalizedEvidence,
    JsonElement ExpectedKnowledge
);

public sealed record FareSummaryEvalManifest(
    int SchemaVersion,
    string PacketVersion,
    string PromptVersion,
    int RenderVersion,
    string Model,
    string Pricing,
    bool PaidRunExecuted,
    FareSummaryEvalCase[] Cases
);

public sealed record FareSummaryAdversarialCase(
    string Id,
    string Scenario,
    string InputCaseId,
    string Json,
    bool NonText
);

public sealed record FareSummaryAdversarialPacket(
    int SchemaVersion,
    string PacketVersion,
    string ExpectedStatus,
    FareSummaryAdversarialCase[] Cases
);

public sealed record FareSummaryEvalOutcome(
    string CaseId,
    string Status,
    int Dispatches,
    decimal? CostUsd,
    double? OptionalLatencySeconds,
    bool MoneyScopeUnknownCorrect,
    bool MandatoryFactsComplete,
    int DisplayedUnsupportedClaims,
    bool WholeOptionalStageMeasured = false
);

public sealed record FareSummaryBlindVote(
    string ReaderId,
    string CaseId,
    string Verdict,
    bool RussianReader,
    bool Blinded,
    bool AllMandatoryFactsPreserved
);

public sealed record FareSummaryEvalScore(
    int EligibleDenominator,
    int ValidAiNumerator,
    double ValidAiRate,
    double? P95Seconds,
    bool ActualCostGate,
    bool CorrectnessGate,
    bool IneligibleZeroDispatch,
    bool WholeOptionalStageGate,
    bool HumanGate,
    bool CanEnable
);

public static class FareSummaryEvalPacket
{
    public static readonly DateTimeOffset Now = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static FareSummaryEvalManifest Load(string? path = null) =>
        JsonSerializer.Deserialize<FareSummaryEvalManifest>(
            File.ReadAllText(
                path
                    ?? Path.Combine(
                        AppContext.BaseDirectory,
                        "Flights",
                        "FareSummary",
                        "manifest.json"
                    )
            ),
            Json
        ) ?? throw new InvalidDataException("Fare summary packet unavailable.");

    public static FareSummaryAdversarialPacket LoadAdversarial() =>
        JsonSerializer.Deserialize<FareSummaryAdversarialPacket>(
            File.ReadAllText(
                Path.Combine(AppContext.BaseDirectory, "Flights", "FareSummary", "adversarial.json")
            ),
            Json
        ) ?? throw new InvalidDataException("Fare summary adversarial packet unavailable.");

    public static QuotedFareSnapshot? Snapshot(FareSummaryEvalCase c) =>
        c.Snapshot.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
            ? null
            : c.Snapshot.Deserialize<QuotedFareSnapshot>(Json);

    public static FareSummaryFact[] CanonicalFacts(FareSummaryEvalCase c) =>
        Snapshot(c) is { } snapshot ? FareSummaryFormatter.BuildFacts(snapshot) : [];

    public static FareSummaryRequested Request(
        FareSummaryEvalCase c,
        DateTimeOffset now,
        bool injectCatalogFaults = true
    ) =>
        new(
            Guid.NewGuid(),
            Snapshot(c)?.Fingerprint ?? new string('a', 64),
            FareSummaryMessageIdentity.PromptVersion,
            "ru",
            now.AddMilliseconds(c.DeadlineOffsetMs),
            injectCatalogFaults && c.AiInputMode == "FaultInjectedCatalog"
                ? c.Facts
                : CanonicalFacts(c)
        );

    public static FareSummaryOptions FictionalOptions(FareSummaryEvalCase c) =>
        new()
        {
            Enabled = true,
            Model = "fictional-model",
            PricingModel = "fictional-model",
            PricingVerified = c.Configuration != "unverified",
            InputUsdPerMillion = 1,
            OutputUsdPerMillion = c.Configuration == "expensive" ? 1000 : 1,
        };

    public static decimal? Amount(JsonElement value) =>
        value.ValueKind == JsonValueKind.Null
            ? null
            : decimal.Parse(
                value.GetString()!,
                NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture
            );

    public static void ValidateManifest(FareSummaryEvalManifest manifest)
    {
        var tuning = manifest.Cases.Where(c => c.Partition == "Tuning").ToArray();
        var holdout = manifest.Cases.Where(c => c.Partition == "Holdout").ToArray();
        if (
            manifest.SchemaVersion != 1
            || manifest.PacketVersion != "fare-summary-fictional-v1"
            || manifest.PromptVersion != FareSummaryMessageIdentity.PromptVersion
            || manifest.Cases.Length != 48
            || manifest.Cases.Select(c => c.Id).Distinct(StringComparer.Ordinal).Count() != 48
            || tuning.Length != 32
            || holdout.Length != 16
            || manifest.Cases.GroupBy(c => c.PrimaryCategory).Count() != 4
            || manifest.Cases.GroupBy(c => c.PrimaryCategory).Any(g => g.Count() != 12)
            || holdout.Count(c => c.Eligible && c.Tags.Contains("Mixed")) < 12
            || holdout.Count(c => !c.Eligible) < 4
            || !tuning.Any(c => c.Adults == 9 && c.Legs == 4)
            || !holdout.Any(c => c.Adults == 9 && c.Legs == 4)
        )
            throw new InvalidDataException(
                "Fare summary manifest does not satisfy the frozen protocol."
            );
    }

    public static FareSummaryEvalScore Score(
        FareSummaryEvalManifest manifest,
        IReadOnlyList<FareSummaryEvalOutcome> outcomes,
        IReadOnlyList<FareSummaryBlindVote> votes
    )
    {
        ValidateManifest(manifest);
        if (
            outcomes.Select(o => o.CaseId).Distinct(StringComparer.Ordinal).Count()
                != outcomes.Count
            || outcomes.Any(o => !manifest.Cases.Any(c => c.Id == o.CaseId))
        )
            throw new InvalidDataException("Evaluation outcomes do not match the frozen cases.");
        var holdout = manifest.Cases.Where(c => c.Partition == "Holdout").ToArray();
        var eligible = holdout.Where(c => c.Eligible).ToArray();
        var selected = outcomes
            .Where(o => holdout.Any(c => c.Id == o.CaseId))
            .ToDictionary(o => o.CaseId, StringComparer.Ordinal);
        var successes = eligible.Count(c =>
            selected.TryGetValue(c.Id, out var o)
            && o.Status == "Ready"
            && o.Dispatches == 1
            && o.CostUsd is >= 0 and <= FareSummaryExtractor.MaxCostUsd
            && o.MoneyScopeUnknownCorrect
            && o.MandatoryFactsComplete
            && o.DisplayedUnsupportedClaims == 0
        );
        var measured = eligible
            .Where(c =>
                selected.TryGetValue(c.Id, out var o)
                && o.OptionalLatencySeconds is >= 0
                && o.WholeOptionalStageMeasured
            )
            .Select(c => selected[c.Id].OptionalLatencySeconds!.Value)
            .Order()
            .ToArray();
        double? p95 =
            measured.Length == eligible.Length
                ? measured[(int)Math.Ceiling(0.95 * measured.Length) - 1]
                : null;
        var cost = eligible.All(c =>
            selected.TryGetValue(c.Id, out var o)
            && (o.Dispatches == 0 || o.CostUsd is >= 0 and <= FareSummaryExtractor.MaxCostUsd)
        );
        var correctness = selected.Values.All(o =>
            o.MoneyScopeUnknownCorrect
            && o.MandatoryFactsComplete
            && o.DisplayedUnsupportedClaims == 0
        );
        var zero = holdout
            .Where(c => !c.Eligible)
            .All(c =>
                selected.TryGetValue(c.Id, out var o) && o.Dispatches == 0 && o.Status != "Ready"
            );
        var wholeStage = eligible.All(c =>
            selected.TryGetValue(c.Id, out var o) && o.WholeOptionalStageMeasured
        );
        var readers = votes.Select(v => v.ReaderId).Distinct(StringComparer.Ordinal).ToArray();
        var human =
            readers.Length == 2
            && readers.All(reader =>
            {
                var comparisons = votes.Where(v => v.ReaderId == reader).ToArray();
                return comparisons.Length == eligible.Length
                    && comparisons.Select(v => v.CaseId).Distinct(StringComparer.Ordinal).Count()
                        == eligible.Length
                    && comparisons.All(v =>
                        eligible.Any(c => c.Id == v.CaseId)
                        && v.RussianReader
                        && v.Blinded
                        && v.AllMandatoryFactsPreserved
                        && v.Verdict is "Win" or "Loss" or "Tie"
                        && selected.TryGetValue(v.CaseId, out var o)
                        && o.Status == "Ready"
                    )
                    && comparisons.Count(v => v.Verdict == "Win") >= 8
                    && comparisons.Count(v => v.Verdict == "Loss") <= 1;
            });
        var rate = (double)successes / eligible.Length;
        return new(
            eligible.Length,
            successes,
            rate,
            p95,
            cost,
            correctness,
            zero,
            wholeStage,
            human,
            wholeStage && rate >= 0.95 && p95 is <= 4 && cost && correctness && zero && human
        );
    }
}
