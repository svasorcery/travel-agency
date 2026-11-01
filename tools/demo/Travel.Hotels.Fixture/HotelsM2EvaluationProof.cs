using System.Runtime.CompilerServices;
using System.Text.Json;
using Travel.Modules.Hotels.Core;
using Travel.Modules.Hotels.Infrastructure.Providers.Duffel;
using Travel.Modules.Hotels.Tests.Integration;

namespace Travel.Hotels.Fixture;

internal sealed partial class HotelsL1Runner
{
    // Pure saved-fact evaluation. No process constructor, HTTP, database, waits or source effects.
    internal static object VerifyM2Evaluation()
    {
        var receipts = new List<object>();
        foreach (var version in new[] { 1, 2, 3, 4, 5 })
        foreach (var code in new[] { "S", "L" })
        {
            foreach (
                var mutation in new[]
                {
                    "none",
                    "duplicate-effect",
                    "wrong-source",
                    "capacity",
                    "late-confirmation",
                    "retained-memory-growth",
                    "diagnostic-rss-growth",
                    "ceiling",
                    "baseline-tolerance",
                    "redundant-due",
                    "required-due",
                    "unknown-due",
                    "pending-inbox",
                    "lost-reference",
                    "missing-successful-source-step",
                    "wrong-timeout-reason",
                }
            )
            {
                var plan = new HotelM2LoadSchedule(new(code, new(2026, 11, 6)));
                var ghost = (HotelsL1HostProcess)
                    RuntimeHelpers.GetUninitializedObject(typeof(HotelsL1HostProcess));
                var runner = new HotelsL1Runner(ghost, Path.GetTempPath());
                runner.m2Evidence = new(
                    plan.Jobs.Select(j => new HotelLoadJob(j.Id, j.Kind, j.Due, j.Provider))
                );
                var epoch = DateTimeOffset.UnixEpoch;
                var creates = plan.Jobs.Where(j => j.Kind == "create").ToArray();
                var steps = plan
                    .Steps.Where(s =>
                        s.ReadAttempt is -1 or 0
                        && s.Due < 360
                        && !(plan.Jobs[s.JobId].Kind == "search" && Phase(s.Due) == "Burst")
                    )
                    .ToArray();
                if (mutation == "missing-successful-source-step")
                    steps = steps
                        .Where(s =>
                            s.JobId != plan.Jobs.First(j => j.Kind == "search" && j.Due >= 30).Id
                            || s.Provider != "hotelbeds"
                        )
                        .ToArray();
                var rows = plan
                    .Seeds.Select(seed =>
                    {
                        var job = creates.SingleOrDefault(j => j.ReservationId == seed.Id);
                        var observed = plan.Jobs.SingleOrDefault(j =>
                            j.Kind == "callback"
                            && Phase(j.Due) != "Burst"
                            && j.ReservationId == seed.Id
                        );
                        var attempt = job is null
                            ? seed.Role == "draft"
                                ? (Guid?)null
                                : plan.Profile.Id(
                                    "seed-attempt",
                                    seed.Index + (seed.Provider == "hotelbeds" ? 100 : 0)
                                )
                            : plan.Profile.Id("attempt", job.Id);
                        var known =
                            seed.Role == "confirmed"
                            || observed is not null
                            || job is not null && job.Due != 154.5;
                        if (mutation == "lost-reference" && job?.Due == 154.5)
                            known = true;
                        var outcome =
                            attempt is null ? ReservationOutcome.Quoted
                            : known ? ReservationOutcome.Confirmed
                            : ReservationOutcome.Unknown;
                        var read =
                            observed is not null || job is { Provider: "hotelbeds" } && known
                                ? 1
                                : 0;
                        var due = job?.Due ?? observed?.Due ?? 0;
                        return new HotelM2FinalRow(
                            seed.Id,
                            seed.Provider,
                            attempt,
                            attempt is not null,
                            outcome,
                            known,
                            read,
                            3,
                            known ? epoch.AddSeconds(due) : null,
                            known ? epoch.AddSeconds(due + 300) : null,
                            attempt is null ? null : epoch.AddSeconds(due),
                            epoch.AddSeconds(due + .02)
                        );
                    })
                    .ToArray();
                HotelWireSnapshot Source(string provider, int second, bool boundary)
                {
                    var sourceSteps = steps.Where(s => s.Provider == provider).ToArray();
                    var started = sourceSteps.Where(s => s.Due <= second).ToArray();
                    var completed = sourceSteps.Count(s => s.Due + .01 <= second);
                    var events = new List<HotelWireEvent>();
                    foreach (var step in sourceSteps)
                    {
                        var capacity = new HotelSourceCapacitySnapshot(
                            provider,
                            new(
                                epoch.AddSeconds((Math.Floor(step.Due / 60) + 1) * 60),
                                started.Count(s =>
                                    s.Due <= step.Due
                                    && Math.Floor(s.Due / 60) == Math.Floor(step.Due / 60)
                                    && s.Operation != "read"
                                ),
                                started.Count(s =>
                                    s.Due <= step.Due
                                    && Math.Floor(s.Due / 60) == Math.Floor(step.Due / 60)
                                    && s.Operation == "read"
                                ),
                                step.Operation == "read" ? 0 : 1,
                                step.Operation == "read" ? 1 : 0,
                                started.Length,
                                2
                            ),
                            0
                        );
                        var lower = second == 0 ? -1 : second - 1;
                        if (step.Due > lower && step.Due <= second)
                            events.Add(
                                new(
                                    step.Ordinal + 1,
                                    (long)(step.Due * System.Diagnostics.Stopwatch.Frequency),
                                    step.Operation,
                                    "Started",
                                    null,
                                    capacity,
                                    mutation == "wrong-source" && step.JobId == creates[0].Id
                                        ? -1
                                        : step.JobId,
                                    step.Ordinal,
                                    step.ReadAttempt
                                )
                            );
                        if (step.Due + .01 > lower && step.Due + .01 <= second)
                            events.Add(
                                new(
                                    step.Ordinal + 1,
                                    (long)(
                                        (step.Due + .01) * System.Diagnostics.Stopwatch.Frequency
                                    ),
                                    step.Operation,
                                    "Response",
                                    200,
                                    null,
                                    step.JobId,
                                    step.Ordinal,
                                    step.ReadAttempt
                                )
                            );
                    }
                    var effects = creates
                        .Where(j => j.Provider == provider && j.Due + .01 <= second)
                        .ToDictionary(
                            j =>
                                HotelWireMeasurements.Correlation(
                                    provider,
                                    plan.Profile.Id("attempt", j.Id)
                                ),
                            _ => 1
                        );
                    if (mutation == "duplicate-effect" && effects.Count > 0)
                        effects[effects.Keys.First()] = 2;
                    return new(
                        provider,
                        started.Length,
                        completed,
                        started.Length - completed,
                        2,
                        false,
                        !boundary,
                        System.Diagnostics.Stopwatch.Frequency,
                        new(0, 0, effects.Count, boundary ? 0 : null),
                        effects,
                        0,
                        boundary ? 0 : null,
                        boundary ? [] : events.ToArray()
                    );
                }
                var initial = new[]
                {
                    Source("duffel-stays", -1, true),
                    Source("hotelbeds", -1, true),
                };
                runner.m2Evidence.Boundary("beforeTimed", initial);
                foreach (var job in plan.Jobs)
                {
                    var aborted = job.Kind == "search" && job.Due == 162;
                    var limited = job.Kind == "search" && job.Due == 186;
                    var burstSearch = job.Kind == "search" && Phase(job.Due) == "Burst";
                    var outcome =
                        job.Kind == "create"
                            ? job.Due == 154.5
                                ? "Unknown"
                                : "Confirmed"
                            : limited
                                ? "Limited"
                                : "Complete";
                    var useful = !aborted && !limited && !burstSearch && job.Kind != "create";
                    var terminal = aborted ? "Cancelled" : "Response";
                    var status =
                        aborted ? (int?)null
                        : burstSearch ? 429
                        : 200;
                    var op = new HotelM2DispatchResult(
                        job.Id,
                        terminal,
                        job.Due,
                        job.Due + .01,
                        status,
                        outcome,
                        useful,
                        null,
                        job.ReservationId,
                        job.Provider,
                        job.Kind == "create"
                    );
                    op = op with
                    {
                        SourceStatuses =
                            job.Kind == "search"
                                ?
                                [
                                    new("duffel-stays", ExecutionKind.Complete, null),
                                    new(
                                        "hotelbeds",
                                        limited ? ExecutionKind.Failed : ExecutionKind.Complete,
                                        limited
                                            ? mutation == "wrong-timeout-reason"
                                                ? "Other"
                                                : "Timeout"
                                            : null
                                    ),
                                ]
                                : [],
                    };
                    runner.loadOperations.Add(op);
                    runner.operations.Add(
                        new(
                            Phase(job.Due),
                            job.Kind,
                            job.Due,
                            job.Due,
                            job.Due + .01,
                            status ?? 0,
                            outcome,
                            useful,
                            null,
                            job.ReservationId,
                            job.Id
                        )
                    );
                    if (job.Kind == "create")
                        runner.m2Evidence.RecordHttp(
                            job.Id,
                            job.ReservationId,
                            job.Provider,
                            job.Due + .01,
                            200,
                            true
                        );
                }
                for (var second = 0; second <= 360; second++)
                {
                    var sources = new[]
                    {
                        Source("duffel-stays", second, false),
                        Source("hotelbeds", second, false),
                    };
                    var capacities = sources
                        .Select(s => new HotelSourceCapacitySnapshot(
                            s.Provider,
                            new(
                                epoch.AddSeconds((Math.Floor(second / 60d) + 1) * 60),
                                mutation == "capacity" ? 25 : 0,
                                0,
                                0,
                                0,
                                s.Starts,
                                2
                            ),
                            s.Completions
                        ))
                        .ToArray();
                    var hasDue =
                        second == 360
                        && mutation is "redundant-due" or "required-due" or "unknown-due";
                    var disposition =
                        mutation == "redundant-due" ? "redundant"
                        : mutation == "unknown-due" ? "unknown"
                        : "required";
                    var queue = new HotelM2QueueSnapshot(
                        hasDue ? 1 : 0,
                        hasDue && disposition == "required" ? 1 : 0,
                        hasDue && disposition == "redundant" ? 1 : 0,
                        hasDue && disposition == "unknown" ? 1 : 0,
                        !hasDue || disposition != "unknown",
                        hasDue
                            ?
                            [
                                new HotelM2DueItem(
                                    plan.Profile.Id("proof-work", second),
                                    "proof",
                                    epoch.AddSeconds(359),
                                    119,
                                    rows[0].Id,
                                    "Confirmed",
                                    false,
                                    disposition,
                                    "Synthetic"
                                ),
                            ]
                            : []
                    );
                    runner.loadFrames.Add(
                        new(second, second, sources, capacities)
                        {
                            Queue = queue,
                            RawDue = queue.RawDue,
                            PendingInbox = mutation == "pending-inbox" && second == 360 ? 1 : 0,
                        }
                    );
                    var observed = rows.Select(r =>
                        {
                            var job = creates.SingleOrDefault(j => j.ReservationId == r.Id);
                            var visible = job is null || job.Due + .01 <= second;
                            var confirmed =
                                r.Outcome == ReservationOutcome.Confirmed
                                && visible
                                && !(
                                    mutation == "late-confirmation"
                                    && job is { Provider: "hotelbeds" }
                                );
                            return new HotelObservedPurchase(
                                r.Id,
                                visible ? r.AttemptId : null,
                                r.Provider,
                                confirmed,
                                true
                            );
                        })
                        .ToArray();
                    runner.m2Evidence.Sample(second, sources, observed);
                    var rss = mutation switch
                    {
                        "retained-memory-growth" when second >= 330 => 100 * 1024 * 1024 + 1,
                        "diagnostic-rss-growth" when second >= 355 => 90 * 1024 * 1024 + 4096,
                        "diagnostic-rss-growth" when second >= 330 => 90 * 1024 * 1024,
                        "ceiling" => 1073741824 + 1,
                        "baseline-tolerance" when second >= 330 => 229 * 1024 * 1024,
                        "baseline-tolerance" when second >= 300 => 240 * 1024 * 1024,
                        _ => 100 * 1024 * 1024,
                    };
                    runner.resources.Add(
                        new(
                            second,
                            rss,
                            0,
                            0,
                            1,
                            100,
                            38,
                            41,
                            100,
                            mutation == "pending-inbox" && second == 360 ? 1 : 0,
                            0,
                            second == 360
                            && mutation is "redundant-due" or "required-due" or "unknown-due"
                                ? 1
                                : 0,
                            0,
                            0,
                            new(epoch, 0, 0, 0, 0, 0, 0)
                        )
                    );
                }
                var final = new[]
                {
                    Source("duffel-stays", 360, true),
                    Source("hotelbeds", 360, true),
                };
                runner.m2Evidence.Boundary("afterTimed", final);
                runner.setup = JsonSerializer.SerializeToElement(
                    new { initialSources = initial },
                    HotelsL1Frames.Json
                );
                runner.finalFacts = JsonSerializer.SerializeToElement(
                    new { m2Rows = rows, finalSources = final },
                    HotelsL1Frames.Json
                );
                for (var i = 0; i < 36; i++)
                    runner.databaseResources.Add(new { synthetic = true });
                var assessment = runner.AssessM2(plan, version);
                var gates = assessment.Gates;
                var expected =
                    mutation == "none"
                    || version >= 2 && mutation == "diagnostic-rss-growth"
                    || version >= 3 && mutation == "redundant-due"
                    || version >= 5 && mutation == "retained-memory-growth";
                var passed = gates.Values.All(v => v);
                if (passed != expected)
                    throw new InvalidOperationException(
                        $"M2 evaluation self-check {code}/{mutation}: {string.Join(',', gates.Where(g => !g.Value).Select(g => g.Key))}"
                    );
                receipts.Add(
                    new
                    {
                        profile = code,
                        mutation,
                        expected,
                        acceptanceVersion = version,
                        memoryDiagnostic = assessment.Diagnostic,
                        rssTrendDiagnostic = assessment.RssTrendDiagnostic,
                        queueDiagnostic = assessment.QueueDiagnostic,
                        recoveryDiagnostic = assessment.RecoveryDiagnostic,
                        actual = passed,
                        gates,
                    }
                );
            }
        }
        return new
        {
            recoveryOrigins = new[] { "S", "L" }.ToDictionary(
                code => code,
                code =>
                    HotelsM2RecoveryCohort.Origins(
                        new HotelM2LoadSchedule(new(code, new(2026, 11, 6))).Jobs
                    )
            ),
            queueEvidence = VerifyRejectedQueueEvidence(),
            sourceOnly = true,
            httpRequests = 0,
            hostProcesses = 0,
            database = false,
            passed = receipts.Count,
            cases = receipts,
        };
    }

