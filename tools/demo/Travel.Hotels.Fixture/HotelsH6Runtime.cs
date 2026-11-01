using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Travel.Modules.Hotels.Tests.Integration;

namespace Travel.Hotels.Fixture;

internal sealed class HotelsH6Port(
    HotelsH5Runtime inner,
    HotelsH6Client helper,
    HotelM2LoadRunSpec spec,
    string output
) : IHotelsH5Port
{
    public double Elapsed => inner.Elapsed;
    public int PreflightSamples => inner.PreflightSamples;
    public int PreflightSampleAttempts => inner.PreflightSampleAttempts;

    public Task WaitAsync(double due, CancellationToken ct) => inner.WaitAsync(due, ct);

    public Task<JsonElement> SetupAsync(CancellationToken ct) => inner.SetupAsync(ct);

    public Task<JsonElement> SampleAsync(CancellationToken ct) => inner.SampleAsync(ct);

    public Task CaptureAsync(string name, CancellationToken ct) => inner.CaptureAsync(name, ct);

    public Task BeginAsync(double origin, CancellationToken ct) => inner.BeginAsync(origin, ct);

    public Task<HotelM2DispatchResult> DispatchAsync(HotelM2PlannedJob job, CancellationToken ct) =>
        inner.DispatchAsync(job, ct);

    public void CheckResources() => helper.CheckResources(spec, output);

    public Task SaveAsync(string name, object value) => inner.SaveAsync(name, value);

    public Task CleanupAsync(CancellationToken ct) =>
        HotelsH6Cleanup.RunAsync(
            token => helper.FinishAsync(output, CheckResources, token),
            inner.CleanupAsync,
            ct
        );
}

internal static class HotelsH6Runtime
{
    internal static async Task RunAsync(
        string root,
        string path,
        bool execute,
        CancellationToken ct
    )
    {
        if (HotelsH6Windows.IsAdministrator())
            throw new InvalidDataException("Use an ordinary H6 driver; only helper elevates.");
        var (packet, h5, spec) = HotelH6Packet.Read(root, path);
        var output = HotelM2LoadRunSpec.Resolve(root, spec.Output, true);
        var resources = HotelM2ResourceGuard.Check(spec, output);
        if (!execute)
        {
            Console.WriteLine(
                JsonSerializer.Serialize(
                    new
                    {
                        sourceReady = true,
                        executed = false,
                        runtimeAuthorized = false,
                        runtimeQualified = false,
                        analysisQualified = false,
                        vmmapEulaAccepted = HotelH5Packet.EulaAccepted(),
                        workload = 56,
                        publicRequests = 57,
                        ipcSamples = 36,
                        maps = 3,
                        heapSnapshots = 2,
                        etlFiles = 1,
                        packet.WpaVersion,
                        resources,
                    },
                    HotelM2LoadRunSpec.Json
                )
            );
            return;
        }
        var markerPath = Path.Combine(Path.GetDirectoryName(output)!, "attempt-1.json");
        await using var marker = new FileStream(
            markerPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.Read
        );
        await JsonSerializer.SerializeAsync(
            marker,
            new { state = "started", complete = false },
            HotelM2LoadRunSpec.Json,
            ct
        );
        await marker.FlushAsync(ct);
        marker.Flush(true);
        var clock = Stopwatch.GetTimestamp();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(600));
        HotelsH6Client? helper = null;
        HotelsH5Runtime? inner = null;
        HotelH5Receipt? receipt = null;
        string? error = null;
        try
        {
            if (Directory.Exists(output) || !HotelH5Packet.EulaAccepted())
                throw new InvalidDataException("H6 output exists or VMMap license not accepted.");
            Directory.CreateDirectory(output);
            helper = HotelsH6Client.Create();
            await File.WriteAllTextAsync(
                Path.Combine(output, "helper-intent.json"),
                JsonSerializer.Serialize(
                    new
                    {
                        helper.Nonce,
                        helper.TraceRoot,
                        driverPid = Environment.ProcessId,
                        tracingTarget = "fresh-owned-Host-only",
                        namedInstance = "TravelHotelsH6-" + helper.Nonce,
                    },
                    HotelM2LoadRunSpec.Json
                ),
                deadline.Token
            );
            await helper.LaunchAsync(path, deadline.Token); // Mandatory readiness before Host creation.
            inner = new(root, h5, spec, helper.AttachAsync, clock);
            var port = new HotelsH6Port(inner, helper, spec, output);
            receipt = await HotelsH5Controller.RunAsync(port, spec.Profile, deadline.Token, helper);
        }
        catch (Exception ex)
        {
            error = ex.GetType().Name;
        }
        finally
        {
            helper?.Dispose();
        }

