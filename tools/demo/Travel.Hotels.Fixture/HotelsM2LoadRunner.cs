using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Travel.Host.Tests.Integration.Hotels;
using Travel.Modules.Hotels.Core;
using Travel.Modules.Hotels.Infrastructure.Providers.Duffel;
using Travel.Modules.Hotels.Tests.Integration;

namespace Travel.Hotels.Fixture;

internal sealed record HotelM2SourceFrame(
    int PlannedSecond,
    double ObservedAt,
    HotelWireSnapshot[] Sources,
    HotelSourceCapacitySnapshot[] Capacity
)
{
    public HotelM2QueueSnapshot? Queue { get; init; }
    public int RawDue { get; init; }
    public int PendingInbox { get; init; }
}

internal sealed partial class HotelsL1Runner
{
    private readonly ConcurrentBag<HotelM2SourceFrame> loadFrames = [];
    private readonly ConcurrentBag<HotelM2DispatchResult> loadOperations = [];
    private readonly ConcurrentBag<object> databaseResources = [];
    private readonly ConcurrentBag<object> environmentResources = [];

    private void ObserveM2Sample(JsonElement sample, int second)
    {
        if (host.LoadSpec is null)
            return;
        var at = clock.Elapsed.TotalSeconds;
        if (
            at > second + 1
            || sample.GetProperty("metrics").GetProperty("at").GetDouble() > second + 1
        )
            throw new InvalidOperationException("M2 sample missed its declared one-second window.");
        var queue = sample.TryGetProperty("queueObservation", out var observedQueue)
            ? observedQueue.Deserialize<HotelM2QueueSnapshot>(HotelsL1Frames.Json)
            : null;
        var rawDue = sample.GetProperty("metrics").GetProperty("dueWork").GetInt32();
        var pendingInbox = sample.GetProperty("metrics").GetProperty("pendingInbox").GetInt32();
        loadFrames.Add(
            new(
                second,
                at,
                sample
                    .GetProperty("sourceMeasurements")
                    .Deserialize<HotelWireSnapshot[]>(HotelsL1Frames.Json)!,
                sample
                    .GetProperty("sourceCapacity")
                    .Deserialize<HotelSourceCapacitySnapshot[]>(HotelsL1Frames.Json)!
            )
            {
                Queue = queue,
                RawDue = rawDue,
                PendingInbox = pendingInbox,
            }
        );
        if (
            host.LoadSpec.Version >= 3
            && (
                queue is null
                || !HotelsM2QueueObservation.Valid(queue)
                || queue.RawDue != sample.GetProperty("metrics").GetProperty("dueWork").GetInt32()
            )
        )
            throw new InvalidDataException("Due-work classification is incomplete.");

        HotelM2ResourceGuard.Check(
            host.LoadSpec,
            output,
            value =>
                environmentResources.Add(
                    new
                    {
                        stage = "timed",
                        plannedSecond = second,
                        observedAt = clock.Elapsed.TotalSeconds,
                        value,
                    }
                )
        );
    }

