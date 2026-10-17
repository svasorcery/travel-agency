using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Runtime;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Travel.Host.Configuration;
using Travel.Host.Tests.Integration.Hotels;
using Travel.Modules.Hotels.Api.Middleware;
using Travel.Modules.Hotels.Application;
using Travel.Modules.Hotels.Core;
using Travel.Modules.Hotels.Infrastructure.Persistence;
using Travel.Modules.Hotels.Infrastructure.Providers.Duffel;
using Wolverine;
using Wolverine.Runtime;

namespace Travel.Hotels.Fixture;

// Private CLI-test transport. There is no product endpoint, credential payload or command registry.
internal static class HotelsL1Frames
{
    internal const int MaximumBytes = 65536;
    internal const int MaximumQueuedFrames = 64;
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    internal static async Task WriteAsync(Stream stream, object message, CancellationToken ct)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(message, Json);
        if (bytes.Length is < 1 or > MaximumBytes)
            throw new InvalidDataException("CLI frame exceeds its fixed bound.");
        var prefix = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(prefix, bytes.Length);
        await stream.WriteAsync(prefix, ct);
        await stream.WriteAsync(bytes, ct);
        await stream.FlushAsync(ct);
    }

    internal static async Task<JsonElement> ReadAsync(Stream stream, CancellationToken ct)
    {
        var prefix = new byte[4];
        await stream.ReadExactlyAsync(prefix, ct);
        var count = BinaryPrimitives.ReadInt32LittleEndian(prefix);
        if (count is < 1 or > MaximumBytes)
            throw new InvalidDataException("CLI frame exceeds its fixed bound.");
        var bytes = new byte[count];
        await stream.ReadExactlyAsync(bytes, ct);
        using var document = JsonDocument.Parse(bytes);
        return document.RootElement.Clone();
    }
}

internal sealed record L1Stock(Guid Id, string QuoteId, string BookingId);

internal sealed record L1Start(double At, string Operation, HotelCapacitySnapshot Capacity);

internal sealed record L1Read(DateTimeOffset At, string BookingId, int Status, double Milliseconds);

internal sealed record L1ReservationFact(
    Guid Id,
    Guid? AttemptId,
    bool SenderFinished,
    ReservationOutcome Outcome,
    int ReadsClaimed,
    int ReadAllowance,
    DateTimeOffset? FirstKnownAt,
    DateTimeOffset? ReadUntil
);

internal sealed record L1GenerationMemory(
    int Index,
    long SizeBeforeBytes,
    long FragmentationBeforeBytes,
    long SizeAfterBytes,
    long FragmentationAfterBytes
);

internal sealed record L1GcMemory(
    long Index,
    int Generation,
    bool Compacted,
    bool Concurrent,
    long HeapSizeBytesAtLastGc,
    long FragmentedBytesAtLastGc,
    long CommittedBytesAtLastGc,
    long PromotedBytesAtLastGc,
    L1GenerationMemory[] Generations
);

internal sealed record L1BclMemory(
    long EstimatedAllocatedHeapBytes,
    long ApproximateTotalAllocatedBytes,
    int Gen0Collections,
    int Gen1Collections,
    int Gen2Collections,
    L1GcMemory LastGc,
    int ThreadPoolThreads,
    long PendingThreadPoolItems,
    long ActiveTimers,
    long JitCompiledMethods,
    long JitCompiledIlBytes,
    int LoadedAssemblies
)
{
    internal static L1BclMemory Capture()
    {
        var info = GC.GetGCMemoryInfo();
        var generations = new L1GenerationMemory[info.GenerationInfo.Length];
        for (var index = 0; index < generations.Length; index++)
        {
            var generation = info.GenerationInfo[index];
            generations[index] = new(
                index,
                generation.SizeBeforeBytes,
                generation.FragmentationBeforeBytes,
                generation.SizeAfterBytes,
                generation.FragmentationAfterBytes
            );
        }
        return new(
            GC.GetTotalMemory(false),
            GC.GetTotalAllocatedBytes(false),
            GC.CollectionCount(0),
            GC.CollectionCount(1),
            GC.CollectionCount(2),
            new(
                info.Index,
                info.Generation,
                info.Compacted,
                info.Concurrent,
                info.HeapSizeBytes,
                info.FragmentedBytes,
                info.TotalCommittedBytes,
                info.PromotedBytes,
                generations
            ),
            ThreadPool.ThreadCount,
            ThreadPool.PendingWorkItemCount,
            System.Threading.Timer.ActiveCount,
            JitInfo.GetCompiledMethodCount(false),
            JitInfo.GetCompiledILBytes(false),
            AppDomain.CurrentDomain.GetAssemblies().Length
        );
    }
}

internal sealed record L1ProcessMemory(
    int Pid,
    long WorkingSetBytes,
    long PrivateBytes,
    int OsThreads,
    int Handles,
    double CpuMilliseconds
)
{
    internal static L1ProcessMemory Capture(Process process)
    {
        process.Refresh();
        return new(
            process.Id,
            process.WorkingSet64,
            process.PrivateMemorySize64,
            process.Threads.Count,
            process.HandleCount,
            process.TotalProcessorTime.TotalMilliseconds
        );
    }
}

internal sealed record L1StageMemory(
    string Stage,
    L1BclMemory Memory,
    L1ProcessMemory Process,
    HotelsFakeRetentionSnapshot Fake
);

internal sealed record L1EnvelopeMemory(
    long DueBodyBytes,
    long FutureBodyBytes,
    long IncomingBodyBytes,
    long OutgoingBodyBytes,
    int MissingRawBodies
);

internal sealed record L1SampleMemory(
    L1BclMemory Memory,
    HotelsFakeRetentionSnapshot Fake,
    L1EnvelopeMemory Envelopes,
    double ObserverUntilMemorySnapshotWallMilliseconds
);

