using System.Collections.Concurrent;
using System.Text.Json;
using Travel.Modules.Hotels.Tests.Integration;

namespace Travel.Hotels.Fixture;

internal interface IHotelsH5Port
{
    double Elapsed { get; }
    int PreflightSamples { get; }
    int PreflightSampleAttempts { get; }
    Task WaitAsync(double due, CancellationToken ct);
    Task<JsonElement> SetupAsync(CancellationToken ct);
    Task<JsonElement> SampleAsync(CancellationToken ct);
    Task CaptureAsync(string name, CancellationToken ct);
    Task BeginAsync(double origin, CancellationToken ct);
    Task<HotelM2DispatchResult> DispatchAsync(HotelM2PlannedJob job, CancellationToken ct);
    void CheckResources();
    Task SaveAsync(string name, object value);
    Task CleanupAsync(CancellationToken ct);
}

internal sealed record HotelH5Receipt(
    bool AcquisitionComplete,
    bool CleanupComplete,
    int IpcSamples,
    string[] Maps,
    HotelLoadLedgerSnapshot Ledger,
    HotelM2ExecutionFailures? ExecutionFailures,
    string? Failure,
    string? CleanupFailure
)
{
    public int IpcSampleAttempts { get; init; }
    public HotelH6Comparison? H6Comparison { get; init; }
}

// One fixed diagnostic protocol. All HTTP work uses the existing dispatcher/execution/ledger.
internal static class HotelsH5Controller
{
    internal static HotelM2PlannedJob[] Prefix(HotelM2LoadProfile profile)
    {
        if (profile.Code != "S")
            throw new InvalidDataException("H5 requires the S dataset.");
        var jobs = new HotelM2LoadSchedule(profile)
            .Jobs.Where(j => j.Due >= 0 && j.Due < 10)
            .ToArray();
        if (
            jobs.Length != 56
            || jobs.Any(j => j.Prerequisite is int p && !jobs.Any(k => k.Id == p))
        )
            throw new InvalidDataException("H5 prefix changed.");
        return jobs;
    }