    private static object VerifyRejectedQueueEvidence()
    {
        var verified = new List<string>();
        foreach (var kind in new[] { "unknown", "overflow", "raw-count-mismatch" })
        {
            var ghost = (HotelsL1HostProcess)
                RuntimeHelpers.GetUninitializedObject(typeof(HotelsL1HostProcess));
            var spec = new HotelM2LoadRunSpec(
                3,
                "S",
                new(2026, 11, 6),
                "TestResults/hotels-m2-memory-load-acceptance/proof",
                "",
                [],
                "",
                "",
                ""
            );
            typeof(HotelsL1HostProcess)
                .GetField(
                    "<LoadSpec>k__BackingField",
                    System.Reflection.BindingFlags.Instance
                        | System.Reflection.BindingFlags.NonPublic
                )!
                .SetValue(ghost, spec);
            var runner = new HotelsL1Runner(ghost, Path.GetTempPath());
            var id = Guid.NewGuid();
            var item = new HotelM2DueItem(
                id,
                "Unknown.Hotels.Work",
                DateTimeOffset.UnixEpoch,
                119,
                null,
                null,
                null,
                "unknown",
                kind
            );
            var queue = kind switch
            {
                "unknown" => new HotelM2QueueSnapshot(1, 0, 0, 1, false, [item]),
                "overflow" => new HotelM2QueueSnapshot(
                    65,
                    0,
                    64,
                    1,
                    false,
                    Enumerable
                        .Range(0, 64)
                        .Select(_ => item with { Disposition = "redundant" })
                        .ToArray()
                ),
                _ => new HotelM2QueueSnapshot(0, 0, 0, 0, true, []),
            };
            var rawDue = kind == "raw-count-mismatch" ? 1 : queue.RawDue;
            var sample = JsonSerializer.SerializeToElement(
                new
                {
                    metrics = new
                    {
                        at = 0,
                        dueWork = rawDue,
                        pendingInbox = 0,
                    },
                    sourceMeasurements = Array.Empty<HotelWireSnapshot>(),
                    sourceCapacity = Array.Empty<HotelSourceCapacitySnapshot>(),
                    queueObservation = queue,
                },
                Json
            );
            var rejected = false;
            try
            {
                runner.ObserveM2Sample(sample, 0);
            }
            catch (InvalidDataException)
            {
                rejected = true;
            }
            if (!rejected)
                throw new InvalidOperationException("Invalid queue sample did not stop execution.");
            var path = Path.Combine(
                Path.GetTempPath(),
                "hotels-m2-queue-proof-" + Guid.NewGuid().ToString("N") + ".json"
            );
            try
            {
                using (var file = new FileStream(path, FileMode.CreateNew))
                    JsonSerializer.Serialize(file, runner.loadFrames.ToArray(), Json);
                using var saved = JsonDocument.Parse(File.ReadAllText(path));
                if (saved.RootElement.GetArrayLength() != 1)
                    throw new InvalidOperationException(
                        "Rejected queue sample was lost before artifact write: " + kind
                    );
                var frame = saved.RootElement[0];
                if (
                    frame.GetProperty("rawDue").GetInt32() != rawDue
                    || frame.GetProperty("queue").GetProperty("rawDue").GetInt32() != queue.RawDue
                )
                    throw new InvalidOperationException("Rejected raw counts changed: " + kind);
                if (
                    queue.Items.Length > 0
                    && frame
                        .GetProperty("queue")
                        .GetProperty("items")[0]
                        .GetProperty("envelopeId")
                        .GetGuid() != id
                )
                    throw new InvalidOperationException("Rejected envelope identity lost.");
            }
            finally
            {
                if (
                    Path.GetDirectoryName(Path.GetFullPath(path))
                        != Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()))
                    || !Path.GetFileName(path)
                        .StartsWith("hotels-m2-queue-proof-", StringComparison.Ordinal)
                )
                    throw new InvalidOperationException("Temporary proof path changed.");
                File.Delete(path);
            }
            verified.Add(kind);
        }
        return new
        {
            passed = verified.Count,
            cases = verified,
            runtime = false,
        };
    }
}
