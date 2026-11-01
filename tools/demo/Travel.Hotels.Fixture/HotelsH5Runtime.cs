using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Travel.Host.Tests.Integration.Hotels;
using Travel.Modules.Hotels.Tests.Integration;

namespace Travel.Hotels.Fixture;

internal sealed class HotelsH5Runtime(
    string root,
    HotelH5Packet packet,
    HotelM2LoadRunSpec spec,
    Func<HotelsL1HostProcess, CancellationToken, Task>? beforePrepare = null,
    long? clockOrigin = null
) : IHotelsH5Port
{
    private readonly long startedTimestamp = clockOrigin ?? Stopwatch.GetTimestamp();
    private HotelsL1HostProcess? host;
    private HotelsM2LoadDispatch? dispatch;
    private HotelH5Identity? identity;
    private double began;
    private readonly HotelH5Progress progress = new();
    public int PreflightSamples => progress.PreflightSamples;
    public int PreflightSampleAttempts => progress.PreflightSampleAttempts;
    private long beginTimestamp;
    private readonly string output = HotelM2LoadRunSpec.Resolve(root, spec.Output, true);
    private readonly string manifest = HotelM2LoadRunSpec.Resolve(root, spec.Manifest, false);
    public double Elapsed => Stopwatch.GetElapsedTime(startedTimestamp).TotalSeconds;

    public Task WaitAsync(double due, CancellationToken ct) =>
        HotelsLoadTiming.WaitUntilAsync(due, () => Elapsed, ct);

    public void CheckResources() => HotelM2ResourceGuard.Check(spec, output);

    public async Task SaveAsync(string name, object value)
    {
        if (Path.GetFileName(name) != name)
            throw new InvalidDataException("H5 output filename escaped.");
        using var limit = new CancellationTokenSource(
            TimeSpan.FromSeconds(Math.Clamp(600 - Elapsed, 0.001, 5))
        );
        await using var file = new FileStream(
            Path.Combine(output, name),
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.Read
        );
        await JsonSerializer.SerializeAsync(file, value, HotelM2LoadRunSpec.Json, limit.Token);
        await file.FlushAsync(limit.Token);
        limit.Token.ThrowIfCancellationRequested();
    }

    public async Task<JsonElement> SetupAsync(CancellationToken ct)
    {
        if (!HotelH5Packet.EulaAccepted())
            throw new InvalidDataException("Owner VMMap license acceptance required.");
        packet.VerifyTool(root);
        if (await HotelsM2Docker.ImageIdAsync(spec, ct) != spec.DatabaseImageId)
            throw new InvalidDataException("Pinned local database image changed; no pull.");
        progress.StartupAttempted = true;
        host = await HotelsL1HostProcess.StartAsync(
            root,
            manifest,
            ct,
            diagnostic: true,
            loadSpec: spec,
            profilePath: HotelM2LoadRunSpec.Resolve(root, packet.LoadProfile, false)
        );
        progress.HostAcquired = true;
        identity = ObserveIdentity();
        HotelsH5Map.ValidateIdentity(identity, identity, host.Process.HasExited);
        await SaveAsync(
            "identity.json",
            new
            {
                identity,
                ready = host.Ready,
                driverPid = Environment.ProcessId,
            }
        );
        await SaveAsync(
            "preflight.json",
            await host.PreflightAsync(
                ct,
                () => progress.PreflightSampleAttempts++,
                async raw =>
                {
                    progress.PreflightSamples++;
                    await SaveAsync("preflight-sample.json", raw);
                }
            )
        );
        if (beforePrepare is not null)
            await beforePrepare(host, ct);
        var setup = await host.CommandAsync("prepare", null, ct);
        var stock = setup
            .GetProperty("m2Stock")
            .Deserialize<HotelM2SeedStock[]>(HotelsL1Frames.Json)!
            .ToDictionary(s => s.Id);
        dispatch = new HotelsM2LoadDispatch(
            host.Client,
            new HotelM2LoadSchedule(spec.Profile),
            () => Elapsed - began,
            async (job, token) =>
            {
                var row = stock[job.ReservationId!.Value];
                if (row.Provider != "duffel-stays" || row.QuoteId is null)
                    throw new InvalidDataException("H5 callback stock mismatch.");
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
        return setup;
    }

    public async Task<JsonElement> SampleAsync(CancellationToken ct)
    {
        VerifyIdentity();
        var before = L1ProcessMemory.Capture(host!.Process);
        var raw = await host.CommandAsync("sample", null, ct);
        if (raw.GetProperty("pid").GetInt32() != identity!.Pid)
            throw new InvalidDataException("H5 sample PID changed.");
        // The raw child metrics contain rss=0 by contract; do not report that placeholder as RSS.
        var result = JsonNode.Parse(raw.GetRawText())!.AsObject();
        result["parentProcessBefore"] = JsonSerializer.SerializeToNode(before, HotelsL1Frames.Json);
        result["parentProcessAfter"] = JsonSerializer.SerializeToNode(
            L1ProcessMemory.Capture(host.Process),
            HotelsL1Frames.Json
        );
        return JsonSerializer.SerializeToElement(result, HotelsL1Frames.Json);
    }

    public async Task BeginAsync(double origin, CancellationToken ct)
    {
        began = origin;
        beginTimestamp = startedTimestamp + (long)(origin * Stopwatch.Frequency);
        await host!.CommandAsync(
            "begin",
            new { began = DateTimeOffset.UtcNow, timestamp = beginTimestamp },
            ct
        );
    }

    public Task<HotelM2DispatchResult> DispatchAsync(HotelM2PlannedJob job, CancellationToken ct) =>
        dispatch!.ExecuteAsync(job, ct);

    private HotelH5Identity ObserveIdentity()
    {
        var owned = host!;
        owned.Process.Refresh();
        return new(
            owned.Process.Id,
            owned.Process.StartTime.ToUniversalTime().Ticks,
            Path.GetFullPath(owned.Ready.GetProperty("root").GetString()!),
            HotelsL1HostProcess.Hash(Assembly.GetExecutingAssembly().Location),
            HotelsL1HostProcess.Hash(manifest)
        );
    }

    private void VerifyIdentity()
    {
        HotelsH5Map.ValidateIdentity(identity!, ObserveIdentity(), host!.Process.HasExited);
        if (
            host.Ready.GetProperty("pid").GetInt32() != identity!.Pid
            || host.Ready.GetProperty("assemblyHash").GetString() != identity.BinaryHash
            || host.Ready.GetProperty("manifestHash").GetString() != identity.ManifestHash
        )
            throw new InvalidDataException("H5 ready identity changed.");
    }

    public async Task CaptureAsync(string name, CancellationToken ct)
    {
        if (name is not ("P0" or "P1" or "P2"))
            throw new InvalidDataException("Unexpected map.");
        VerifyIdentity();
        var start = Elapsed;
        var before = L1ProcessMemory.Capture(host!.Process);
        HotelH5Map? map = null;
        string? error = null;
        try
        {
            map = await HotelsH5Collector.CaptureAsync(
                HotelM2LoadRunSpec.Resolve(root, packet.Vmmap, false),
                Path.Combine(output, name + ".xml"),
                identity!.Pid,
                () =>
                {
                    VerifyIdentity();
                    packet.VerifyTool(root);
                    if (!HotelH5Packet.EulaAccepted())
                        throw new InvalidDataException("VMMap license not accepted.");
                },
                CheckResources,
                info => new HotelH5CollectorProcess(
                    Process.Start(info)
                        ?? throw new InvalidOperationException("Owned collector did not start.")
                ),
                ct
            );
        }
        catch (Exception ex)
        {
            error = ex.GetType().Name;
            throw;
        }
        finally
        {
            await SaveAsync(
                name + "-capture.json",
                new
                {
                    name,
                    started = start,
                    ended = Elapsed,
                    before,
                    after = host.Process.HasExited ? null : L1ProcessMemory.Capture(host.Process),
                    map,
                    error,
                }
            );
        }
    }

    public async Task CleanupAsync(CancellationToken ct)
    {
        progress.RequireKnownCleanup();
        if (host is null)
            return;
        try
        {
            host.Completed = false; // This short diagnostic must never pass the 360s acceptance guard.
            await host.StopAsync(ct);
            var assets = host.Ready.GetProperty("assets");
            var container = assets.GetProperty("containerId").GetString()!;
            var temporary = Path.GetFullPath(assets.GetProperty("temporaryDirectory").GetString()!);
            if (
                assets.GetProperty("ownerPid").GetInt32() != identity!.Pid
                || container.Length != 64
                || !container.All(char.IsAsciiHexDigit)
                || !temporary.StartsWith(
                    Path.GetFullPath(Path.GetTempPath()),
                    StringComparison.OrdinalIgnoreCase
                )
                || !Path.GetFileName(temporary)
                    .StartsWith("travel-hotels-host-", StringComparison.Ordinal)
            )
                throw new InvalidDataException("H5 cleanup identity changed.");
            var remaining = await HotelsM2Docker.ReadAsync(
                ["ps", "-a", "-q", "--no-trunc", "--filter", "id=" + container],
                ct
            );
            if (!string.IsNullOrWhiteSpace(remaining) || Directory.Exists(temporary))
                throw new InvalidDataException("Owned H5 assets remain after cleanup.");
            await SaveAsync(
                "cleanup.json",
                new
                {
                    host.Cleanup,
                    exitCode = host.Process.ExitCode,
                    expectedExitCode = 1,
                    containerMissing = true,
                    temporaryDirectoryMissing = true,
                }
            );
        }
        finally
        {
            host.ReleaseOwnedHandles();
        }
    }

    internal static async Task RunAsync(
        string root,
        string path,
        bool execute,
        CancellationToken ct
    )
    {
        var (packet, spec) = HotelH5Packet.Read(root, path);
        var output = HotelM2LoadRunSpec.Resolve(root, spec.Output, true);
        if (!execute)
        {
            var resources = HotelM2ResourceGuard.Check(spec, output);
            Console.WriteLine(
                JsonSerializer.Serialize(
                    new
                    {
                        sourceReady = true,
                        executed = false,
                        executionReady = HotelH5Packet.EulaAccepted(),
                        vmmapEulaAccepted = HotelH5Packet.EulaAccepted(),
                        workload = 56,
                        publicRequests = 57,
                        ipcSamples = 35,
                        maps = 3,
                        resources,
                    },
                    HotelM2LoadRunSpec.Json
                )
            );
            return;
        }
        // Atomic invocation marker precedes every runtime effect; even a failed prerequisite consumes it.
        var markerPath = Path.Combine(Path.GetDirectoryName(output)!, "attempt-1.json");
        await using var marker = new FileStream(
            markerPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.Read
        );
        await JsonSerializer.SerializeAsync(
            marker,
            new { state = "started", passed = false },
            HotelM2LoadRunSpec.Json,
            CancellationToken.None
        );
        await marker.FlushAsync();
        HotelH5Receipt? receipt = null;
        HotelsH5Runtime? port = null;
        string? error = null;
        var total = Stopwatch.StartNew();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(600));
        try
        {
            deadline.Token.ThrowIfCancellationRequested();
            if (Directory.Exists(output))
                throw new InvalidDataException("Retain existing H5 output.");
            if (!HotelH5Packet.EulaAccepted())
                throw new InvalidDataException("Owner VMMap license acceptance required.");
            Directory.CreateDirectory(output);
            port = new HotelsH5Runtime(root, packet, spec);
            receipt = await HotelsH5Controller.RunAsync(port, spec.Profile, deadline.Token);
        }
        catch (Exception ex)
        {
            error = ex.GetType().Name;
        }
        bool Eligible() =>
            error is null
            && receipt is { AcquisitionComplete: true, CleanupComplete: true }
            && !deadline.IsCancellationRequested
            && total.Elapsed.TotalSeconds <= 600;
        var accepted = await HotelsH5Finalization.FinishAsync(
            async () =>
            {
                if (port is not null)
                    await port.SaveAsync("receipt.json", receipt ?? (object)new { error });
            },
            () =>
            {
                spec.VerifyBuild(root);
                HotelsSourceManifest.VerifyFile(
                    root,
                    HotelM2LoadRunSpec.Resolve(root, spec.Manifest, false),
                    spec.Inputs
                );
                packet.VerifyTool(root);
                return Task.CompletedTask;
            },
            async () =>
            {
                if (port is null)
                    return;
                var hashes = new Dictionary<string, string>();
                using var readLimit = new CancellationTokenSource(
                    TimeSpan.FromSeconds(Math.Clamp(600 - total.Elapsed.TotalSeconds, 0.001, 5))
                );
                // Independent of identity/receipt verification: hash whatever evidence was actually saved.
                foreach (var file in Directory.GetFiles(output).Order())
                {
                    using var input = File.OpenRead(file);
                    hashes.Add(
                        Path.GetFileName(file)!,
                        Convert.ToHexString(await SHA256.HashDataAsync(input, readLimit.Token))
                    );
                }
                await port.SaveAsync("raw-integrity.json", hashes);
            },
            async (complete, errors) =>
            {
                if (complete && !Eligible())
                    complete = false;
                using var writeLimit = complete
                    ? CancellationTokenSource.CreateLinkedTokenSource(deadline.Token)
                    : new CancellationTokenSource();
                writeLimit.CancelAfter(TimeSpan.FromSeconds(5));
                // Remove any earlier success before writing the final/failure state.
                marker.Position = 0;
                marker.SetLength(0);
                await JsonSerializer.SerializeAsync(
                    marker,
                    new
                    {
                        complete,
                        diagnosticOnly = true,
                        m2Accepted = false,
                        receipt,
                        error,
                        finalizationErrors = errors,
                        cancelled = ct.IsCancellationRequested,
                        elapsedSeconds = total.Elapsed.TotalSeconds,
                    },
                    HotelM2LoadRunSpec.Json,
                    writeLimit.Token
                );
                await marker.FlushAsync(writeLimit.Token);
                marker.Flush(flushToDisk: true);
                // FinishAsync rechecks Eligible after this durable write and corrects late success.
            },
            Eligible
        );
        if (!accepted)
            throw new InvalidOperationException("H5 stopped; retain attempt and raw evidence.");
    }
}