    internal static async Task<HotelH5Receipt> RunAsync(
        IHotelsH5Port port,
        HotelM2LoadProfile profile,
        CancellationToken ct,
        IHotelsH6Hooks? h6 = null
    )
    {
        var jobs = Prefix(profile);
        var ledger = new HotelsLoadLedger(
            jobs.Select(j => new HotelLoadJob(j.Id, j.Kind, j.Due, j.Provider))
        );
        var maps = new List<string>(3);
        var results = new ConcurrentDictionary<int, HotelM2DispatchResult>();
        using var total = CancellationTokenSource.CreateLinkedTokenSource(ct);
        // Reserve the final 60 seconds for cleanup within the total 600 second budget.
        total.CancelAfter(
            TimeSpan.FromSeconds(Math.Max(0.001, h6 is null ? 540 : 480 - port.Elapsed))
        );
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(total.Token);
        HotelsM2Execution? execution = null;
        Task[] requests = [];
        Task? sampling = null;
        var origin = 0d;
        var begun = false;
        var samples = 0;
        var sampleAttempts = 0;
        HotelH6Comparison? comparison = null;
        var complete = false;
        var cleaned = false;
        string? failure = null,
            cleanupFailure = null;
        string stage = "setup";
        double WarmupTime() => begun ? port.Elapsed - origin : 0;
        async Task<JsonElement> Sample(string phase, int index, double due)
        {
            await port.WaitAsync(due, stop.Token);
            port.CheckResources();
            var started = port.Elapsed;
            sampleAttempts++;
            var raw = await port.SampleAsync(stop.Token);
            samples++;
            await port.SaveAsync(
                phase + "-" + index.ToString("D2") + ".json",
                new
                {
                    phase,
                    plannedAt = due,
                    started,
                    completed = port.Elapsed,
                    raw,
                }
            );
            if (started > due + 1 || port.Elapsed > due + 1)
                throw new TimeoutException("H5 sample missed its one-second window.");
            return raw;
        }
        async Task Map(string name)
        {
            stage = name;
            if (h6 is not null && name != "P0")
            {
                using var snapshot = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
                snapshot.CancelAfter(TimeSpan.FromSeconds(30));
                await h6.BeforeMapAsync(name, snapshot.Token);
                snapshot.Token.ThrowIfCancellationRequested();
            }
            port.CheckResources();
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
            limit.CancelAfter(TimeSpan.FromSeconds(30));
            await port.CaptureAsync(name, limit.Token);
            limit.Token.ThrowIfCancellationRequested();
            port.CheckResources();
            maps.Add(name);
        }
        try
        {
            port.CheckResources();
            await port.SaveAsync("planned-jobs.json", jobs);
            JsonElement setup;
            using (var limit = CancellationTokenSource.CreateLinkedTokenSource(stop.Token))
            {
                limit.CancelAfter(TimeSpan.FromSeconds(120));
                setup = await port.SetupAsync(limit.Token);
            }
            // Preflight counters are recorded at the actual IPC boundary in the port.
            var baseline = Sources(setup.GetProperty("initialSources"));
            await port.SaveAsync("setup.json", setup);
            await Map("P0");
            stage = "control";
            var control = port.Elapsed;
            for (var i = 0; i < 20; i++)
                CheckControl(baseline, await Sample("control", i, control + i));
            await port.WaitAsync(control + 20, stop.Token);
            await Map("P1");
            stage = "boundary";
            CheckControl(baseline, await Sample("boundary", 0, port.Elapsed));
            stage = "begin";
            origin = port.Elapsed;
            begun = true;
            await port.BeginAsync(origin, stop.Token);
            execution = new HotelsM2Execution(
                ledger,
                stop,
                WarmupTime,
                (due, token) => port.WaitAsync(origin + due, token),
                port.DispatchAsync,
                (job, result) =>
                {
                    results[job.Id] = result;
                    if (!Expected(job, result))
                        throw new InvalidDataException("Unexpected H5 job outcome.");
                }
            );
            stage = "warmup";
            requests = jobs.Select(j => execution.RunJobAsync(j, stop.Token)).ToArray();
            JsonElement last = default;
            sampling = execution.ObserveAsync(
                async () =>
                {
                    for (var i = 0; i <= 12; i++)
                        last = await Sample("warmup", i, origin + i);
                },
                "h5-sampling"
            );
            // Sampling is the fixed t=12 boundary. Never await unfinished jobs past it.
            await execution.WaitAsync([], sampling, Task.CompletedTask);
            if (
                requests.Any(t => !t.IsCompletedSuccessfully)
                || results.Count != 56
                || !ledger.Snapshot().AllObserved
                || jobs.Any(j => !Expected(j, results[j.Id]))
            )
                throw new InvalidDataException(
                    "H5 warmup is incomplete or has unexpected outcomes."
                );
            CheckQuiescent(last);
            ledger.Seal(WarmupTime());
            await Map("P2");
            stop.Token.ThrowIfCancellationRequested();
            if (h6 is not null)
            {
                comparison = h6.Compare(last, await Sample("post-capture", 0, port.Elapsed));
                await port.SaveAsync("post-capture-comparison.json", comparison);
            }
            complete = samples + port.PreflightSamples == (h6 is null ? 35 : 36) && maps.Count == 3;
        }
        catch (Exception ex)
        {
            failure = stage + ":" + ex.GetType().Name;
        }
        finally
        {
            // Close evidence before cancellation: late continuations cannot rewrite the denominator.
            execution?.Seal();
            ledger.Seal(WarmupTime());
            try
            {
                await stop.CancelAsync();
                if (requests.Length > 0 || sampling is not null)
                    await Task.WhenAll(requests.Append(sampling ?? Task.CompletedTask))
                        .WaitAsync(TimeSpan.FromSeconds(2));
            }
            catch (Exception ex)
            {
                if (ex is not OperationCanceledException)
                {
                    complete = false;
                    failure ??= "drain:" + ex.GetType().Name;
                }
            }
            try
            {
                await port.SaveAsync(
                    "operations.json",
                    results.Values.OrderBy(r => r.JobId).ToArray()
                );
            }
            catch (Exception ex)
            {
                complete = false;
                failure ??= "operations-evidence:" + ex.GetType().Name;
            }
            try
            {
                using var cleanup = new CancellationTokenSource(
                    TimeSpan.FromSeconds(h6 is null ? 58 : 118)
                );
                await port.CleanupAsync(cleanup.Token);
                cleanup.Token.ThrowIfCancellationRequested();
                cleaned = true;
            }
            catch (Exception ex)
            {
                cleanupFailure = ex.GetType().Name;
            }
        }
        return new(
            complete && !total.IsCancellationRequested,
            cleaned,
            samples + port.PreflightSamples,
            maps.ToArray(),
            ledger.Snapshot(),
            execution?.Snapshot(),
            failure,
            cleanupFailure
        )
        {
            IpcSampleAttempts = sampleAttempts + port.PreflightSampleAttempts,
            H6Comparison = comparison,
        };
    }

