namespace Travel.Modules.Hotels.Tests.Integration;

public sealed record HotelM2PlannedJob(
    int Id,
    double Due,
    string Kind,
    string? Provider = null,
    int? Prerequisite = null,
    Guid? ReservationId = null,
    int? PurchaseIndex = null
);

public sealed record HotelM2SeedRow(Guid Id, string Provider, string Role, int Index);

public sealed record HotelM2PlannedStep(
    int Ordinal,
    int JobId,
    string Provider,
    string Operation,
    double Due,
    int ReadAttempt = -1
);

public sealed record HotelM2WireFault(
    int DelayMilliseconds,
    bool SafeFailure = false,
    bool IgnoreCancellation = false,
    bool LoseReplyAfterEffect = false
);

// Concrete two-profile workload metadata; no response objects or unbounded dispatch queue.
public sealed class HotelM2LoadSchedule
{
    public HotelM2LoadProfile Profile { get; }
    public IReadOnlyList<HotelM2PlannedJob> Jobs { get; }
    public IReadOnlyList<HotelM2SeedRow> Seeds { get; }
    public IReadOnlyList<HotelM2PlannedStep> Steps { get; }

    public HotelM2LoadSchedule(HotelM2LoadProfile profile)
    {
        Profile = profile;
        var seeds = new List<HotelM2SeedRow>();
        foreach (var source in new[] { "duffel-stays", "hotelbeds" })
        {
            for (var i = 0; i < 12; i++)
                seeds.Add(new(profile.Id(source + ":draft", i), source, "draft", i));
            seeds.Add(new(profile.Id(source + ":confirmed", 0), source, "confirmed", 0));
        }
        for (var i = 0; i < 12; i++)
            seeds.Add(
                new(profile.Id("duffel-stays:observation", i), "duffel-stays", "observation", i)
            );
        Seeds = seeds.AsReadOnly();
        var raw = new List<(double Due, string Kind, int Sequence)>();
        foreach (
            var (start, duration, burst) in new[]
            {
                (0d, 30d, false),
                (30d, 120d, false),
                (150d, 60d, false),
                (210d, 30d, true),
                (240d, 120d, false),
            }
        )
        {
            Add("status", 0, .2);
            Add("search", 0, burst ? .025 : 6);
            Add("callback", burst ? 0 : 8, burst ? 1 : 30);
            if (!burst)
            {
                Add("rates", 1.2, 15);
                Add("prepare", 3, 20);
                Add("create", 4.5, 20);
            }
            void Add(string kind, double offset, double interval)
            {
                for (var i = 0; offset + i * interval < duration - .000001; i++)
                    raw.Add((start + offset + i * interval, kind, raw.Count));
            }
        }
        var jobs = raw.OrderBy(j => j.Due)
            .ThenBy(j => j.Kind, StringComparer.Ordinal)
            .ThenBy(j => j.Sequence)
            .Select((j, id) => new HotelM2PlannedJob(id, j.Due, j.Kind))
            .ToArray();
        var prepares = jobs.Where(j => j.Kind == "prepare").ToArray();
        var creates = jobs.Where(j => j.Kind == "create").ToArray();
        for (var i = 0; i < prepares.Length; i++)
        {
            var source = profile.PurchaseSource(i);
            var row = seeds.Single(s =>
                s.Role == "draft" && s.Provider == source && s.Index == i / 2
            );
            var rates = jobs.Last(j => j.Kind == "rates" && j.Due < prepares[i].Due);
            jobs[prepares[i].Id] = prepares[i] with
            {
                Provider = source,
                ReservationId = row.Id,
                Prerequisite = rates.Id,
                PurchaseIndex = i,
            };
            jobs[creates[i].Id] = creates[i] with
            {
                Provider = source,
                ReservationId = row.Id,
                Prerequisite = prepares[i].Id,
                PurchaseIndex = i,
            };
        }
        var statusIndex = 0;
        var observationIndex = 0;
        for (var i = 0; i < jobs.Length; i++)
        {
            var job = jobs[i];
            if (job.Kind == "rates")
                jobs[i] = job with
                {
                    Prerequisite = jobs.Last(j => j.Kind == "search" && j.Due < job.Due).Id,
                };
            if (job.Kind == "status")
            {
                var source = (statusIndex++ % 2 == 0) ? "duffel-stays" : "hotelbeds";
                jobs[i] = job with
                {
                    Provider = source,
                    ReservationId = seeds
                        .Single(s => s.Role == "confirmed" && s.Provider == source)
                        .Id,
                };
            }
            if (job.Kind == "callback")
            {
                var selectedObservation = Phase(job.Due) == "Burst" ? -1 : observationIndex++;
                var seed =
                    Phase(job.Due) == "Burst"
                        ? seeds.Single(s => s.Role == "confirmed" && s.Provider == "duffel-stays")
                        : seeds.Single(s =>
                            s.Role == "observation" && s.Index == selectedObservation
                        );
                jobs[i] = job with { Provider = "duffel-stays", ReservationId = seed.Id };
            }
        }
        if (jobs.Length != 3152 || seeds.Count != 38 || observationIndex != 11)
            throw new InvalidOperationException("Frozen workload denominator changed.");
        Jobs = Array.AsReadOnly(jobs);
        var steps = new List<HotelM2PlannedStep>();
        foreach (var job in jobs)
        {
            if (job.Kind is "search" or "rates")
            {
                steps.Add(new(0, job.Id, "duffel-stays", job.Kind, job.Due));
                steps.Add(new(0, job.Id, "hotelbeds", "availability", job.Due));
            }
            else if (job.Kind is "prepare" or "create")
                steps.Add(new(0, job.Id, job.Provider!, job.Kind, job.Due));
            if (
                job.Kind == "callback" && Phase(job.Due) != "Burst"
                || job.Kind == "create" && job.Provider == "hotelbeds" && job.Due != 154.5
            )
                for (var read = 0; read < 3; read++)
                    steps.Add(
                        new(
                            0,
                            job.Id,
                            job.Provider!,
                            "read",
                            job.Due + new[] { 0, 30, 120 }[read],
                            read
                        )
                    );
        }
        var ordinals = new Dictionary<string, int> { { "duffel-stays", 0 }, { "hotelbeds", 0 } };
        Steps = Array.AsReadOnly(
            steps
                .OrderBy(s => s.Due)
                .ThenBy(s => s.JobId)
                .ThenBy(s => s.Operation, StringComparer.Ordinal)
                .Select(s => s with { Ordinal = ordinals[s.Provider]++ })
                .ToArray()
        );
    }

