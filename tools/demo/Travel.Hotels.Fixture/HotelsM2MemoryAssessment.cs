namespace Travel.Hotels.Fixture;

internal sealed record HotelM2MemoryPoint(
    double At,
    long Rss,
    int Reservations,
    int RetainedInboxRows,
    int CacheEntries,
    long CacheBytes
);

internal sealed record HotelM2FinalMemoryDiagnostic(
    string Name,
    bool Blocking,
    bool LegacyGatePassed,
    int FirstCount,
    int SecondCount,
    double FirstMeanRss,
    double SecondMeanRss,
    double DeltaMeanRss,
    bool SameRowCounts,
    bool SameCachePayload,
    string FullRetainedStateComparison,
    HotelM2MemoryPoint[] First,
    HotelM2MemoryPoint[] Second
);

internal sealed record HotelM2RssTrendDiagnostic(
    bool LegacyGatePassed,
    bool LegacyGateBlocking,
    bool NonGrowth,
    bool TrendBlocking,
    double BaselineAt,
    long BaselineRss,
    double FinalAt,
    long FinalRss,
    long GrowthBytes,
    long AllowedGrowthBytes,
    bool BudgetPassed,
    int FirstCount,
    int SecondCount,
    long FirstSumRss,
    long SecondSumRss,
    double FirstMeanRss,
    double SecondMeanRss,
    double DeltaMeanRss,
    string FullRetainedStateComparison,
    HotelM2MemoryPoint[] First,
    HotelM2MemoryPoint[] Second
);

internal sealed record HotelM2MemoryAssessment(
    int AcceptanceVersion,
    Dictionary<string, bool> Gates,
    HotelM2FinalMemoryDiagnostic Diagnostic
)
{
    public HotelM2QueueDiagnostic? QueueDiagnostic { get; init; }
    public HotelM2RecoveryDiagnostic? RecoveryDiagnostic { get; init; }
    public HotelM2RssTrendDiagnostic? RssTrendDiagnostic { get; init; }
}

internal static class HotelsM2MemoryAssessment
{
    internal const string BudgetGate = "m2PostWarmMemoryBudget";
    internal const long GrowthBudgetBytes = 134217728;
    internal const string DiagnosticName = "finalSameRetainedRowsMemoryStable";

    internal static bool SupportsVersion(int version) => version is 1 or 2 or 3 or 4 or 5;

    internal static void ValidateVersions(int small, int large)
    {
        if (!SupportsVersion(small) || small != large)
            throw new InvalidDataException("One supported S/L acceptance version is required.");
    }

    internal static HotelM2MemoryAssessment Create(
        int version,
        IReadOnlyDictionary<string, bool> legacyGates,
        IEnumerable<HotelM2MemoryPoint> points
    )
    {
        ValidateVersions(version, version);
        if (!legacyGates.TryGetValue(DiagnosticName, out var legacyPassed))
            throw new InvalidDataException("Original final memory result is missing.");
        var telemetry = points.OrderBy(p => p.At).ToArray();
        var windows = telemetry.Where(p => p.At >= 350 && p.At < 360).ToArray();
        var first = windows.Where(p => p.At < 355).ToArray();
        var second = windows.Where(p => p.At >= 355).ToArray();
        if (
            first.Length < 5
            || second.Length < 5
            || windows.Any(p => !double.IsFinite(p.At) || p.Rss < 0)
            || windows.Select(p => p.At).Distinct().Count() != windows.Length
        )
            throw new InvalidDataException("Final memory diagnostic evidence is incomplete.");

        var sameRows =
            windows.Select(p => (p.Reservations, p.RetainedInboxRows)).Distinct().Count() == 1;
        var sameCache = windows.Select(p => (p.CacheEntries, p.CacheBytes)).Distinct().Count() == 1;
        var firstMean = first.Average(p => (double)p.Rss);
        var secondMean = second.Average(p => (double)p.Rss);
        var gates = new Dictionary<string, bool>(legacyGates, StringComparer.Ordinal);
        if (version >= 2)
            gates.Remove(DiagnosticName);
        var assessment = new HotelM2MemoryAssessment(
            version,
            gates,
            new(
                DiagnosticName,
                version == 1,
                legacyPassed,
                first.Length,
                second.Length,
                firstMean,
                secondMean,
                secondMean - firstMean,
                sameRows,
                sameCache,
                sameRows && sameCache ? "notEstablished" : "changed",
                first,
                second
            )
        );
        if (version < 5)
            return assessment;
        if (
            !legacyGates.TryGetValue("workingSetRecovery", out var legacyRecovery)
            || !legacyGates.TryGetValue("workingSetCeiling", out var ceiling)
            || telemetry.Length != 361
            || telemetry.Select(p => p.At).Distinct().Count() != 361
            || telemetry
                .Where((p, i) => !double.IsFinite(p.At) || p.At < i || p.At > i + 1 || p.Rss < 0)
                .Any()
        )
            throw new InvalidDataException("Complete RSS budget/trend evidence is required.");
        var baseline = telemetry.Last(p => p.At < 30);
        var final = telemetry[^1];
        var early = telemetry.Where(p => p.At >= 300 && p.At < 330).ToArray();
        var late = telemetry.Where(p => p.At >= 330).ToArray();
        var firstSum = early.Sum(p => p.Rss);
        var secondSum = late.Sum(p => p.Rss);
        var earlyMean = (double)firstSum / early.Length;
        var lateMean = (double)secondSum / late.Length;
        var nonGrowth = lateMean <= earlyMean;
        var growth = final.Rss - baseline.Rss;
        var budgetPassed = growth <= GrowthBudgetBytes;
        if (
            legacyRecovery != (budgetPassed && nonGrowth)
            || ceiling != telemetry.All(p => p.Rss <= 1073741824)
        )
            throw new InvalidDataException("Legacy memory result disagrees with raw samples.");
        gates.Remove("workingSetRecovery");
        gates[BudgetGate] = budgetPassed;
        var observedStateUnchanged =
            early
                .Concat(late)
                .Select(p => (p.Reservations, p.RetainedInboxRows, p.CacheEntries, p.CacheBytes))
                .Distinct()
                .Count() == 1;
        return assessment with
        {
            RssTrendDiagnostic = new(
                legacyRecovery,
                false,
                nonGrowth,
                false,
                baseline.At,
                baseline.Rss,
                final.At,
                final.Rss,
                growth,
                GrowthBudgetBytes,
                budgetPassed,
                early.Length,
                late.Length,
                firstSum,
                secondSum,
                earlyMean,
                lateMean,
                lateMean - earlyMean,
                observedStateUnchanged ? "notEstablished" : "changed",
                early,
                late
            ),
        };
    }
}
