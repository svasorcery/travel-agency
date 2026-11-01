using System.Text.Json;
using Shouldly;
using Travel.Hotels.Fixture;

namespace Travel.Modules.Hotels.Tests.Integration;

public sealed class HotelM2MemoryAssessmentTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static (Dictionary<string, bool> Gates, HotelM2MemoryPoint[] Points) Recorded()
    {
        using var json = JsonDocument.Parse(
            File.ReadAllText(
                Path.Combine(AppContext.BaseDirectory, "Fixtures", "m2-v3-final-memory.json")
            )
        );
        return (
            json.RootElement.GetProperty("gates").Deserialize<Dictionary<string, bool>>(Json)!,
            json.RootElement.GetProperty("points").Deserialize<HotelM2MemoryPoint[]>(Json)!
        );
    }

    [Fact]
    public void Recorded_4KiB_growth_is_false_diagnostic_in_v2_without_rewriting_input()
    {
        var (gates, points) = Recorded();
        var result = HotelsM2MemoryAssessment.Create(2, gates, points);
        result.AcceptanceVersion.ShouldBe(2);
        result.Gates.Count.ShouldBe(23);
        result.Gates.ContainsKey(HotelsM2MemoryAssessment.DiagnosticName).ShouldBeFalse();
        result.Gates.Values.ShouldAllBe(v => v);
        result.Diagnostic.Blocking.ShouldBeFalse();
        result.Diagnostic.LegacyGatePassed.ShouldBeFalse();
        result.Diagnostic.DeltaMeanRss.ShouldBe(4096);
        result.Diagnostic.FirstMeanRss.ShouldBe(468946944);
        result.Diagnostic.SecondMeanRss.ShouldBe(468951040);
        result.Diagnostic.FirstCount.ShouldBe(5);
        result.Diagnostic.SecondCount.ShouldBe(5);
        result.Diagnostic.SameRowCounts.ShouldBeTrue();
        result.Diagnostic.SameCachePayload.ShouldBeFalse();
        result.Diagnostic.FullRetainedStateComparison.ShouldBe("changed");
        gates.Count.ShouldBe(24);
        gates[HotelsM2MemoryAssessment.DiagnosticName].ShouldBeFalse();
        foreach (var pair in result.Gates)
            pair.Value.ShouldBe(gates[pair.Key]);
    }

    [Fact]
    public void Version1_retains_the_recorded_failure_and_all24_gates()
    {
        var (gates, points) = Recorded();
        var result = HotelsM2MemoryAssessment.Create(1, gates, points);
        result.Gates.Count.ShouldBe(24);
        result.Gates[HotelsM2MemoryAssessment.DiagnosticName].ShouldBeFalse();
        result.Diagnostic.Blocking.ShouldBeTrue();
        result.Diagnostic.LegacyGatePassed.ShouldBeFalse();
    }

    [Theory]
    [InlineData("workingSetCeiling")]
    [InlineData("workingSetRecovery")]
    [InlineData("cacheBounds")]
    [InlineData("dueWorkAndInboxSettled")]
    [InlineData("m2SourceAccounting")]
    [InlineData("offeredAndCompleted")]
    [InlineData("normalAndSteadyPurchase")]
    public void Every_other_failure_remains_blocking(string key)
    {
        var (gates, points) = Recorded();
        gates[key] = false;
        var result = HotelsM2MemoryAssessment.Create(2, gates, points);
        result.Gates[key].ShouldBeFalse();
        result.Gates.Values.All(v => v).ShouldBeFalse();
    }

    [Fact]
    public void Equal_observed_counts_do_not_claim_full_retained_state_comparability()
    {
        var (gates, points) = Recorded();
        points = points.Select(p => p with { CacheBytes = points[0].CacheBytes }).ToArray();
        var result = HotelsM2MemoryAssessment.Create(2, gates, points);
        result.Diagnostic.SameCachePayload.ShouldBeTrue();
        result.Diagnostic.FullRetainedStateComparison.ShouldBe("notEstablished");
    }

    [Fact]
    public void Missing_diagnostic_samples_cannot_be_demoted_to_success()
    {
        var (gates, points) = Recorded();
        Should.Throw<InvalidDataException>(() =>
            HotelsM2MemoryAssessment.Create(2, gates, points.Take(9))
        );
    }

    [Fact]
    public void Duplicate_timestamps_do_not_replace_missing_samples()
    {
        var (gates, points) = Recorded();
        points[1] = points[0];
        Should.Throw<InvalidDataException>(() => HotelsM2MemoryAssessment.Create(2, gates, points));
    }

    [Fact]
    public void Missing_original_boolean_cannot_be_fabricated()
    {
        var (gates, points) = Recorded();
        gates.Remove(HotelsM2MemoryAssessment.DiagnosticName);
        Should.Throw<InvalidDataException>(() => HotelsM2MemoryAssessment.Create(2, gates, points));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(6, 6)]
    [InlineData(1, 2)]
    [InlineData(2, 1)]
    public void Unsupported_or_mixed_versions_are_rejected(int small, int large) =>
        Should.Throw<InvalidDataException>(() =>
            HotelsM2MemoryAssessment.ValidateVersions(small, large)
        );

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void Matching_supported_versions_are_explicit(int version)
    {
        HotelsM2MemoryAssessment.SupportsVersion(version).ShouldBeTrue();
        HotelsM2MemoryAssessment.ValidateVersions(version, version);
    }

    [Fact]
    public void Json_receipt_preserves_false_nonblocking_result_and_raw_windows()
    {
        var (gates, points) = Recorded();
        var result = HotelsM2MemoryAssessment.Create(2, gates, points);
        var json = JsonSerializer.SerializeToElement(
            new
            {
                acceptanceVersion = result.AcceptanceVersion,
                gates = result.Gates,
                memoryDiagnostic = result.Diagnostic,
            },
            Json
        );
        json.GetProperty("acceptanceVersion").GetInt32().ShouldBe(2);
        json.GetProperty("memoryDiagnostic")
            .GetProperty("legacyGatePassed")
            .GetBoolean()
            .ShouldBeFalse();
        json.GetProperty("memoryDiagnostic").GetProperty("blocking").GetBoolean().ShouldBeFalse();
        json.GetProperty("memoryDiagnostic").GetProperty("first").GetArrayLength().ShouldBe(5);
        json.GetProperty("gates")
            .TryGetProperty(HotelsM2MemoryAssessment.DiagnosticName, out _)
            .ShouldBeFalse();
    }
}