    public HotelM2WireFault Fault(HotelM2PlannedStep step)
    {
        var job = Jobs[step.JobId];
        if (job.Kind == "search" && job.Due == 162)
            return step.Provider == "hotelbeds" ? new(18000, IgnoreCancellation: true) : new(3000);
        if (job.Kind == "search" && job.Due == 186)
            return new(step.Provider == "hotelbeds" ? 16000 : 100);
        if (
            job.Due == 150 && job.Kind == "search"
            || job.Due == 151.2 && job.Kind == "rates"
            || job.Due == 153 && job.Kind == "prepare"
        )
            return new(100);
        if (job.Kind == "create" && job.Due == 154.5)
            return new(100, LoseReplyAfterEffect: true);
        if (Phase(step.Due) == "Burst")
            return new(500);
        if (Phase(step.Due) == "Degraded" && step.Operation != "create")
            return (step.Ordinal % 10) switch
            {
                0 or 1 or 2 => new(3000),
                9 => new(100, SafeFailure: true),
                _ => new(100),
            };
        return new(100);
    }

    public static string Phase(double at) =>
        at < 30 ? "Warmup"
        : at < 150 ? "Normal"
        : at < 210 ? "Degraded"
        : at < 240 ? "Burst"
        : at < 300 ? "RecoveryAdmission"
        : "RecoverySteady";
}