internal sealed record L1ParentMemorySample(double At, L1ProcessMemory Host, L1SampleMemory Child);

// The parent owns all histories and JSON checkpoints. The child owns only the real fixture and
// bounded current telemetry. Neither side can send an arbitrary product command through this pipe.
internal sealed class HotelsL1HostProcess : IAsyncDisposable
{
    private readonly Process process;
    private readonly SemaphoreSlim commands = new(1);
    private readonly Stream input;
    private readonly Stream output;
    private bool stopped;
    private bool stopRequested;
    internal HttpClient Client { get; } = new(new HttpClientHandler { AllowAutoRedirect = false });
    internal JsonElement Ready { get; private set; }
    internal Process Process => process;
    internal int SupplierStarts { get; private set; }
    internal int SupplierCreates { get; private set; }
    internal bool Completed { get; set; }
    internal bool Diagnostic { get; }
    internal JsonElement Cleanup { get; private set; }

    private HotelsL1HostProcess(Process process, bool diagnostic)
    {
        Diagnostic = diagnostic;
        this.process = process;
        input = process.StandardInput.BaseStream;
        output = process.StandardOutput.BaseStream;
    }

    internal static async Task<HotelsL1HostProcess> StartAsync(
        string root,
        string manifestPath,
        CancellationToken ct,
        bool diagnostic = false
    )
    {
        var assembly = Assembly.GetExecutingAssembly().Location;
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = root,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in new[] { assembly, "l1-host", manifestPath })
            start.ArgumentList.Add(argument);
        start.Environment["HOTELS_L1_PARENT_PID"] = Environment.ProcessId.ToString();
        start.Environment["HOTELS_L1_DIAGNOSTIC"] = diagnostic ? "1" : "0";
        HotelsL1Runner.VerifyManifestFile(root, manifestPath);
        var child = new HotelsL1HostProcess(
            Process.Start(start)
                ?? throw new InvalidOperationException("Owned Host child did not start."),
            diagnostic
        );
        try
        {
            // Drain stderr without accumulating history in the child or exposing raw exceptions.
            child.stderr = child.process.StandardError.ReadToEndAsync();
            var message = await HotelsL1Frames.ReadAsync(child.output, ct);
            if (message.GetProperty("kind").GetString() != "ready")
                throw new InvalidDataException("Owned Host child did not provide identity.");
            child.Ready = message.GetProperty("data").Clone();
            var ready = child.Ready;
            var uri = new Uri(ready.GetProperty("client").GetString()!);
            var listeners = ready
                .GetProperty("listeners")
                .EnumerateArray()
                .Select(value => new Uri(value.GetString()!))
                .ToArray();
            if (
                ready.GetProperty("pid").GetInt32() != child.process.Id
                || ready.GetProperty("parentPid").GetInt32() != Environment.ProcessId
                || !string.Equals(
                    Path.GetFullPath(ready.GetProperty("root").GetString()!),
                    Path.GetFullPath(root),
                    StringComparison.OrdinalIgnoreCase
                )
                || ready.GetProperty("assemblyHash").GetString() != Hash(assembly)
                || ready.GetProperty("manifestHash").GetString() != Hash(manifestPath)
                || ready.GetProperty("diagnostic").GetBoolean() != diagnostic
                || ready.GetProperty("assets").GetProperty("ownerPid").GetInt32()
                    != child.process.Id
                || !ready.GetProperty("isolated").GetBoolean()
                || ready.GetProperty("affinity").GetString() != "0x3"
                || !OwnedAddress(uri)
                || listeners.Length == 0
                || listeners.Any(a => !OwnedAddress(a))
            )
                throw new InvalidOperationException(
                    "Owned Host process identity mismatch before sockets."
                );
            child.Client.BaseAddress = uri;
            return child;
        }
        catch
        {
            await child.DisposeAsync();
            throw;
        }
    }

    private Task<string> stderr = Task.FromResult("");

    internal static string Hash(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static bool OwnedAddress(Uri uri) =>
        uri.IsAbsoluteUri
        && uri.IsLoopback
        && uri.Scheme == Uri.UriSchemeHttp
        && uri.Port == 5111
        && uri.UserInfo.Length == 0
        && uri.Query.Length == 0
        && uri.Fragment.Length == 0
        && uri.AbsolutePath == "/";

    internal async Task<JsonElement> CommandAsync(
        string command,
        object? payload,
        CancellationToken ct
    )
    {
        await commands.WaitAsync(ct);
        try
        {
            await HotelsL1Frames.WriteAsync(input, new { command, payload }, ct);
            var reply = await HotelsL1Frames.ReadAsync(output, ct);
            if (reply.GetProperty("kind").GetString() != command)
                throw new InvalidDataException(
                    "Owned Host command failed or response is out of order."
                );
            var data = reply.GetProperty("data");
            if (data.TryGetProperty("supplierStarts", out var starts))
                SupplierStarts = starts.GetInt32();
            if (data.TryGetProperty("supplierCreates", out var creates))
                SupplierCreates = creates.GetInt32();
            return data.Clone();
        }
        finally
        {
            commands.Release();
        }
    }

    internal async Task<object> PreflightAsync(CancellationToken ct)
    {
        // StartAsync verified PID, root, source/BIN identity and owned listener before any socket.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/hotels/reservations");
        request.Headers.Authorization = new("Bearer", "fictional-hotels-owner");
        using var response = await Client.SendAsync(request, deadline.Token);
        using var content = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(deadline.Token)
        );
        var state = await CommandAsync("sample", null, deadline.Token);
        if (
            response.StatusCode != HttpStatusCode.OK
            || response.Headers.CacheControl?.NoStore != true
            || content.RootElement.ValueKind != JsonValueKind.Array
            || content.RootElement.GetArrayLength() != 0
            || SupplierStarts != 0
            || SupplierCreates != 0
        )
            throw new InvalidOperationException("Owned child readonly preflight failed.");
        return new
        {
            ready = Ready,
            status = 200,
            noStore = true,
            emptyOwnedList = true,
            supplierStartsBefore = 0,
            supplierStartsAfter = SupplierStarts,
            supplierCreates = SupplierCreates,
            sampledChildPid = state.GetProperty("pid").GetInt32(),
            parentPid = Environment.ProcessId,
        };
    }

    internal async Task StopAsync(CancellationToken ct)
    {
        if (!stopped)
        {
            try
            {
                if (!process.HasExited && !stopRequested)
                {
                    stopRequested = true;
                    Cleanup = await CommandAsync("stop", new { completed = Completed }, ct);
                }
            }
            finally
            {
                // EOF also tears down an incomplete child. Do not mark cleanup complete until exit.
                input.Dispose();
                await process.WaitForExitAsync(ct);
                stopped = true;
            }
            if (process.ExitCode != (Completed ? 0 : 1))
                throw new InvalidOperationException("Owned child cleanup exit was unexpected.");
            // Never print raw stderr (which is not part of the bounded metrics contract).
            if (!string.IsNullOrEmpty(await stderr))
                throw new InvalidOperationException(
                    "Owned child wrote unexpected diagnostic output."
                );
        }
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await StopAsync(deadline.Token);
        process.Dispose();
        commands.Dispose();
    }

    internal static async Task RunChildAsync(string root, string manifestPath, CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows())
            throw new InvalidOperationException("The fixed Host profile requires Windows.");
        using var process = Process.GetCurrentProcess();
        process.ProcessorAffinity = (nint)3;
        if (process.ProcessorAffinity != (nint)3)
            throw new InvalidOperationException("Child affinity was not established.");
        using var input = Console.OpenStandardInput();
        using var output = Console.OpenStandardOutput();
        // Reserve raw stdout for framed telemetry before any fixture/Testcontainers logger starts.
        Console.SetOut(TextWriter.Null);
        Console.SetError(TextWriter.Null);
        var outbound = Channel.CreateBounded<object>(
            new BoundedChannelOptions(HotelsL1Frames.MaximumQueuedFrames)
            {
                SingleReader = true,
                SingleWriter = true,
                FullMode = BoundedChannelFullMode.Wait,
            }
        );
        var writing = Task.Run(
            async () =>
            {
                await foreach (var frame in outbound.Reader.ReadAllAsync(ct))
                    await HotelsL1Frames.WriteAsync(output, frame, ct);
            },
            ct
        );
        void Reply(string kind, object data)
        {
            if (!outbound.Writer.TryWrite(new { kind, data }))
                throw new InvalidOperationException("CLI observation queue overflow.");
        }
        var completed = false;
        var diagnostic = Environment.GetEnvironmentVariable("HOTELS_L1_DIAGNOSTIC") == "1";
        object? assets = null;
        try
        {
            await using (var fixture = new HotelsHostFixture())
            {
                fixture.AssetsReady = owned =>
                {
                    assets = owned;
                    File.WriteAllText(
                        Path.Combine(
                            root,
                            "TestResults",
                            "hotels-m1",
                            $"fixture-child-assets-{Environment.ProcessId}.json"
                        ),
                        JsonSerializer.Serialize(owned, HotelsL1Frames.Json)
                    );
                };
                await fixture.StartAsync(ct, 5111);
                await using var state = new HotelsL1ChildState(fixture, diagnostic);
                Reply(
                    "ready",
                    new
                    {
                        pid = Environment.ProcessId,
                        parentPid = int.Parse(
                            Environment.GetEnvironmentVariable("HOTELS_L1_PARENT_PID")
                                ?? throw new InvalidOperationException(
                                    "Owned parent identity missing."
                                )
                        ),
                        root,
                        assemblyHash = Hash(Assembly.GetExecutingAssembly().Location),
                        manifestHash = Hash(manifestPath),
                        diagnostic,
                        memoryRuntime = diagnostic
                            ? new
                            {
                                runtime = Environment.Version.ToString(),
                                serverGc = GCSettings.IsServerGC,
                                cachedProcessorCount = Environment.ProcessorCount,
                                affinity = "0x3",
                            }
                            : null,
                        affinity = "0x3",
                        isolated = fixture.IsolatedConfiguration,
                        client = fixture.Client.BaseAddress!.AbsoluteUri,
                        listeners = fixture
                            .Services.GetRequiredService<IServer>()
                            .Features.Get<IServerAddressesFeature>()!
                            .Addresses.ToArray(),
                        assets,
                    }
                );
                while (true)
                {
                    var message = await HotelsL1Frames.ReadAsync(input, ct);
                    var command = message.GetProperty("command").GetString();
                    var payload = message.GetProperty("payload");
                    if (command == "stop")
                    {
                        completed = payload.GetProperty("completed").GetBoolean();
                        break;
                    }
                    var data = command switch
                    {
                        "prepare" => await state.PrepareAsync(ct),
                        "begin" => state.Begin(payload),
                        "sample" => await state.SampleAsync(ct),
                        "facts" => await state.FinalFactsAsync(ct),
                        _ => throw new InvalidDataException("Unknown CLI command."),
                    };
                    Reply(command!, data);
                }
            }
            // This reply is emitted only after actual Host/container/key cleanup completed.
            Reply(
                "stop",
                new
                {
                    completed,
                    cleaned = true,
                    assets,
                    pid = Environment.ProcessId,
                }
            );
        }
        finally
        {
            outbound.Writer.TryComplete();
            await writing;
            Environment.ExitCode = completed ? 0 : 1;
        }
    }
}