    private async Task RunM2Async(string manifestPath, CancellationToken ct)
    {
        var spec = host.LoadSpec!;
        var plan = new HotelM2LoadSchedule(spec.Profile);
        ledger = new HotelsLoadLedger(
            plan.Jobs.Select(j => new HotelLoadJob(j.Id, j.Kind, j.Due, j.Provider))
        );
        m2Evidence = new HotelsM2Evidence(
            plan.Jobs.Select(j => new HotelLoadJob(j.Id, j.Kind, j.Due, j.Provider))
        );
        async Task Save(string name, object value)
        {
            await using var file = new FileStream(
                Path.Combine(output, name),
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.Read
            );
            await JsonSerializer.SerializeAsync(file, value, Json, CancellationToken.None);
        }
        using var timed = CancellationTokenSource.CreateLinkedTokenSource(ct);
        using var dispatchDeadline = CancellationTokenSource.CreateLinkedTokenSource(timed.Token);
        Task? sampling = null;
        Task? databaseSampling = null;
        Task[] requests = [];
        HotelsM2Execution? execution = null;
        Dictionary<string, bool>? gates = null;
        HotelM2MemoryAssessment? assessment = null;
        string? failure = null;
        string stage = "setup";
        HotelM2ExecutionFailure? failureDetail = null;
        HotelM2ExecutionFailures executionFailures = new(null, false, []);
        try
        {
            await Save("planned-jobs.json", plan.Jobs);
            await Save("planned-source-steps.json", plan.Steps);
            HotelM2ResourceGuard.Check(
                spec,
                output,
                value => environmentResources.Add(new { stage = "before", value })
            );
            VerifyManifestFile(root, manifestPath, spec.Inputs);
            using (var setupDeadline = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                var remainingSetup =
                    TimeSpan.FromSeconds(120) - Stopwatch.GetElapsedTime(host.StartupTimestamp);
                if (remainingSetup <= TimeSpan.Zero)
                    throw new TimeoutException("M2 setup budget expired.");
                setupDeadline.CancelAfter(remainingSetup);
                var preflight = await VerifyOwnedConnectionAsync(setupDeadline.Token);
                await Save(
                    "db-before.json",
                    await HotelsM2Docker.OwnedStatsAsync(host, setupDeadline.Token)
                );
                setup = await host.CommandAsync("prepare", null, setupDeadline.Token);
                var sources = setup
                    .GetProperty("initialSources")
                    .Deserialize<HotelWireSnapshot[]>(HotelsL1Frames.Json)!;
                m2Evidence.Boundary("beforeTimed", sources);
                await Save(
                    "before-run.json",
                    new
                    {
                        profile = spec,
                        ready = host.Ready,
                        preflight,
                        setup,
                        measuredHostPid = host.Process.Id,
                        driverPid = Environment.ProcessId,
                        diagnosticOnly = false,
                        hard2GiBEnvironmentProven = false,
                    }
                );
            }
            var stock = setup
                .GetProperty("m2Stock")
                .Deserialize<HotelM2SeedStock[]>(HotelsL1Frames.Json)!
                .ToDictionary(s => s.Id);
            var dispatch = new HotelsM2LoadDispatch(
                host.Client,
                plan,
                () => clock.Elapsed.TotalSeconds,
                async (job, token) =>
                {
                    var row = stock[job.ReservationId!.Value];
                    if (row.Provider != "duffel-stays" || row.QuoteId is null)
                        throw new InvalidOperationException("Callback stock source mismatch.");
                    return await HotelsHostTests.SendWebhook(
                        host.Client,
                        HotelsHostTests.Event(
                            "evt_load_" + spec.Code + "_" + job.Id,
                            "stays.booking.created",
                            new JsonObject
                            {
                                ["quote_id"] = row.QuoteId,
                                ["booking_id"] = row.BookingId,
                            }
                        ),
                        token,
                        job.Id
                    );
                }
            );
            stage = "begin";
            began = DateTimeOffset.UtcNow;
            beganTimestamp = Stopwatch.GetTimestamp();
            clock.Start();
            await host.CommandAsync("begin", new { began, timestamp = beganTimestamp }, ct);
            dispatchDeadline.CancelAfter(TimeSpan.FromSeconds(360));
            execution = new HotelsM2Execution(
                ledger,
                timed,
                () => clock.Elapsed.TotalSeconds,
                WaitUntil,
                dispatch.ExecuteAsync,
                (job, result) =>
                {
                    loadOperations.Add(result);
                    if (job.Kind == "create")
                        m2Evidence.RecordHttp(
                            job.Id,
                            result.ResourceId,
                            result.ResponseProvider,
                            result.Completed,
                            result.Status,
                            result.PurchaseAccepted
                        );
                    operations.Add(
                        new(
                            Phase(job.Due),
                            job.Kind,
                            job.Due,
                            result.Started,
                            result.Completed,
                            result.Status ?? 0,
                            result.Outcome,
                            result.Useful,
                            result.Refusal,
                            result.ResourceId,
                            job.Id
                        )
                    );
                }
            );
            sampling = execution.ObserveAsync(() => SampleAsync(timed.Token), "sampling");
            databaseSampling = execution.ObserveAsync(
                () => SampleM2DatabaseAsync(timed),
                "database"
            );
            stage = "execution";
            requests = plan
                .Jobs.Select(job => execution.RunJobAsync(job, dispatchDeadline.Token))
                .ToArray();
            await execution.WaitAsync(requests, sampling, databaseSampling);
            await WaitUntil(360, timed.Token);
            stage = "final-facts";
            finalFacts = await host.CommandAsync("facts", null, timed.Token);
            m2Evidence.Boundary(
                "afterTimed",
                finalFacts
                    .GetProperty("finalSources")
                    .Deserialize<HotelWireSnapshot[]>(HotelsL1Frames.Json)!
            );
            ledger.Seal(clock.Elapsed.TotalSeconds);
            if (
                loadFrames.Count != 361
                || loadOperations.Count != 3152
                || !ledger.Snapshot().AllObserved
            )
                throw new InvalidOperationException(
                    "M2 full offered/sample denominator incomplete."
                );
            await Save("db-after.json", await HotelsM2Docker.OwnedStatsAsync(host, timed.Token));
            stage = "gates";
            assessment = AssessM2(plan, spec.Version);
            gates = assessment.Gates;
            spec.VerifyBuild(root);
            host.Completed = true; // Protocol completion, never a waiver for false gates.
            VerifyManifestFile(root, manifestPath, spec.Inputs);
        }
        catch (Exception ex)
        {
            failure = ex.GetType().Name;
            failureDetail =
                execution?.Snapshot().First
                ?? HotelsM2Execution.Describe(ex, stage, clock.Elapsed.TotalSeconds);
            try
            {
                await timed.CancelAsync().WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch { }
            try
            {
                await Task.WhenAll(requests).WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch { }
            if (sampling is not null)
            {
                try
                {
                    await sampling.WaitAsync(TimeSpan.FromSeconds(5));
                }
                catch { }
            }
            if (databaseSampling is not null)
            {
                try
                {
                    await databaseSampling.WaitAsync(TimeSpan.FromSeconds(5));
                }
                catch { }
            }
        }
        finally
        {
            ledger.Seal(clock.Elapsed.TotalSeconds);
            executionFailures = execution?.Seal() ?? new(null, false, []);
            bool Eligible() =>
                failure is null
                && gates is not null
                && gates.Values.All(v => v)
                && host.Process.HasExited
                && !ct.IsCancellationRequested;
            var receiptOwned = false;
            async Task Receipt(bool passed, IReadOnlyList<string> errors)
            {
                var value = new
                {
                    profile = spec.Code,
                    acceptanceVersion = spec.Version,
                    memoryDiagnostic = assessment?.Diagnostic,
                    rssTrendDiagnostic = assessment?.RssTrendDiagnostic,
                    queueDiagnostic = assessment?.QueueDiagnostic,
                    recoveryDiagnostic = assessment?.RecoveryDiagnostic,
                    passed,
                    failure,
                    failureDetail,
                    finalizationErrors = errors,
                    planned = 3152,
                    observed = loadOperations.Count,
                    gatesEvaluated = gates is not null,
                    gates,
                    elapsedSeconds = clock.Elapsed.TotalSeconds,
                    cancelled = ct.IsCancellationRequested,
                    resourcesAreWholeContourCapacityProof = false,
                    historicalM1 = "failed16/17 unchanged",
                };
                await using var file = new FileStream(
                    Path.Combine(output, "receipt.json"),
                    receiptOwned ? FileMode.Create : FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.Read
                );
                receiptOwned = true;
                await JsonSerializer.SerializeAsync(file, value, Json, CancellationToken.None);
            }
            var passed = await HotelsM2Completion.FinishAsync(
                async attempt =>
                {
                    async Task SaveAttempt(string name, object value) =>
                        await attempt(name, () => Save(name, value));
                    await SaveAttempt("ledger.json", ledger.Snapshot());
                    await SaveAttempt(
                        "execution-failures.json",
                        new { phaseFailure = failureDetail, execution = executionFailures }
                    );
                    await SaveAttempt(
                        "operations.json",
                        loadOperations.OrderBy(o => o.JobId).ToArray()
                    );
                    await SaveAttempt("resources.json", resources.OrderBy(r => r.At).ToArray());
                    await SaveAttempt(
                        "source-frames.json",
                        loadFrames.OrderBy(f => f.PlannedSecond).ToArray()
                    );
                    await SaveAttempt("driver-resources.json", driverResources.ToArray());
                    await SaveAttempt("database-resources.json", databaseResources.ToArray());
                    await SaveAttempt(
                        "memory-observations.json",
                        memorySamples.OrderBy(s => s.At).ToArray()
                    );
                    await SaveAttempt(
                        "final-facts.json",
                        finalFacts.ValueKind == JsonValueKind.Undefined
                            ? (object)new { available = false, reason = "FactsNotReached" }
                            : finalFacts
                    );
                    if (m2Evidence.Active)
                        await attempt(
                            "source-measurements",
                            () =>
                                m2Evidence.SaveAsync(
                                    Path.Combine(output, "source-measurements.json"),
                                    CancellationToken.None
                                )
                        );
                    await attempt(
                        "resource-after",
                        () =>
                        {
                            HotelM2ResourceGuard.Check(
                                spec,
                                output,
                                value =>
                                    environmentResources.Add(new { stage = "afterTimed", value })
                            );
                            return Task.CompletedTask;
                        }
                    );
                },
                async () =>
                {
                    using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                    try
                    {
                        await host.StopAsync(cleanup.Token);
                    }
                    finally
                    {
                        try
                        {
                            HotelM2ResourceGuard.Check(
                                spec,
                                output,
                                value =>
                                    environmentResources.Add(new { stage = "afterCleanup", value })
                            );
                        }
                        finally
                        {
                            try
                            {
                                await Save(
                                    "environment-resources.json",
                                    environmentResources.ToArray()
                                );
                            }
                            finally
                            {
                                await Save(
                                    "cleanup.json",
                                    new
                                    {
                                        cleanup = host.Cleanup.ValueKind == JsonValueKind.Undefined
                                            ? (JsonElement?)null
                                            : host.Cleanup,
                                        exited = host.Process.HasExited,
                                    }
                                );
                            }
                        }
                    }
                },
                Receipt,
                Eligible
            );
            if (!passed)
                throw new InvalidOperationException(
                    "M2 profile failed or is incomplete; campaign must stop."
                );
        }
    }

    private async Task SampleM2DatabaseAsync(CancellationTokenSource timed)
    {
        for (var at = 0; at < 360; at += 10)
        {
            await WaitUntil(at, timed.Token);
            var value = await HotelsM2Docker.OwnedStatsAsync(host, timed.Token);
            databaseResources.Add(
                new
                {
                    plannedAt = at,
                    observedAt = clock.Elapsed.TotalSeconds,
                    value,
                }
            );
        }
    }

    private HotelM2MemoryAssessment AssessM2(HotelM2LoadSchedule plan, int version)
    {
        var legacy = M2Gates(plan);
        var assessment = HotelsM2MemoryAssessment.Create(
            version,
            legacy,
            resources.Select(r => new HotelM2MemoryPoint(
                r.At,
                r.Rss,
                r.Reservations,
                r.RetainedInboxRows,
                r.CacheEntries,
                r.CacheBytes
            ))
        );
        if (version < 3)
            return assessment;
        var frames = loadFrames.OrderBy(f => f.PlannedSecond).ToArray();
        var final =
            frames.LastOrDefault()?.Queue
            ?? throw new InvalidDataException("Final due-work classification missing.");
        var last = resources.OrderBy(r => r.At).Last();
        var gates = new Dictionary<string, bool>(assessment.Gates, StringComparer.Ordinal);
        gates.Remove("dueWorkAndInboxSettled");
        gates[HotelsM2QueueObservation.MandatoryGate] =
            frames.Length == 361
            && frames.Select(f => f.PlannedSecond).SequenceEqual(Enumerable.Range(0, 361))
            && frames.All(f =>
                f.Queue is { } q
                && HotelsM2QueueObservation.Valid(q)
                && q.RawDue == f.RawDue
                && f.PendingInbox >= 0
            )
            && HotelsM2QueueObservation.Settled(final, last.PendingInbox, last.DueWork);
        HotelM2RecoveryDiagnostic? recovery = null;
        if (version >= 4)
        {
            recovery = HotelsM2RecoveryCohort.Evaluate(
                plan.Jobs,
                operations
                    .Select(o => new HotelM2RecoveryFact(
                        o.JobId,
                        o.Kind,
                        o.Due,
                        o.Completed,
                        o.Useful
                    ))
                    .ToArray(),
                (id, end) => m2Evidence!.WindowPurchaseSuccess(id, end),
                legacy["recoveryWithin75Seconds"]
            );
            gates.Remove("recoveryWithin75Seconds");
            gates[HotelsM2RecoveryCohort.Gate] = recovery.RecoveredAt is <= 315;
        }
        return assessment with
        {
            Gates = gates,
            QueueDiagnostic = new(legacy["dueWorkAndInboxSettled"], false, final),
            RecoveryDiagnostic = recovery,
        };
    }

    private Dictionary<string, bool> M2Gates(HotelM2LoadSchedule plan)
    {
        var samples = operations.OrderBy(o => o.Due).ToArray();
        var telemetry = resources.OrderBy(r => r.At).ToArray();
        var normalized = samples
            .Select(s => s.Kind == "create" ? s with { Useful = UsefulForPhase(s) } : s)
            .ToArray();
        var result = Gates(normalized, telemetry, 38, 38);
        result["normalAndSteadyPurchase"] = M2PurchaseGates(samples);
        result["recoveryWithin75Seconds"] = M2RecoveryAt(samples) is <= 315;
        var frames = loadFrames.OrderBy(f => f.PlannedSecond).ToArray();
        var starts = frames
            .SelectMany(f =>
                f.Sources.SelectMany(s =>
                    s.Events.Where(e => e.Stage == "Started").Select(e => (s.Provider, Event: e))
                )
            )
            .ToArray();
        var finals = finalFacts
            .GetProperty("m2Rows")
            .Deserialize<HotelM2FinalRow[]>(HotelsL1Frames.Json)!;
        var after = finalFacts
            .GetProperty("finalSources")
            .Deserialize<HotelWireSnapshot[]>(HotelsL1Frames.Json)!;
        var before = setup
            .GetProperty("initialSources")
            .Deserialize<HotelWireSnapshot[]>(HotelsL1Frames.Json)!;
        result["capacityLanesAndActive"] = frames.All(f =>
            f.Capacity.Length == 2
            && f.Capacity.All(s =>
                s.Capacity.ForegroundStarts <= 24
                && s.Capacity.RecoveryStarts <= 6
                && s.Capacity.ForegroundActive <= 1
                && s.Capacity.RecoveryActive <= 1
                && s.Capacity.MaxActive <= 2
            )
        );
        result["actualHttpStartsWithinPermitWindows"] =
            starts.All(s =>
                s.Event.Capacity is { } c
                && c.Provider == s.Provider
                && (
                    s.Event.Operation == "read"
                        ? c.Capacity.RecoveryActive == 1
                        : c.Capacity.ForegroundActive == 1
                )
            )
            && starts
                .GroupBy(s => (s.Provider, s.Event.Capacity!.Capacity.WindowUntil))
                .All(g =>
                    g.Count(s => s.Event.Operation == "read") <= 6
                    && g.Count(s => s.Event.Operation != "read") <= 24
                );
        var callbacks = plan
            .Jobs.Where(j => j.Kind == "callback" && Phase(j.Due) != "Burst")
            .ToArray();
        result["allElevenFirstIdentityReadsActuallyStarted"] =
            callbacks.Length == 11
            && callbacks.All(j =>
            {
                var row = finals.Single(r => r.Id == j.ReservationId);
                var count = starts.Count(s =>
                    s.Provider == "duffel-stays"
                    && s.Event.JobId == j.Id
                    && s.Event.Operation == "read"
                );
                return count >= 1
                    && count <= row.ReadsClaimed
                    && row.ReadsClaimed <= 3
                    && row.ReadAllowance == 3
                    && row.ReadUntil - row.FirstKnownAt == TimeSpan.FromMinutes(5);
            });
        var creates = plan.Jobs.Where(j => j.Kind == "create").ToArray();
        var admitted = finals
            .Where(r => creates.Any(j => j.ReservationId == r.Id) && r.AttemptId is not null)
            .ToArray();
        var timedCreates =
            after.Sum(s => s.Effects.Values.Sum()) - before.Sum(s => s.Effects.Values.Sum());
        result["admittedBoundedOutcome"] =
            admitted.Length == timedCreates
            && admitted.All(r =>
                r.SenderFinished
                && r.Outcome
                    is ReservationOutcome.Confirmed
                        or ReservationOutcome.NotCreated
                        or ReservationOutcome.Unknown
            )
            && loadOperations
                .Where(o =>
                    admitted.Any(r => r.Id == o.ResourceId) && plan.Jobs[o.JobId].Kind == "create"
                )
                .All(o => o.Completed - o.Started <= 150);
        var lossJob = creates.Single(j => j.Due == 154.5);
        var loss = loadOperations.Single(o => o.JobId == lossJob.Id);
        var lossRow = finals.Single(r => r.Id == lossJob.ReservationId);
        var effectKey = lossRow.AttemptId is { } attempt
            ? HotelWireMeasurements.Correlation(lossJob.Provider!, attempt)
            : "";
        result["oneFastLostReplyWithRetainedEffect"] =
            loss.Terminal == "Response"
            && loss.Outcome == "Unknown"
            && loss.Completed - loss.Started <= 1
            && !lossRow.HasBookingId
            && after
                .Single(s => s.Provider == lossJob.Provider)
                .Effects.GetValueOrDefault(effectKey) == 1;
        result["m2SourceAccounting"] = after.All(s =>
            s.Active == 0
            && !s.Overflow
            && s.UnknownEffects == 0
            && s.Starts - before.Single(b => b.Provider == s.Provider).Starts
                == starts.Count(e => e.Provider == s.Provider)
            && s.Starts == s.Completions
        );
        result["m2PlannedSourceSteps"] = starts.All(s =>
            s.Event.JobId is { } id
            && s.Event.StepOrdinal is { } ordinal
            && plan.Steps.Any(p =>
                p.JobId == id
                && p.Provider == s.Provider
                && p.Ordinal == ordinal
                && p.Operation == s.Event.Operation
                && p.ReadAttempt == s.Event.ReadAttempt
            )
        );
        result["m2SuccessfulSourceSteps"] = loadOperations
            .Where(o => o.Useful || o.PurchaseAccepted)
            .All(o =>
                plan.Steps.Where(p => p.JobId == o.JobId && p.ReadAttempt == -1)
                    .All(p =>
                        starts.Count(s =>
                            s.Provider == p.Provider
                            && s.Event.JobId == p.JobId
                            && s.Event.StepOrdinal == p.Ordinal
                            && s.Event.Operation == p.Operation
                        ) == 1
                    )
            );
        result["m2PerSourcePurchase"] = new[] { (30d, 150d), (300d, 360d) }.All(phase =>
            new[] { "duffel-stays", "hotelbeds" }.All(source =>
            {
                var cohort = creates
                    .Where(j => j.Due >= phase.Item1 && j.Due < phase.Item2 && j.Provider == source)
                    .ToArray();
                return cohort.Length > 0
                    && cohort.Count(j =>
                        m2Evidence!.PhasePurchaseSuccess(j.Id, phase.Item1, phase.Item2)
                    )
                        >= cohort.Length * .95;
            })
        );
        result["m2HbxKnownReferenceReads"] =
            creates.Where(j => j.Provider == "hotelbeds" && j.Due != 154.5).Count() == 8
            && admitted
                .Where(r =>
                    r.Provider == "hotelbeds"
                    && creates.Single(j => j.ReservationId == r.Id).Due != 154.5
                )
                .All(r =>
                    r.HasBookingId
                    && r.ReadsClaimed is >= 1 and <= 3
                    && r.ReadAllowance == 3
                    && starts.Any(s =>
                        s.Provider == "hotelbeds"
                        && s.Event.Operation == "read"
                        && creates.Single(j => j.ReservationId == r.Id).Id == s.Event.JobId
                    )
                );
        result["m2ResourceAccounting"] = databaseResources.Count == 36;
        var timeout = loadOperations.Single(o =>
            plan.Jobs[o.JobId].Kind == "search" && plan.Jobs[o.JobId].Due == 186
        );
        result["m2FaultOutcomes"] =
            loadOperations
                .Single(o => plan.Jobs[o.JobId].Kind == "search" && plan.Jobs[o.JobId].Due == 162)
                .Terminal == "Cancelled"
            && HotelsM2LoadDispatch.ExpectedTimeout(timeout.Outcome, timeout.SourceStatuses);
        return result;
    }
}
