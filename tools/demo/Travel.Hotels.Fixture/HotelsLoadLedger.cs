namespace Travel.Hotels.Fixture;

internal sealed record HotelLoadJob(int Id, string Kind, double Due, string? Provider = null);

internal sealed record HotelLoadEntry(
    int Id,
    string Kind,
    double Due,
    double? Started,
    double? TerminalAt,
    string? Terminal,
    int? Status
);

internal sealed record HotelLoadLedgerSnapshot(
    int Planned,
    bool AllObserved,
    bool Sealed,
    IReadOnlyDictionary<string, int> Counts,
    HotelLoadEntry[] Entries
);

// The parent owns this finite plan and all terminal observations. A stopped run retains
// jobs that never reached HTTP; snapshots never invent zero latency for missing work.
internal sealed class HotelsLoadLedger
{
    private static readonly string[] Terminals =
    [
        "Planned",
        "Response",
        "Cancelled",
        "FailedBeforeResponse",
        "NotDispatched",
        "NotRun",
        "Unfinished",
    ];
    private readonly object gate = new();
    private readonly Dictionary<int, HotelLoadEntry> entries;
    private bool closed;

    public HotelsLoadLedger(IEnumerable<HotelLoadJob> plan)
    {
        var jobs = plan.ToArray();
        if (
            jobs.Length == 0
            || jobs.Select(j => j.Id).Distinct().Count() != jobs.Length
            || jobs.Any(j =>
                j.Id < 0
                || !Finite(j.Due)
                || j.Kind
                    is not ("search" or "rates" or "prepare" or "create" or "status" or "callback")
            )
        )
            throw new ArgumentException("A unique finite Hotels job plan is required.");
        entries = jobs.ToDictionary(
            j => j.Id,
            j => new HotelLoadEntry(j.Id, j.Kind, j.Due, null, null, null, null)
        );
    }

    public bool Begin(int id, double at)
    {
        lock (gate)
        {
            var entry = entries[id];
            if (closed)
                return false;
            if (
                !Finite(at)
                || at < entry.Due
                || entry.Started is not null
                || entry.Terminal is not null
            )
                throw new InvalidOperationException("Invalid or repeated job start.");
            entries[id] = entry with { Started = at };
            return true;
        }
    }

    public bool Complete(int id, string terminal, double at, int? status)
    {
        lock (gate)
        {
            var entry = entries[id];
            if (closed)
                return false; // Late driver results cannot rewrite the stop snapshot.
            if (
                entry.Started is null
                || entry.Terminal is not null
                || !Finite(at)
                || at < entry.Started
                || terminal
                    is not ("Response" or "Cancelled" or "FailedBeforeResponse" or "NotDispatched")
                || status is < 100 or > 599
                || terminal == "Response" && status is null
                || terminal == "NotDispatched" && status is not null
            )
                throw new InvalidOperationException("Invalid or repeated terminal observation.");
            entries[id] = entry with { TerminalAt = at, Terminal = terminal, Status = status };
            return true;
        }
    }

    // An exception before a response can occur even before a valid Begin; do not invent Started.
    public bool FailBeforeResponse(int id, double at)
    {
        if (!Finite(at))
            throw new ArgumentOutOfRangeException(nameof(at));
        lock (gate)
        {
            var entry = entries[id];
            if (closed || entry.Terminal is not null)
                return false;
            if (entry.Started > at)
                throw new InvalidOperationException("Failure precedes actual start.");
            entries[id] = entry with { Terminal = "FailedBeforeResponse", TerminalAt = at };
            return true;
        }
    }

    public void Seal(double at)
    {
        if (!Finite(at))
            throw new ArgumentOutOfRangeException(nameof(at));
        lock (gate)
        {
            if (closed)
                return;
            if (entries.Values.Any(e => e.Started > at || e.TerminalAt > at))
                throw new InvalidOperationException("Stop cannot precede an observed event.");
            foreach (var entry in entries.Values.Where(e => e.Terminal is null).ToArray())
                entries[entry.Id] = entry with
                {
                    Terminal = entry.Started is null ? "NotRun" : "Unfinished",
                    TerminalAt = at,
                };
            closed = true;
        }
    }

    public HotelLoadLedgerSnapshot Snapshot()
    {
        lock (gate)
        {
            var snapshot = entries.Values.OrderBy(e => e.Id).ToArray();
            return new(
                snapshot.Length,
                snapshot.All(e => e.Terminal is not (null or "NotRun" or "Unfinished")),
                closed,
                Terminals.ToDictionary(
                    t => t,
                    t => snapshot.Count(e => (e.Terminal ?? "Planned") == t)
                ),
                snapshot
            );
        }
    }

    // A cohort's start/end are explicit: callers must not carry a fixed-phase Useful
    // boolean into a different rolling recovery window.
    public static bool PurchaseSuccess(
        double due,
        double httpCompleted,
        double? authoritativeObservedAt,
        int? status,
        bool sourceMatched,
        int? effects,
        double cohortStart,
        double cutoff
    ) =>
        Finite(due)
        && Finite(httpCompleted)
        && Finite(cohortStart)
        && Finite(cutoff)
        && cohortStart < cutoff
        && due >= cohortStart
        && due < cutoff
        && sourceMatched
        && effects == 1
        && status is >= 200 and < 300
        && authoritativeObservedAt is { } observed
        && Finite(observed)
        && httpCompleted >= due
        && observed >= due
        && httpCompleted <= Math.Min(due + 150, cutoff)
        && observed <= Math.Min(due + 150, cutoff);

    public static bool PhasePurchaseSuccess(
        double due,
        double httpCompleted,
        double? observedAt,
        int? status,
        bool sourceMatched,
        int? effects,
        double phaseStart,
        double phaseEnd
    ) =>
        PurchaseSuccess(
            due,
            httpCompleted,
            observedAt,
            status,
            sourceMatched,
            effects,
            phaseStart,
            phaseEnd
        );

    public static bool WindowPurchaseSuccess(
        double due,
        double httpCompleted,
        double? observedAt,
        int? status,
        bool sourceMatched,
        int? effects,
        double end
    ) =>
        PurchaseSuccess(
            due,
            httpCompleted,
            observedAt,
            status,
            sourceMatched,
            effects,
            end - 60,
            end
        );

    private static bool Finite(double value) => double.IsFinite(value) && value >= 0;
}