        double Elapsed() => Stopwatch.GetElapsedTime(clock).TotalSeconds;
        bool Eligible() =>
            error is null
            && receipt is { AcquisitionComplete: true, CleanupComplete: true }
            && helper?.Receipt
                is {
                    TrackingClosed: true,
                    SessionClosed: true,
                    Snapshots: 2,
                    EtlSaved: true,
                    Errors.Length: 0
                }
            && !deadline.IsCancellationRequested
            && Elapsed() <= 600;
        async Task Save(string name, object value)
        {
            using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await using var file = new FileStream(
                Path.Combine(output, name),
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.Read
            );
            await JsonSerializer.SerializeAsync(file, value, HotelM2LoadRunSpec.Json, limit.Token);
        }
        var passed = await HotelsH5Finalization.FinishAsync(
            () =>
                Directory.Exists(output)
                    ? Save(
                        "receipt.json",
                        new
                        {
                            receipt,
                            error,
                            trace = helper?.Receipt,
                            helperPid = helper?.HelperPid,
                            retainedTraceRoot = helper?.TraceRoot,
                            analysisComplete = false,
                            etlQualification = "pending-owned-PID-two-snapshots-loss-and-stack-coverage",
                            comparison = receipt?.H6Comparison,
                        }
                    )
                    : Task.CompletedTask,
            () =>
            {
                spec.VerifyBuild(root);
                HotelsSourceManifest.VerifyFile(
                    root,
                    HotelM2LoadRunSpec.Resolve(root, spec.Manifest, false),
                    spec.Inputs
                );
                packet.VerifyTools();
                return Task.CompletedTask;
            },
            async () =>
            {
                if (!Directory.Exists(output))
                    return;
                var hashes = new Dictionary<string, string>();
                using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                foreach (var file in HotelsH6Store.Files(output))
                {
                    await using var source = File.OpenRead(file);
                    hashes.Add(
                        Path.GetRelativePath(output, file).Replace('\\', '/'),
                        Convert.ToHexString(await SHA256.HashDataAsync(source, limit.Token))
                    );
                }
                await Save("raw-integrity.json", hashes);
            },
            async (complete, failures) =>
            {
                using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                marker.Position = 0;
                marker.SetLength(0);
                await JsonSerializer.SerializeAsync(
                    marker,
                    new
                    {
                        complete = complete && Eligible(),
                        acquisitionComplete = complete && Eligible(),
                        analysisComplete = false,
                        diagnosticOnly = true,
                        m2Accepted = false,
                        receipt,
                        trace = helper?.Receipt,
                        error,
                        finalizationErrors = failures,
                        elapsedSeconds = Elapsed(),
                        helperPid = helper?.HelperPid,
                        retainedTraceRoot = helper?.TraceRoot,
                    },
                    HotelM2LoadRunSpec.Json,
                    limit.Token
                );
                await marker.FlushAsync(limit.Token);
                marker.Flush(true);
            },
            Eligible
        );
        if (!passed)
            throw new InvalidOperationException(
                "H6 stopped; preserve attempt/raw evidence. No automatic retry."
            );
    }
}