internal sealed class HotelsL1ChildState(HotelsHostFixture fixture, bool diagnostic)
    : IAsyncDisposable
{
    private readonly List<Guid> drafts = [];
    private readonly List<L1Stock> observationStock = [];
    private readonly List<L1Stock> statusStock = [];
    private readonly Channel<L1Start> starts = Channel.CreateBounded<L1Start>(64);
    private long startTimestamp;
    private DateTimeOffset began;
    private bool prepared,
        overflow;
    private int supplierStarts;
    private Task? phases;
    private readonly CancellationTokenSource phaseCancellation = new();

    private L1StageMemory Stage(string name)
    {
        using var process = Process.GetCurrentProcess();
        return new(
            name,
            L1BclMemory.Capture(),
            L1ProcessMemory.Capture(process),
            fixture.Supplier.GetRetentionSnapshot(false)
        );
    }

    private double Elapsed =>
        startTimestamp == 0
            ? 0
            : (Stopwatch.GetTimestamp() - startTimestamp) / (double)Stopwatch.Frequency;

    internal async Task<object> PrepareAsync(CancellationToken ct)
    {
        if (prepared || startTimestamp != 0)
            throw new InvalidOperationException("CLI preparation can run only once.");
        prepared = true;
        var settings = fixture.Services.GetRequiredService<IOptions<HotelsOptions>>().Value;
        var requestBytes = fixture
            .Services.GetRequiredService<IOptions<HotelRequestOptions>>()
            .Value.RequestBytes;
        var ingressBytes = fixture
            .Services.GetRequiredService<IOptions<RawWebhookTransportOptions>>()
            .Value.MaximumBytes;
        var policy = fixture.Services.GetRequiredService<HotelPolicy>();
        if (
            settings.ForegroundStarts != 24
            || settings.RecoveryStarts != 6
            || settings.ForegroundConcurrency != 1
            || settings.RecoveryConcurrency != 1
            || settings.WindowSeconds != 60
            || settings.ReadTimeoutSeconds != 15
            || settings.CreateTimeoutSeconds != 140
            || settings.CacheEntries != 128
            || settings.CacheBytes != 16777216
            || settings.ResponseBytes != 2097152
            || settings.Properties != 100
            || settings.Rates != 200
            || requestBytes != 16384
            || ingressBytes != 2097152
            || policy.SnapshotSeconds != 300
            || policy.QuoteSeconds != 300
            || policy.EscalationSeconds != 600
            || policy.ReadWindowSeconds != 300
            || policy.ReadAllowance != 3
            || policy.SecondReadDelaySeconds != 30
            || policy.ThirdReadDelaySeconds != 120
            || policy.DraftRetentionHours != 24
            || policy.FinishedRetentionDays != 90
        )
            throw new InvalidOperationException("The predeclared settings changed.");
        var stages = new List<L1StageMemory>(4);
        if (diagnostic)
            stages.Add(Stage("beforeSeed"));
        fixture.Supplier.ChangeTermsOnRefresh = false;
        fixture.Supplier.DelayMilliseconds = 0;
        var seedReceipt = await SeedAsync(ct);
        if (diagnostic)
            stages.Add(Stage("afterSeed"));
        var cacheProof = FillAndBoundCache();
        if (diagnostic)
            stages.Add(Stage("afterCacheProof"));
        fixture.Supplier.DelayMilliseconds = 100;
        supplierStarts = fixture.Supplier.Starts.Count;
        var retainedBefore = await RetainedCountAsync(ct);
        var inboxBefore = await InboxFootprintAsync(ct);
        var initialFake = diagnostic ? fixture.Supplier.GetRetentionSnapshot(true) : null;
        if (diagnostic)
            stages.Add(Stage("beforeTimed"));
        return new
        {
            drafts,
            observationStock,
            statusStock,
            seedReceipt,
            cacheProof,
            retainedBefore,
            inboxBefore,
            memoryStages = diagnostic ? stages : null,
            initialFake,
            supplierStarts,
            supplierCreates = fixture.Supplier.Creates,
            settings = new
            {
                settings.ForegroundStarts,
                settings.RecoveryStarts,
                settings.ForegroundConcurrency,
                settings.RecoveryConcurrency,
                settings.WindowSeconds,
                settings.ReadTimeoutSeconds,
                settings.CreateTimeoutSeconds,
                settings.CacheEntries,
                settings.CacheBytes,
                settings.ResponseBytes,
                settings.Properties,
                settings.Rates,
                requestBytes,
                ingressBytes,
                policy.SnapshotSeconds,
                policy.QuoteSeconds,
                policy.EscalationSeconds,
                policy.ReadWindowSeconds,
                policy.ReadAllowance,
                policy.SecondReadDelaySeconds,
                policy.ThirdReadDelaySeconds,
                policy.DraftRetentionHours,
                policy.FinishedRetentionDays,
            },
        };
    }

    internal object Begin(JsonElement payload)
    {
        if (!prepared || startTimestamp != 0)
            throw new InvalidOperationException("CLI begin can run only once after preparation.");
        began = payload.GetProperty("began").GetDateTimeOffset();
        startTimestamp = payload.GetProperty("timestamp").GetInt64();
        if (startTimestamp > Stopwatch.GetTimestamp() || Elapsed > 5)
            throw new InvalidOperationException("CLI monotonic start is invalid.");
        fixture.Supplier.OnStart = operation =>
        {
            Interlocked.Increment(ref supplierStarts);
            if (
                !starts.Writer.TryWrite(
                    new(
                        Elapsed,
                        operation,
                        fixture.Services.GetRequiredService<HotelCapacity>().GetSnapshot()
                    )
                )
            )
                overflow = true;
        };
        phases = ConfigurePhasesAsync(phaseCancellation.Token);
        return new
        {
            began,
            timestamp = startTimestamp,
            pid = Environment.ProcessId,
        };
    }

    private async Task WaitUntil(double at, CancellationToken ct)
    {
        var left = at - Elapsed;
        if (left > 0)
            await Task.Delay(TimeSpan.FromSeconds(left), ct);
    }

    private async Task ConfigurePhasesAsync(CancellationToken ct)
    {
        await WaitUntil(150, ct);
        fixture.Supplier.Degraded = true;
        fixture.Supplier.NextCreate = "lost";
        await WaitUntil(210, ct);
        fixture.Supplier.Degraded = false;
        fixture.Supplier.NextCreate = "confirmed";
        fixture.Supplier.DelayMilliseconds = 500;
        await WaitUntil(240, ct);
        fixture.Supplier.DelayMilliseconds = 100;
    }

    public async ValueTask DisposeAsync()
    {
        await phaseCancellation.CancelAsync();
        try
        {
            if (phases is not null)
                await phases;
        }
        catch (OperationCanceledException) when (phaseCancellation.IsCancellationRequested) { }
        phaseCancellation.Dispose();
    }

    internal async Task<object> SampleAsync(CancellationToken ct)
    {
        var observerStarted = Stopwatch.GetTimestamp();
        if (overflow)
            throw new InvalidOperationException("Bounded supplier observation overflow.");
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HotelsDbContext>();
        var now = DateTimeOffset.UtcNow;
        var pending = await db
            .Inbox.AsNoTracking()
            .Where(i => i.ProcessedAt == null)
            .Select(i => i.ReceivedAt)
            .ToArrayAsync(ct);
        var runtime = fixture.Services.GetRequiredService<IWolverineRuntime>();
        var incoming = await runtime.Storage.Admin.AllIncomingAsync();
        var outgoing = await runtime.Storage.Admin.AllOutgoingAsync();
        var envelopes = incoming
            .Concat(outgoing)
            .Where(e =>
                e.Status != EnvelopeStatus.Handled
                && e.MessageType?.Contains("Hotels", StringComparison.Ordinal) == true
            )
            .ToArray();
        var due = envelopes
            .Select(e => e.ScheduledTime is { } scheduled ? scheduled : e.SentAt)
            .Where(d => d <= now)
            .ToArray();
        var cache = fixture.Services.GetRequiredService<HotelSnapshotCache>();
        var events = new List<L1Start>(64);
        while (starts.Reader.TryRead(out var item))
            events.Add(item);
        var reads = new List<L1Read>(64);
        while (fixture.Supplier.ReadCompletions.TryDequeue(out var read))
        {
            if (startTimestamp != 0 && read.At >= began)
                reads.Add(new(read.At, read.BookingId, read.Status, read.Milliseconds));
            if (reads.Count > 64)
                throw new InvalidOperationException("Bounded read observation overflow.");
        }
        // This queue is fictional test telemetry, not production state or admission counters.
        // Its exact cumulative total is retained in supplierStarts; history lives in the parent.
        if (startTimestamp != 0)
            while (fixture.Supplier.Starts.TryDequeue(out _)) { }
        return new
        {
            pid = Environment.ProcessId,
            supplierStarts = startTimestamp == 0 ? fixture.Supplier.Starts.Count : supplierStarts,
            supplierCreates = fixture.Supplier.Creates,
            starts = events,
            reads,
            memoryDiagnostic = diagnostic
                ? new L1SampleMemory(
                    L1BclMemory.Capture(),
                    fixture.Supplier.GetRetentionSnapshot(false),
                    new(
                        envelopes
                            .Where(e => (e.ScheduledTime ?? e.SentAt) <= now)
                            .Sum(e => (long)(e.Data?.Length ?? 0)),
                        envelopes
                            .Where(e => (e.ScheduledTime ?? e.SentAt) > now)
                            .Sum(e => (long)(e.Data?.Length ?? 0)),
                        incoming
                            .Where(e => envelopes.Contains(e))
                            .Sum(e => (long)(e.Data?.Length ?? 0)),
                        outgoing
                            .Where(e => envelopes.Contains(e))
                            .Sum(e => (long)(e.Data?.Length ?? 0)),
                        envelopes.Count(e => e.Data is null)
                    ),
                    Stopwatch.GetElapsedTime(observerStarted).TotalMilliseconds
                )
                : null,
            metrics = new
            {
                at = Elapsed,
                rss = 0,
                managedHeap = GC.GetTotalMemory(false),
                cpuMilliseconds = 0,
                cacheEntries = cache.Count,
                cacheBytes = cache.PayloadBytes,
                reservations = await db.Reservations.CountAsync(ct),
                retainedInboxRows = await db.Inbox.CountAsync(ct),
                retainedInboxCiphertextBytes = await InboxBytesAsync(db, ct),
                pendingInbox = pending.Length,
                oldestInboxLagMs = pending.Length == 0
                    ? 0
                    : (now - pending.Min()).TotalMilliseconds,
                dueWork = due.Length,
                futureWork = envelopes.Length - due.Length,
                oldestWorkLagMs = due.Length == 0 ? 0 : (now - due.Min()).TotalMilliseconds,
                capacity = fixture.Services.GetRequiredService<HotelCapacity>().GetSnapshot(),
            },
        };
    }

    internal async Task<object> FinalFactsAsync(CancellationToken ct)
    {
        if (phases is null || !phases.IsCompletedSuccessfully || Elapsed < 360)
            throw new InvalidOperationException("Final facts require the full fixed duration.");
        // Capture before the detailed fake JSON projection: no timed sample includes its allocations.
        var finalMemory = diagnostic ? Stage("afterTimedBeforePayloadProjection") : null;
        var finalFake = diagnostic ? fixture.Supplier.GetRetentionSnapshot(true) : null;
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HotelsDbContext>();
        var rows = await db.Reservations.AsNoTracking().ToArrayAsync(ct);
        if (rows.Length != 38)
            throw new InvalidOperationException("Fixed reservation cardinality changed.");
        return new
        {
            rows = rows.Select(r =>
                {
                    var state = JsonSerializer.Deserialize<ReservationState>(r.StateJson)!;
                    return new L1ReservationFact(
                        r.Id,
                        r.AttemptId,
                        r.SenderFinished,
                        state.Outcome,
                        state.ReadsClaimed,
                        state.ReadAllowance,
                        state.FirstKnownAt,
                        state.ReadUntil
                    );
                })
                .ToArray(),
            inbox = await InboxFootprintAsync(ct),
            supplierStarts,
            supplierCreates = fixture.Supplier.Creates,
            finalMemory,
            finalFake,
        };
    }

    private static HotelParty Party() =>
        new(
            [new("Fictional", "Traveller")],
            "fictional@example.invalid",
            "+44000000000",
            true,
            true
        );

    private async Task<object> SeedAsync(CancellationToken ct)
    {
        using var raw = new HttpClient(fixture.Supplier, disposeHandler: false);
        StayTerms? accepted = null;
        for (var i = 0; i < 38; i++)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            using var search = await raw.PostAsJsonAsync(
                "https://api.duffel.com/stays/search",
                new
                {
                    data = new
                    {
                        check_in_date = today.AddDays(7).ToString("yyyy-MM-dd"),
                        check_out_date = today.AddDays(9).ToString("yyyy-MM-dd"),
                        guests = new[] { new { type = "adult" } },
                    },
                },
                ct
            );
            var found = JsonNode.Parse(await search.Content.ReadAsStringAsync(ct))!["data"]![
                "results"
            ]![0]!;
            var rateId = found["accommodation"]!["rooms"]![0]!["rates"]![0]![
                "id"
            ]!.GetValue<string>();
            using var quote = await raw.PostAsJsonAsync(
                "https://api.duffel.com/stays/quotes",
                new { data = new { rate_id = rateId } },
                ct
            );
            var quoteId = JsonNode.Parse(await quote.Content.ReadAsStringAsync(ct))!["data"]![
                "id"
            ]!.GetValue<string>();
            string? bookingId = null;
            if (i < 14)
            {
                using var create = await raw.PostAsJsonAsync(
                    "https://api.duffel.com/stays/bookings",
                    new
                    {
                        data = new
                        {
                            quote_id = quoteId,
                            guests = new[]
                            {
                                new { given_name = "Fictional", family_name = "Traveller" },
                            },
                        },
                    },
                    ct
                );
                bookingId = JsonNode.Parse(await create.Content.ReadAsStringAsync(ct))!["data"]![
                    "id"
                ]!.GetValue<string>();
                if (accepted is null)
                {
                    var reader = (IHotelReservationReader)
                        fixture
                            .Services.GetServices<IHotelBookingProvider>()
                            .Single(p => p.Key == "duffel-stays");
                    accepted = (await reader.ReadAsync(bookingId, ct)).Facts!.Actual!;
                }
            }
            var now = DateTimeOffset.UtcNow;
            var id = new Guid(
                MD5.HashData(System.Text.Encoding.UTF8.GetBytes("20261017:hotel-stock:" + i))
            );
            var row = new HotelReservation
            {
                Id = id,
                OwnerId = Guid.Parse(HotelsHostFixture.Owner),
                QuoteRevision = 1,
                Provider = "duffel-stays",
                Environment = "Test",
                ProviderAccount = HotelsHostFixture.Account,
                Terms = accepted!,
                SourcePreparationReference = quoteId,
                CreatedAt = now,
                UpdatedAt = now,
                ValidUntil = now.AddMinutes(5),
                PrivatePurgeAt = now.AddHours(24),
            };
            using var scope = fixture.Services.CreateScope();
            var protector = scope.ServiceProvider.GetRequiredService<IHotelPrivateProtector>();
            row.ProtectedContext = protector
                .Protect(
                    JsonSerializer.Serialize(
                        new ProviderContextHandle(
                            1,
                            JsonSerializer.Serialize(new { QuoteId = quoteId, RateId = rateId })
                        )
                    ),
                    HotelReservationService.Binding(row, "context")
                )
                .Value;
            HotelWork[] work = [];
            if (bookingId is not null)
            {
                row.AttemptId = Guid.NewGuid();
                row.ActionId = Guid.NewGuid();
                row.AdmittedAt = now;
                row.SenderFinished = true;
                row.PrivatePurgeAt = null;
                row.State.Outcome = ReservationOutcome.Unknown;
                row.State.Review.Add(ReviewReason.TransportUnknown);
                row.State.EscalateAt = now.AddMinutes(10);
                row.ProtectedParty = protector
                    .Protect(
                        JsonSerializer.Serialize(Party()),
                        HotelReservationService.Binding(row, "party")
                    )
                    .Value;
                work = [new(id, true, row.State.EscalateAt.Value)];
                if (i < 2)
                {
                    row.State.Apply(
                        new(ObservationKind.Confirmed, new(bookingId, null, accepted)),
                        accepted,
                        now
                    );
                    statusStock.Add(new(id, quoteId, bookingId));
                }
                else
                    observationStock.Add(new(id, quoteId, bookingId));
            }
            else
                drafts.Add(id);
            var saved = await scope
                .ServiceProvider.GetRequiredService<IHotelReservationStore>()
                .SaveAsync(row, 0, work, ct);
            if (saved.IsError || !saved.Value)
                throw new InvalidOperationException("Fixed stock seed failed.");
        }
        return new
        {
            drafts = drafts.Count,
            spentWithoutKnownIdentity = observationStock.Count,
            confirmedStatus = statusStock.Count,
            remoteSeedCreates = fixture.Supplier.Creates,
            supplierSeedStarts = fixture.Supplier.Starts.Count,
        };
    }

    private object FillAndBoundCache()
    {
        var cache = fixture.Services.GetRequiredService<HotelSnapshotCache>();
        var terms = new StayTerms(
            new("acc_cache_fictional", "Fictional cache hotel", null, null, null),
            "Double",
            "rate_cache",
            new(
                DateOnly.FromDateTime(DateTime.UtcNow).AddDays(7),
                DateOnly.FromDateTime(DateTime.UtcNow).AddDays(9)
            ),
            new(1),
            new(120.25m, "GBP"),
            [],
            [],
            new("NonRefundable", []),
            [],
            true,
            "ImmediateTest"
        );
        var request = new StaySearch(HotelSearchService.Areas[0], terms.Dates, terms.Occupancy);
        void Fill(bool large)
        {
            for (var i = 0; i < 140; i++)
            {
                var offers = Enumerable
                    .Range(0, large ? 100 : 1)
                    .Select(j => new HotelOffer(
                        "duffel-stays",
                        "Test",
                        "cache_" + i + "_" + j,
                        terms with
                        {
                            Conditions = large
                                ? [new(new string('x', 2048), "Fictional source scope")]
                                : [],
                        },
                        null,
                        true,
                        null
                    ))
                    .ToArray();
                if (
                    !cache.Put(
                        new HotelSearchSnapshot(
                            Guid.NewGuid(),
                            request,
                            new(ExecutionKind.Complete, offers),
                            [new("duffel-stays", ExecutionKind.Complete, null)],
                            DateTimeOffset.UtcNow.AddMinutes(5)
                        )
                    )
                )
                    throw new InvalidOperationException("Valid bounded cache seed refused.");
            }
        }
        Fill(false);
        var entryCap = cache.Count;
        Fill(true);
        var byteCapEntries = cache.Count;
        var byteCapBytes = cache.PayloadBytes;
        Fill(false);
        if (
            entryCap != 128
            || byteCapEntries >= 128
            || byteCapBytes > 16777216
            || cache.Count != 128
        )
            throw new InvalidOperationException("Whole snapshot entry/byte cap proof failed.");
        return new
        {
            entryCap,
            byteCapEntries,
            byteCapBytes,
            timedStartEntries = cache.Count,
            timedStartBytes = cache.PayloadBytes,
        };
    }

    private async Task<int> RetainedCountAsync(CancellationToken ct)
    {
        using var scope = fixture.Services.CreateScope();
        return await scope
            .ServiceProvider.GetRequiredService<HotelsDbContext>()
            .Reservations.CountAsync(ct);
    }

    private async Task<object> InboxFootprintAsync(CancellationToken ct)
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HotelsDbContext>();
        return new
        {
            rows = await db.Inbox.CountAsync(ct),
            ciphertextBytes = await InboxBytesAsync(db, ct),
        };
    }

    private static Task<long> InboxBytesAsync(HotelsDbContext db, CancellationToken ct) =>
        db
            .Database.SqlQueryRaw<long>(
                "SELECT coalesce(sum(octet_length(protected_payload)),0)::bigint AS \"Value\" FROM hotels.observation_inbox"
            )
            .SingleAsync(ct);
}

