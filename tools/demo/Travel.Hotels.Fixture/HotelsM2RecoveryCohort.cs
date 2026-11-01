using Travel.Modules.Hotels.Tests.Integration;

namespace Travel.Hotels.Fixture;

internal sealed record HotelM2RecoveryOrigin(int RootJobId, double Due);

internal sealed record HotelM2RecoveryFact(
    int JobId,
    string Kind,
    double Due,
    double Completed,
    bool Useful
);

internal sealed record HotelM2RecoveryKind(
    string Kind,
    int RawOffered,
    int RawUseful,
    int[] EligibleIds,
    int[] CarryInIds,
    int Useful,
    double? SearchP95Milliseconds,
    bool Passed
);

internal sealed record HotelM2RecoveryWindow(int End, HotelM2RecoveryKind[] Kinds, bool Passed);

internal sealed record HotelM2RecoveryDiagnostic(
    bool LegacyGatePassed,
    bool LegacyGateBlocking,
    int? RecoveredAt,
    HotelM2RecoveryWindow[] Windows
);

internal static class HotelsM2RecoveryCohort
{
    internal const string Gate = "m2RecoveryWithin75SecondsByOrigin";
    private static readonly string[] Kinds = ["search", "rates", "prepare", "create"];

    internal static Dictionary<int, HotelM2RecoveryOrigin> Origins(
        IReadOnlyList<HotelM2PlannedJob> jobs
    )
    {
        if (
            jobs.Select(j => j.Id).Distinct().Count() != jobs.Count
            || jobs.Any(j =>
                j.Id < 0
                || !double.IsFinite(j.Due)
                || j.Due < 0
                || j.Due >= 360
                || !Kinds.Concat(["status", "callback"]).Contains(j.Kind)
            )
        )
            throw new InvalidDataException("Invalid recovery plan.");
        var plan = jobs.ToDictionary(j => j.Id);
        var result = new Dictionary<int, HotelM2RecoveryOrigin>();
        foreach (var job in jobs.OrderBy(j => j.Due).ThenBy(j => j.Id))
        {
            var expectedParent = job.Kind switch
            {
                "rates" => "search",
                "prepare" => "rates",
                "create" => "prepare",
                _ => null,
            };
            if (expectedParent is null)
            {
                if (job.Prerequisite is not null)
                    throw new InvalidDataException("Unexpected recovery dependency.");
                result[job.Id] = new(job.Id, job.Due);
                continue;
            }
            if (
                job.Prerequisite is not { } id
                || !plan.TryGetValue(id, out var parent)
                || parent.Kind != expectedParent
                || parent.Due >= job.Due
                || !result.TryGetValue(id, out var origin)
            )
                throw new InvalidDataException("Missing, cyclic or future recovery dependency.");
            result[job.Id] = origin;
        }
        return result;
    }

    internal static HotelM2RecoveryDiagnostic Evaluate(
        IReadOnlyList<HotelM2PlannedJob> jobs,
        IReadOnlyList<HotelM2RecoveryFact> facts,
        Func<int, double, bool> purchaseSucceeded,
        bool legacyPassed
    )
    {
        var origins = Origins(jobs);
        var plan = jobs.ToDictionary(j => j.Id);
        if (facts.Select(f => f.JobId).Distinct().Count() != facts.Count)
            throw new InvalidDataException("Duplicate recovery facts.");
        var observed = facts.ToDictionary(f => f.JobId);
        foreach (var f in facts)
            if (
                !plan.TryGetValue(f.JobId, out var j)
                || f.Kind != j.Kind
                || f.Due != j.Due
                || !double.IsFinite(f.Completed)
                || f.Completed < f.Due
            )
                throw new InvalidDataException("Recovery fact differs from the frozen plan.");
        foreach (var j in jobs.Where(j => Kinds.Contains(j.Kind) && j.Due >= 240 && j.Due < 315))
            if (!observed.ContainsKey(j.Id))
                throw new InvalidDataException("Offered recovery fact missing.");
        var windows = new List<HotelM2RecoveryWindow>();
        int? recovered = null;
        foreach (var end in Enumerable.Range(300, 16))
        {
            var groups = Kinds
                .Select(kind =>
                {
                    var raw = jobs.Where(j => j.Kind == kind && j.Due >= end - 60 && j.Due < end)
                        .ToArray();
                    var eligible = raw.Where(j => origins[j.Id].Due >= end - 60).ToArray();
                    var carry = raw.Where(j => origins[j.Id].Due < end - 60)
                        .Select(j => j.Id)
                        .ToArray();
                    bool Succeeded(HotelM2PlannedJob j) =>
                        kind == "create"
                            ? purchaseSucceeded(j.Id, end)
                            : observed[j.Id].Useful && observed[j.Id].Completed <= end;
                    var useful = eligible.Count(Succeeded);
                    var rawUseful = raw.Count(Succeeded);
                    var latencies = eligible
                        .Select(j => (observed[j.Id].Completed - j.Due) * 1000)
                        .Order()
                        .ToArray();
                    double? p95 =
                        kind == "search" && latencies.Length > 0
                            ? latencies[(int)Math.Ceiling(latencies.Length * .95) - 1]
                            : null;
                    return new HotelM2RecoveryKind(
                        kind,
                        raw.Length,
                        rawUseful,
                        eligible.Select(j => j.Id).ToArray(),
                        carry,
                        useful,
                        p95,
                        eligible.Length > 0
                            && useful >= eligible.Length * .95
                            && (kind != "search" || p95 <= 1000)
                    );
                })
                .ToArray();
            var passed = groups.All(g => g.Passed);
            if (passed && recovered is null)
                recovered = end;
            windows.Add(new(end, groups, passed));
        }
        return new(legacyPassed, false, recovered, windows.ToArray());
    }
}