    private static bool Expected(HotelM2PlannedJob job, HotelM2DispatchResult r) =>
        r.JobId == job.Id
        && r.Terminal == "Response"
        && r.Status is >= 200 and < 300
        && r.DriverFailure is null
        && r.Completed <= 12
        && (
            job.Kind == "create"
                ? r.PurchaseAccepted
                    && job.ReservationId is not null
                    && r.ResourceId == job.ReservationId
                    && r.Refusal is null
                    // H5 observes a source-bound admitted purchase; it does not require all
                    // reservation details in the Create response or waive S/L confirmation gates.
                    && (
                        r.Outcome == "Confirmed"
                        || job.Provider == "hotelbeds" && r.Outcome == "ReservedAwaitingDetails"
                    )
                    && r.ResponseProvider == job.Provider
                : r.Useful
        );

    private static Dictionary<string, JsonElement> Sources(JsonElement array)
    {
        var values = array
            .EnumerateArray()
            .ToDictionary(s => s.GetProperty("provider").GetString()!);
        if (
            values.Count != 2
            || !values.ContainsKey("duffel-stays")
            || !values.ContainsKey("hotelbeds")
        )
            throw new InvalidDataException("H5 requires both owned source counters.");
        return values;
    }

    internal static void CheckControl(Dictionary<string, JsonElement> baseline, JsonElement sample)
    {
        var current = Sources(sample.GetProperty("sourceMeasurements"));
        foreach (var (key, initial) in baseline)
        {
            var value = current[key];
            foreach (var name in new[] { "starts", "completions", "unknownEffects" })
                if (value.GetProperty(name).GetInt32() != initial.GetProperty(name).GetInt32())
                    throw new InvalidDataException("Background source work observed.");
            var oldEffects = initial
                .GetProperty("effects")
                .EnumerateObject()
                .ToDictionary(p => p.Name, p => p.Value.GetInt32());
            var effects = value
                .GetProperty("effects")
                .EnumerateObject()
                .ToDictionary(p => p.Name, p => p.Value.GetInt32());
            if (
                effects.Count != oldEffects.Count
                || oldEffects.Any(p => !effects.TryGetValue(p.Key, out var n) || n != p.Value)
            )
                throw new InvalidDataException("Background source effects observed.");
        }
        CheckSourcesIdle(sample);
        var capacity = Sources(sample.GetProperty("sourceCapacity"));
        if (
            capacity.Values.Any(v =>
                v.GetProperty("capacity").GetProperty("totalStarts").GetInt32() != 0
            )
        )
            throw new InvalidDataException("Control consumed runtime admission.");
        if (sample.GetProperty("metrics").GetProperty("at").GetDouble() != 0)
            throw new InvalidDataException("Control unexpectedly entered timed phase.");
    }

    private static void CheckSourcesIdle(JsonElement sample)
    {
        if (
            Sources(sample.GetProperty("sourceMeasurements"))
                .Values.Any(v =>
                    v.GetProperty("active").GetInt32() != 0
                    || v.GetProperty("overflow").GetBoolean()
                )
        )
            throw new InvalidDataException("Source is active or overflowed.");
        if (
            Sources(sample.GetProperty("sourceCapacity"))
                .Values.Any(v =>
                {
                    var c = v.GetProperty("capacity");
                    return c.GetProperty("foregroundActive").GetInt32() != 0
                        || c.GetProperty("recoveryActive").GetInt32() != 0;
                })
        )
            throw new InvalidDataException("Source capacity remains active.");
    }

    private static void CheckQuiescent(JsonElement sample)
    {
        CheckSourcesIdle(sample);
        var metrics = sample.GetProperty("metrics");
        if (
            metrics.GetProperty("pendingInbox").GetInt32() != 0
            || metrics.GetProperty("dueWork").GetInt32() != 0
        )
            throw new InvalidDataException("H5 t=12 is not quiescent; omit P2.");
        // futureWork is deliberately preserved, not required to be zero.
    }
}