internal static class HotelsL1HostProof
{
    internal static async Task RunAsync(
        string root,
        string manifestPath,
        CancellationToken ct,
        bool diagnostic = false
    )
    {
        var proofPrefix = diagnostic ? "l1-memory-diagnostic" : "l1-isolation";
        var output = Path.Combine(root, "TestResults", "hotels-m1", proofPrefix + "-proof.json");
        if (File.Exists(output))
            throw new InvalidOperationException(
                "Retain the prior owned lifecycle proof before repeating it."
            );
        var protocol = new List<object>();
        foreach (var count in new[] { 0, -1, HotelsL1Frames.MaximumBytes + 1 })
        {
            var prefix = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(prefix, count);
            using var stream = new MemoryStream(prefix);
            try
            {
                await HotelsL1Frames.ReadAsync(stream, ct);
                throw new InvalidOperationException("Invalid CLI frame was accepted.");
            }
            catch (InvalidDataException)
            {
                protocol.Add(
                    new
                    {
                        count,
                        rejected = true,
                        commandCalls = 0,
                    }
                );
            }
        }
        using (var stream = new MemoryStream())
        {
            await HotelsL1Frames.WriteAsync(
                stream,
                new string('x', HotelsL1Frames.MaximumBytes - 2),
                ct
            );
            stream.Position = 0;
            var value = await HotelsL1Frames.ReadAsync(stream, ct);
            if (value.GetString()!.Length != HotelsL1Frames.MaximumBytes - 2)
                throw new InvalidOperationException("Exact-bound CLI frame did not roundtrip.");
            protocol.Add(new { exactBytes = HotelsL1Frames.MaximumBytes, accepted = true });
        }
        using (var stream = new MemoryStream())
        {
            try
            {
                await HotelsL1Frames.WriteAsync(
                    stream,
                    new string('x', HotelsL1Frames.MaximumBytes - 1),
                    ct
                );
                throw new InvalidOperationException("Oversized CLI write was accepted.");
            }
            catch (InvalidDataException)
            {
                if (stream.Length != 0)
                    throw new InvalidOperationException(
                        "Oversized CLI write emitted partial bytes."
                    );
                protocol.Add(new { oversizedWriteRejected = true, bytesWritten = stream.Length });
            }
        }
        foreach (var malformedJson in new[] { false, true })
        {
            var bytes = new byte[malformedJson ? 5 : 4];
            BinaryPrimitives.WriteInt32LittleEndian(bytes, 1);
            if (malformedJson)
                bytes[4] = (byte)'!';
            using var stream = new MemoryStream(bytes);
            try
            {
                await HotelsL1Frames.ReadAsync(stream, ct);
                throw new InvalidOperationException("Malformed CLI frame was accepted.");
            }
            catch (Exception ex) when (ex is JsonException or EndOfStreamException)
            {
                protocol.Add(
                    new
                    {
                        malformedJson,
                        rejected = true,
                        commandCalls = 0,
                    }
                );
            }
        }
        await File.WriteAllTextAsync(
            Path.Combine(root, "TestResults", "hotels-m1", proofPrefix + "-protocol-proof.json"),
            JsonSerializer.Serialize(protocol, HotelsL1Frames.Json),
            ct
        );
        using var emptyFake = new HotelsFakeSupplier();
        var cheap = emptyFake.GetRetentionSnapshot(false);
        var detailed = emptyFake.GetRetentionSnapshot(true);
        if (
            cheap.RateEntries != 0
            || cheap.QuoteEntries != 0
            || cheap.BookingEntries != 0
            || cheap.PreparationEntries != 0
            || cheap.RateValueJsonUtf8Bytes is not null
            || detailed.RateValueJsonUtf8Bytes != 0
            || detailed.QuoteValueJsonUtf8Bytes != 0
            || detailed.BookingValueJsonUtf8Bytes != 0
            || detailed.PreparationMapJsonUtf8Bytes != 2
        )
            throw new InvalidOperationException("Readonly fake retention accounting is invalid.");
        var unicode = new JsonObject { ["example"] = "é" };
        var unicodeBytes = System.Text.Encoding.UTF8.GetByteCount(unicode.ToJsonString());
        if (unicodeBytes != JsonSerializer.SerializeToUtf8Bytes(unicode).Length)
            throw new InvalidOperationException("JSON UTF8 accounting is not byte-based.");
        await using var host = await HotelsL1HostProcess.StartAsync(
            root,
            manifestPath,
            ct,
            diagnostic
        );
        var preflight = await host.PreflightAsync(ct);
        var sample = await host.CommandAsync("sample", null, ct);
        if (diagnostic)
        {
            var memory = sample
                .GetProperty("memoryDiagnostic")
                .Deserialize<L1SampleMemory>(HotelsL1Frames.Json)!;
            if (
                memory.Memory.EstimatedAllocatedHeapBytes <= 0
                || memory.Memory.ApproximateTotalAllocatedBytes <= 0
                || memory.Memory.Gen0Collections < 0
                || memory.Memory.Gen1Collections < 0
                || memory.Memory.Gen2Collections < 0
                || memory.Memory.LastGc.Index < 0
                || memory.Memory.ActiveTimers < 0
                || memory.Memory.ThreadPoolThreads < 0
                || memory.Fake.RateEntries != 0
                || memory.Fake.RateValueJsonUtf8Bytes is not null
                || memory.Envelopes.DueBodyBytes != 0
                || memory.Envelopes.FutureBodyBytes != 0
                || memory.ObserverUntilMemorySnapshotWallMilliseconds < 0
            )
                throw new InvalidOperationException("Diagnostic counter shape is invalid.");
        }
        var pid = host.Process.Id;
        if (pid == Environment.ProcessId || sample.GetProperty("pid").GetInt32() != pid)
            throw new InvalidOperationException("Host and driver are not separate processes.");
        host.Process.Refresh();
        var hostRss = host.Process.WorkingSet64;
        using var driver = Process.GetCurrentProcess();
        driver.Refresh();
        await host.StopAsync(ct); // An incomplete experiment must clean up and exit1.
        var assets = host.Ready.GetProperty("assets");
        var container = assets.GetProperty("containerId").GetString()!;
        var directory = Path.GetFullPath(assets.GetProperty("temporaryDirectory").GetString()!);
        if (
            container.Length != 64
            || container.Any(c => !Uri.IsHexDigit(c))
            || !directory.StartsWith(
                Path.GetFullPath(Path.GetTempPath()),
                StringComparison.OrdinalIgnoreCase
            )
            || !Path.GetFileName(directory)
                .StartsWith("travel-hotels-host-", StringComparison.Ordinal)
        )
            throw new InvalidOperationException("Owned asset identity is invalid.");
        var inspect = new ProcessStartInfo("docker")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
        };
        foreach (
            var argument in new[] { "ps", "-a", "-q", "--no-trunc", "--filter", "id=" + container }
        )
            inspect.ArgumentList.Add(argument);
        using var docker =
            Process.Start(inspect)
            ?? throw new InvalidOperationException("Owned cleanup check unavailable.");
        var containers = await docker.StandardOutput.ReadToEndAsync(ct);
        await docker.WaitForExitAsync(ct);
        if (
            docker.ExitCode != 0
            || !string.IsNullOrWhiteSpace(containers)
            || Directory.Exists(directory)
        )
            throw new InvalidOperationException("Owned child assets were not cleaned.");
        var receipt = new
        {
            passed = true,
            protocol,
            counterProof = new
            {
                cheap,
                detailed,
                unicodeBytes,
                supplierStarts = emptyFake.Starts.Count,
            },
            diagnosticOnly = diagnostic,
            preflight,
            childPid = pid,
            driverPid = Environment.ProcessId,
            hostRss,
            driverRss = driver.WorkingSet64,
            sample,
            cleanup = host.Cleanup,
            childExitCode = host.Process.ExitCode,
            containerMissing = true,
            temporaryDirectoryMissing = true,
            sourceUnchanged = true,
            measuredHostIncludesDriverHistory = false,
        };
        HotelsL1Runner.VerifyManifestFile(root, manifestPath);
        await File.WriteAllTextAsync(
            output,
            JsonSerializer.Serialize(
                receipt,
                new JsonSerializerOptions(HotelsL1Frames.Json) { WriteIndented = true }
            ),
            ct
        );
        Console.WriteLine(
            JsonSerializer.Serialize(
                new
                {
                    ownedLifecycleProof = true,
                    childPid = pid,
                    childExitCode = host.Process.ExitCode,
                    output,
                }
            )
        );
    }
}
