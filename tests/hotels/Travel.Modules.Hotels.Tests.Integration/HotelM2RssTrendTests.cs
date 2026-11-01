using System.Text.Json;
using Shouldly;
using Travel.Hotels.Fixture;

namespace Travel.Modules.Hotels.Tests.Integration;

public sealed class HotelM2RssTrendTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static (Dictionary<string, bool> Gates, HotelM2MemoryPoint[] Points) Recorded()
    {
        using var file = JsonDocument.Parse(
            File.ReadAllText(
                Path.Combine(AppContext.BaseDirectory, "Fixtures", "m2-v6-rss-trend.json")
            )
        );
        return (
            file.RootElement.GetProperty("gates").Deserialize<Dictionary<string, bool>>(Json)!,
            file.RootElement.GetProperty("points").Deserialize<HotelM2MemoryPoint[]>(Json)!
        );
    }

    private static (Dictionary<string, bool> Gates, HotelM2MemoryPoint[] Points) Synthetic(
        long growth
    )
    {
        var (gates, _) = Recorded();
        var points = Enumerable
            .Range(0, 361)
            .Select(i => new HotelM2MemoryPoint(
                i,
                100 * 1024 * 1024 + (i == 360 ? growth : 0),
                38,
                41,
                128,
                1000
            ))
            .ToArray();
        gates["workingSetRecovery"] = growth <= 0;
        gates["workingSetCeiling"] = true;
        return (gates, points);
    }

    [Fact]
    public void Real_v6_false_trend_remains_false_diagnostic_and_budget_is_mandatory()
    {
        var (gates, points) = Recorded();
        var old = HotelsM2MemoryAssessment.Create(4, gates, points);
        old.Gates["workingSetRecovery"].ShouldBeFalse();
        old.RssTrendDiagnostic.ShouldBeNull();
        var current = HotelsM2MemoryAssessment.Create(5, gates, points);
        current.Gates.ContainsKey("workingSetRecovery").ShouldBeFalse();
        current.Gates[HotelsM2MemoryAssessment.BudgetGate].ShouldBeTrue();
        current.Gates.Values.ShouldAllBe(v => v);
        var d = current.RssTrendDiagnostic!;
        d.LegacyGatePassed.ShouldBeFalse();
        d.LegacyGateBlocking.ShouldBeFalse();
        d.NonGrowth.ShouldBeFalse();
        d.TrendBlocking.ShouldBeFalse();
        d.FirstCount.ShouldBe(30);
        d.SecondCount.ShouldBe(31);
        d.FirstSumRss.ShouldBe(14941835264);
        d.SecondSumRss.ShouldBe(15439917056);
        (d.SecondSumRss * 30 - d.FirstSumRss * 31).ShouldBe(618496);
        d.GrowthBytes.ShouldBe(52219904);
        d.AllowedGrowthBytes.ShouldBe(134217728);
        d.First.ShouldAllBe(p => p.At >= 300 && p.At < 330);
        d.Second.Last().At.ShouldBeGreaterThanOrEqualTo(360);
        gates["workingSetRecovery"].ShouldBeFalse();
    }

    [Theory]
    [InlineData(134217728L, true)]
    [InlineData(134217729L, false)]
    public void Budget_boundary_has_no_epsilon(long growth, bool expected)
    {
        var (g, p) = Synthetic(growth);
        var r = HotelsM2MemoryAssessment.Create(5, g, p);
        r.Gates[HotelsM2MemoryAssessment.BudgetGate].ShouldBe(expected);
        r.RssTrendDiagnostic!.BudgetPassed.ShouldBe(expected);
        r.RssTrendDiagnostic.NonGrowth.ShouldBeFalse();
    }

    [Fact]
    public void Absolute_one_GiB_ceiling_cannot_be_waived()
    {
        var (g, p) = Synthetic(0);
        p[100] = p[100] with { Rss = 1073741825 };
        g["workingSetCeiling"] = false;
        var r = HotelsM2MemoryAssessment.Create(5, g, p);
        r.Gates["workingSetCeiling"].ShouldBeFalse();
        r.Gates.Values.All(v => v).ShouldBeFalse();
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("nan")]
    [InlineData("early")]
    [InlineData("late")]
    [InlineData("negative")]
    public void Incomplete_or_invalid_samples_cannot_make_a_budget_pass(string mutation)
    {
        var (g, p) = Synthetic(0);
        if (mutation == "missing")
            p = p.Where((_, i) => i != 17).ToArray();
        if (mutation == "duplicate")
            p[18] = p[17];
        if (mutation == "nan")
            p[100] = p[100] with { At = double.NaN };
        if (mutation == "early")
            p[100] = p[100] with { At = 99.9 };
        if (mutation == "late")
            p[360] = p[360] with { At = 361.01 };
        if (mutation == "negative")
            p[100] = p[100] with { Rss = -1 };
        Should.Throw<InvalidDataException>(() => HotelsM2MemoryAssessment.Create(5, g, p));
    }

    [Theory]
    [InlineData("missing-recovery")]
    [InlineData("missing-ceiling")]
    [InlineData("wrong-recovery")]
    [InlineData("wrong-ceiling")]
    public void Missing_or_inconsistent_legacy_results_are_rejected(string mutation)
    {
        var (g, p) = Synthetic(0);
        if (mutation == "missing-recovery")
            g.Remove("workingSetRecovery");
        if (mutation == "missing-ceiling")
            g.Remove("workingSetCeiling");
        if (mutation == "wrong-recovery")
            g["workingSetRecovery"] = false;
        if (mutation == "wrong-ceiling")
            g["workingSetCeiling"] = false;
        Should.Throw<InvalidDataException>(() => HotelsM2MemoryAssessment.Create(5, g, p));
    }

    [Theory]
    [InlineData("cacheBounds")]
    [InlineData("m2SourceAccounting")]
    [InlineData("normalAndSteadyPurchase")]
    public void Unrelated_failure_remains_blocking(string gate)
    {
        var (g, p) = Synthetic(0);
        g[gate] = false;
        HotelsM2MemoryAssessment.Create(5, g, p).Gates[gate].ShouldBeFalse();
    }

    [Fact]
    public void Serialized_diagnostic_retains_exact_false_and_window_data()
    {
        var (g, p) = Recorded();
        var a = HotelsM2MemoryAssessment.Create(5, g, p);
        var json = JsonSerializer.SerializeToElement(
            new { gates = a.Gates, rssTrendDiagnostic = a.RssTrendDiagnostic },
            Json
        );
        json.GetProperty("rssTrendDiagnostic")
            .GetProperty("nonGrowth")
            .GetBoolean()
            .ShouldBeFalse();
        json.GetProperty("rssTrendDiagnostic")
            .GetProperty("legacyGatePassed")
            .GetBoolean()
            .ShouldBeFalse();
        json.GetProperty("rssTrendDiagnostic").GetProperty("second").GetArrayLength().ShouldBe(31);
        json.GetProperty("gates").GetProperty("m2PostWarmMemoryBudget").GetBoolean().ShouldBeTrue();
    }
}
