using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Travel.Host.Tests.Integration.Hotels;
using Travel.Modules.Hotels.Application;
using Travel.Modules.Hotels.Core;
using Travel.Modules.Hotels.Infrastructure.Providers.Duffel;

namespace Travel.Hotels.Fixture;

// Explicit CLI-only experiment. No xUnit attribute, ordinary test registration, or product endpoint.
internal sealed class HotelsL1Runner(HotelsL1HostProcess host, string root)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };
    private readonly ConcurrentBag<Operation> operations = [];
    private readonly ConcurrentBag<Resources> resources = [];
    private readonly ConcurrentQueue<HttpStart> httpStarts = [];
    private readonly ConcurrentDictionary<Guid, double> firstIdentityOffers = [];
    private readonly ConcurrentQueue<ReadyQuote> prepared = [];
    private readonly List<Guid> drafts = [];
    private readonly List<Stock> observationStock = [];
    private readonly List<Stock> statusStock = [];
    private readonly Stopwatch clock = new();
    private readonly SemaphoreSlim checkpointGate = new(1);
    private Catalog? latestSearch,
        latestRates;
    private int draftIndex = -1,
        observationIndex = -1,
        eventIndex;
    private DateTimeOffset began;
    private readonly string output = Path.Combine(
        root,
        "TestResults",
        "hotels-m1",
        host.Diagnostic ? "l1-memory-diagnostic" : "l1"
    );
    private readonly ConcurrentBag<L1ParentMemorySample> memorySamples = [];

    private sealed record Catalog(Guid Id, string SelectionKey);

    private sealed record ReadyQuote(Guid Id, int Revision);

    private sealed record Stock(Guid Id, string QuoteId, string BookingId);

    private readonly ConcurrentQueue<L1Read> readCompletions = [];
    private readonly ConcurrentBag<object> driverResources = [];
    private JsonElement setup,
        finalFacts;
    private long beganTimestamp;

    private sealed record Job(double Due, string Kind);

    private sealed record HttpStart(double At, string Operation, HotelCapacitySnapshot Capacity);

    private sealed record Operation(
        string Phase,
        string Kind,
        double Due,
        double Started,
        double Completed,
        int Status,
        string? Result,
        bool Useful,
        string? Refusal,
        Guid? ReservationId
    );

    private sealed record Resources(
        double At,
        long Rss,
        long ManagedHeap,
        double CpuMilliseconds,
        int CacheEntries,
        int CacheBytes,
        int Reservations,
        int RetainedInboxRows,
        long RetainedInboxCiphertextBytes,
        int PendingInbox,
        double OldestInboxLagMs,
        int DueWork,
        int FutureWork,
        double OldestWorkLagMs,
        HotelCapacitySnapshot Capacity
    );

    public async Task RunAsync(string manifestPath, CancellationToken ct)
    {
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
            throw new InvalidOperationException(
                "Existing L1 receipts must be retained; a rerun requires review and a separate output location."
            );
        Directory.CreateDirectory(output);
        await File.WriteAllTextAsync(
            Path.Combine(output, "planned-jobs.json"),
            JsonSerializer.Serialize(Schedule(), Json),
            CancellationToken.None
        );
        try
        {
            await RunCoreAsync(manifestPath, ct);
        }
        catch (Exception ex)
        {
            await WriteCheckpointAsync();
            await File.WriteAllTextAsync(
                Path.Combine(output, "failure.json"),
                JsonSerializer.Serialize(
                    new
                    {
                        passed = false,
                        diagnosticOnly = host.Diagnostic,
                        phase = began == default ? "Setup" : "TimedOrEvaluation",
                        exceptionType = ex.GetType().Name,
                        offered = 3152,
                        recorded = operations.Count,
                        supplierStartsIncludingSeed = host.SupplierStarts,
                        supplierCreatesIncludingSeed = host.SupplierCreates,
                        note = "Original planned jobs and partial raw data retained; no automatic rerun.",
                    },
                    Json
                ),
                CancellationToken.None
            );
            throw;
        }
    }

    private async Task<object> VerifyOwnedConnectionAsync(CancellationToken ct)
    {
        var receipt = await host.PreflightAsync(ct);
        await File.WriteAllTextAsync(
            Path.Combine(output, "connection-preflight.json"),
            JsonSerializer.Serialize(receipt, Json),
            ct
        );
        return receipt;
    }

    private async Task RunCoreAsync(string manifestPath, CancellationToken ct)
    {
        var manifest =
            JsonSerializer.Deserialize<Dictionary<string, string>>(
                await File.ReadAllTextAsync(manifestPath, ct)
            ) ?? throw new InvalidOperationException("Source manifest missing.");
        VerifyManifest(root, manifest);
        if (!OperatingSystem.IsWindows())
            throw new InvalidOperationException(
                "The fixed profile targets the recorded Windows environment."
            );
        var actualCpu = Environment.ProcessorCount;
        var connectionPreflight = await VerifyOwnedConnectionAsync(ct);
        setup = await host.CommandAsync("prepare", null, ct);
        drafts.AddRange(setup.GetProperty("drafts").Deserialize<Guid[]>(HotelsL1Frames.Json)!);
        observationStock.AddRange(
            setup.GetProperty("observationStock").Deserialize<Stock[]>(HotelsL1Frames.Json)!
        );
        statusStock.AddRange(
            setup.GetProperty("statusStock").Deserialize<Stock[]>(HotelsL1Frames.Json)!
        );
        var seedReceipt = setup.GetProperty("seedReceipt");
        var cacheProof = setup.GetProperty("cacheProof");
        var supplierBefore = host.SupplierStarts;
        var createsBefore = host.SupplierCreates;
        var retainedBefore = setup.GetProperty("retainedBefore").GetInt32();
        var inboxBefore = setup.GetProperty("inboxBefore");
        await File.WriteAllTextAsync(
            Path.Combine(output, "before-run.json"),
            JsonSerializer.Serialize(
                new
                {
                    manifestPath,
                    sourceManifest = manifest,
                    environment = "Testing, memory-only configuration",
                    diagnosticOnly = host.Diagnostic,
                    diagnosticProfile = host.Diagnostic
                        ? new
                        {
                            path = "TestResults/hotels-m1/l1-memory-diagnostic-profile.md",
                            sha256 = HotelsL1HostProcess.Hash(
                                Path.Combine(
                                    root,
                                    "TestResults/hotels-m1/l1-memory-diagnostic-profile.md"
                                )
                            ),
                            acceptanceStatus = "Original l1/receipt.json remains failed; diagnostics cannot reclassify it.",
                        }
                        : null,
                    memoryStages = host.Diagnostic
                        ? setup.GetProperty("memoryStages")
                        : (JsonElement?)null,
                    initialFake = host.Diagnostic
                        ? setup.GetProperty("initialFake")
                        : (JsonElement?)null,
                    connectionPreflight,
                    actualLogicalCpu = actualCpu,
                    usedLogicalCpu = 2,
                    affinity = "0x3",
                    referenceMismatch = "Host has no hard2GiB memory limit; source profile records actual32GiB RAM/Docker16GiB. This does not establish hard-limited2GiB capacity.",
                    seed = 20261017,
                    seedReceipt,
                    cacheProof,
                    retainedBefore,
                    inboxBefore,
                    supplierStartsBeforeTimedRun = supplierBefore,
                    settings = setup.GetProperty("settings"),
                    measuredHostPid = host.Process.Id,
                    driverPid = Environment.ProcessId,
                    measurement = "Child actual Host RSS/CPU; parent owns all driver history/checkpoints. Child includes bounded readonly telemetry/fake-provider costs.",
                },
                Json
            ),
            ct
        );
        var jobs = Schedule();
        if (jobs.Count != 3152)
            throw new InvalidOperationException("The fixed offered denominator changed.");
        await File.WriteAllTextAsync(
            Path.Combine(output, "planned-jobs.json"),
            JsonSerializer.Serialize(jobs, Json),
            ct
        );
        using var timed = CancellationTokenSource.CreateLinkedTokenSource(ct);
        try
        {
            began = DateTimeOffset.UtcNow;
            beganTimestamp = Stopwatch.GetTimestamp();
            clock.Start();
            await host.CommandAsync("begin", new { began, timestamp = beganTimestamp }, ct);
            var sampling = SampleAsync(timed.Token);
            var requests = jobs.Select(job => ExecuteAsync(job, timed.Token)).ToArray();
            await Task.WhenAll(requests);
            await WaitUntil(360, ct);
            await sampling;
            var elapsed = clock.Elapsed.TotalSeconds;
            clock.Stop();
            finalFacts = await host.CommandAsync("facts", null, ct);
            host.Completed = true;
            if (
                host.SupplierStarts - supplierBefore != httpStarts.Count
                || readCompletions.Count != httpStarts.Count(start => start.Operation == "read")
            )
                throw new InvalidOperationException(
                    "Actual supplier/read starts do not reconcile with parent evidence."
                );
            var retained = finalFacts
                .GetProperty("rows")
                .Deserialize<L1ReservationFact[]>(HotelsL1Frames.Json)!;
            var retainedAfter = retained.Length;
            var samples = operations.OrderBy(o => o.Due).ToArray();
            var telemetry = resources.OrderBy(r => r.At).ToArray();
            var supplier = httpStarts
                .Select(s => new { at = s.At, operation = s.Operation })
                .ToArray();
            var phases = new[]
            {
                "Warmup",
                "Normal",
                "Degraded",
                "Burst",
                "RecoveryAdmission",
                "RecoverySteady",
            }
                .Select(name => Summary(name, samples.Where(s => s.Phase == name).ToArray()))
                .ToArray();
            var timedIds = samples
                .Where(s => s.Kind == "create" && s.ReservationId != null)
                .Select(s => s.ReservationId!.Value)
                .ToHashSet();
            var admitted = retained
                .Where(r => timedIds.Contains(r.Id) && r.AttemptId != null)
                .ToArray();
            var bounded = admitted.All(r =>
                r.SenderFinished && r.Outcome != ReservationOutcome.Submitted
            );
            var loss = samples
                .Where(s => s.Phase == "Degraded" && s.Kind == "create" && s.Result == "Unknown")
                .ToArray();
            var observed = observationStock
                .Where(s => firstIdentityOffers.ContainsKey(s.Id))
                .Select(stock =>
                {
                    var state = retained.Single(r => r.Id == stock.Id);
                    var reads = readCompletions
                        .Where(r => r.BookingId == stock.BookingId && r.At >= began)
                        .OrderBy(r => r.At)
                        .ToArray();
                    var offeredAt = firstIdentityOffers[stock.Id];
                    return new
                    {
                        stock.Id,
                        stock.BookingId,
                        offeredAt,
                        phase = Phase(offeredAt),
                        state.ReadsClaimed,
                        state.ReadAllowance,
                        state.FirstKnownAt,
                        state.ReadUntil,
                        outcome = state.Outcome.ToString(),
                        actualStarts = reads.Length,
                        derivedStarts = Math.Max(0, reads.Length - 1),
                        reads = reads.Select(
                            (r, index) =>
                                new
                                {
                                    primary = index == 0,
                                    at = (r.At - began).TotalSeconds,
                                    phase = Phase((r.At - began).TotalSeconds),
                                    r.Status,
                                    r.Milliseconds,
                                    elapsedSincePrimaryOfferMs = (r.At - began).TotalMilliseconds
                                        - offeredAt * 1000,
                                }
                        ),
                    };
                })
                .ToArray();
            var gates = Gates(samples, telemetry, retainedBefore, retainedAfter);
            gates["allElevenFirstIdentityReadsActuallyStarted"] =
                observed.Length == 11
                && observed.All(r =>
                    r.actualStarts >= 1
                    && r.ReadsClaimed >= 1
                    && r.actualStarts <= r.ReadsClaimed
                    && r.ReadsClaimed <= 3
                    && r.ReadAllowance == 3
                    && r.ReadUntil - r.FirstKnownAt == TimeSpan.FromMinutes(5)
                );
            gates["actualHttpStartsWithinPermitWindows"] =
                httpStarts
                    .GroupBy(s => s.Capacity.WindowUntil)
                    .All(window =>
                        window.Count(s => s.Operation != "read") <= 24
                        && window.Count(s => s.Operation == "read") <= 6
                    )
                && httpStarts.All(s =>
                    s.Capacity.ForegroundActive <= 1
                    && s.Capacity.RecoveryActive <= 1
                    && (
                        s.Operation == "read"
                            ? s.Capacity.RecoveryActive == 1
                            : s.Capacity.ForegroundActive == 1
                    )
                );
            gates["admittedBoundedOutcome"] =
                bounded && admitted.Length == host.SupplierCreates - createsBefore;
            gates["oneFastLostReplyWithRetainedEffect"] =
                loss.Length == 1
                && loss[0].Completed - loss[0].Started <= 1
                && admitted.Count(r => r.Id == loss[0].ReservationId) == 1;
            VerifyManifest(root, manifest);
            if (host.Diagnostic)
                await File.WriteAllTextAsync(
                    Path.Combine(output, "memory-diagnostic.json"),
                    JsonSerializer.Serialize(
                        new
                        {
                            diagnosticOnly = true,
                            originalAcceptanceRemainsFailed = true,
                            memoryStages = setup.GetProperty("memoryStages"),
                            initialFake = setup.GetProperty("initialFake"),
                            finalMemory = finalFacts.GetProperty("finalMemory"),
                            finalFake = finalFacts.GetProperty("finalFake"),
                            samples = memorySamples.OrderBy(sample => sample.At).ToArray(),
                            interpretation = "LastGc fields describe the last natural GC, not instantaneous live objects. Allocated bytes are approximate; IL bytes are not native code size. Fake JSON bytes exclude object/bucket/key overhead. Observer-until-memory-snapshot timing excludes subsequent reservation/inbox count queries and frame serialization, and includes concurrent work.",
                        },
                        Json
                    ),
                    ct
                );
            await File.WriteAllTextAsync(
                Path.Combine(output, "recovery-read-accounting.json"),
                JsonSerializer.Serialize(observed, Json),
                ct
            );
            await File.WriteAllTextAsync(
                Path.Combine(output, "operations.json"),
                JsonSerializer.Serialize(samples, Json),
                ct
            );
            await File.WriteAllTextAsync(
                Path.Combine(output, "resources.json"),
                JsonSerializer.Serialize(telemetry, Json),
                ct
            );
            await File.WriteAllTextAsync(
                Path.Combine(output, "supplier-starts.json"),
                JsonSerializer.Serialize(supplier, Json),
                ct
            );
            await File.WriteAllTextAsync(
                Path.Combine(output, "http-permit-windows.json"),
                JsonSerializer.Serialize(httpStarts, Json),
                ct
            );
            await File.WriteAllTextAsync(
                Path.Combine(output, "receipt.json"),
                JsonSerializer.Serialize(
                    new
                    {
                        startedAt = began,
                        fixedOfferedDurationSeconds = 360,
                        completedAtSeconds = elapsed,
                        offered = jobs.Count,
                        diagnosticOnly = host.Diagnostic,
                        originalAcceptanceRemainsFailed = host.Diagnostic,
                        completed = samples.Length,
                        phases,
                        resourcesByPhase = telemetry
                            .GroupBy(t => Phase(t.At))
                            .Select(group => new
                            {
                                phase = group.Key,
                                samples = group.Count(),
                                peakRssBytes = group.Max(t => t.Rss),
                                peakManagedHeapBytes = group.Max(t => t.ManagedHeap),
                                cpuMilliseconds = group.Last().CpuMilliseconds
                                    - group.First().CpuMilliseconds,
                                peakInboxLagMs = group.Max(t => t.OldestInboxLagMs),
                                peakDueWorkLagMs = group.Max(t => t.OldestWorkLagMs),
                                peakPendingInbox = group.Max(t => t.PendingInbox),
                                peakDueWork = group.Max(t => t.DueWork),
                                peakCacheEntries = group.Max(t => t.CacheEntries),
                                peakCacheBytes = group.Max(t => t.CacheBytes),
                            }),
                        usefulRecoveryAtSeconds = RecoveryAt(samples),
                        usefulRecoverySecondsAfterBurst = RecoveryAt(samples) is { } restored
                            ? restored - 240
                            : (double?)null,
                        finalEqualCardinality = new
                        {
                            first = telemetry.Where(t => t.At >= 350 && t.At < 355).ToArray(),
                            second = telemetry.Where(t => t.At >= 355 && t.At < 360).ToArray(),
                            expectedReservations = 38,
                            expectedRetainedInboxRows = 41,
                            interpretation = "Same retained row counts, no cleanup/reset. Original strict steady/baseline gates also apply.",
                        },
                        gates,
                        retainedBefore,
                        retainedAfter,
                        inboxBefore,
                        inboxAfter = finalFacts.GetProperty("inbox"),
                        seedCreates = createsBefore,
                        timedSupplierCreates = host.SupplierCreates - createsBefore,
                        admitted = admitted.Length,
                        primaryReadOffers = jobs.Count(j =>
                            j.Kind == "callback" && Phase(j.Due) != "Burst"
                        ),
                        actualTimedReadStarts = supplier.Count(s => s.operation == "read"),
                        permitAdmissions = telemetry.Last().Capacity.TotalStarts,
                        actualTimedHttpStarts = httpStarts.Count,
                        admittedPermitsWithoutHttpStart = telemetry.Last().Capacity.TotalStarts
                            - httpStarts.Count,
                        providerWindows = httpStarts
                            .GroupBy(s => s.Capacity.WindowUntil)
                            .Select(window => new
                            {
                                windowUntil = window.Key,
                                foregroundHttpStarts = window.Count(s => s.Operation != "read"),
                                recoveryHttpStarts = window.Count(s => s.Operation == "read"),
                                foregroundPermitAdmissions = window.Max(s =>
                                    s.Capacity.ForegroundStarts
                                ),
                                recoveryPermitAdmissions = window.Max(s =>
                                    s.Capacity.RecoveryStarts
                                ),
                            }),
                        readAccounting = "Normal primary reads follow the same2/min first-ID callbacks; derived recovery reads are not filtered from supplier starts/capacity/lag.",
                        readLagAccounting = "ElapsedSincePrimaryOffer includes intended30/120s delays for derived reads; actual overdue inbox/work lag is sampled separately in resources.json.",
                        sourceUnchanged = true,
                        hard2GiBEnvironmentProven = false,
                        passed = gates.Values.All(v => v),
                    },
                    Json
                ),
                ct
            );
            await File.WriteAllTextAsync(
                Path.Combine(output, "driver-resources.json"),
                JsonSerializer.Serialize(driverResources, Json),
                ct
            );
            Console.WriteLine(
                JsonSerializer.Serialize(
                    new
                    {
                        l1Completed = true,
                        offered = jobs.Count,
                        completed = samples.Length,
                        gates,
                        output,
                    }
                )
            );
            if (!gates.Values.All(v => v))
                throw new InvalidOperationException(
                    "The predeclared L1 gates failed; retain receipts and require review before any rerun."
                );
        }
        catch (Exception ex)
        {
            await timed.CancelAsync();
            await WriteCheckpointAsync();
            await File.WriteAllTextAsync(
                Path.Combine(output, "runner-failure.json"),
                JsonSerializer.Serialize(
                    new
                    {
                        passed = false,
                        exceptionType = ex.GetType().Name,
                        offered = jobs.Count,
                        recorded = operations.Count,
                        at = clock.Elapsed.TotalSeconds,
                        note = "Raw checkpoint and planned denominator retained. No automatic rerun.",
                    },
                    Json
                ),
                CancellationToken.None
            );
            throw;
        }
    }

    private static List<Job> Schedule()
    {
        var jobs = new List<Job>();
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
                    jobs.Add(new(start + offset + i * interval, kind));
            }
        }
        return jobs.OrderBy(j => j.Due).ToList();
    }

    private static string Phase(double at) =>
        at < 30 ? "Warmup"
        : at < 150 ? "Normal"
        : at < 210 ? "Degraded"
        : at < 240 ? "Burst"
        : at < 300 ? "RecoveryAdmission"
        : "RecoverySteady";

    private async Task WaitUntil(double seconds, CancellationToken ct)
    {
        var left = seconds - clock.Elapsed.TotalSeconds;
        if (left > 0)
            await Task.Delay(TimeSpan.FromSeconds(left), ct);
    }

    private async Task ExecuteAsync(Job job, CancellationToken ct)
    {
        await WaitUntil(job.Due, ct);
        var started = clock.Elapsed.TotalSeconds;
        int status = 0;
        string? kind = null,
            refusal = null;
        var useful = false;
        Guid? resource = null;
        try
        {
            HttpResponseMessage response;
            if (job.Kind == "search")
                response = await host.Client.PostAsJsonAsync(
                    "/api/hotels/search",
                    SearchBody(),
                    ct
                );
            else if (job.Kind == "rates")
            {
                var search = Volatile.Read(ref latestSearch);
                if (search is null)
                    throw new MissingPrerequisite();
                response = await host.Client.PostAsJsonAsync(
                    "/api/hotels/rates",
                    new { searchId = search.Id, selectionKey = search.SelectionKey },
                    ct
                );
            }
            else if (job.Kind == "prepare")
            {
                var rates = Volatile.Read(ref latestRates);
                if (rates is null)
                    throw new MissingPrerequisite();
                var index = Interlocked.Increment(ref draftIndex);
                if (index >= drafts.Count)
                    throw new MissingPrerequisite();
                response = await SendOwned(
                    HttpMethod.Put,
                    $"/api/hotels/reservations/{drafts[index]}/quote",
                    new
                    {
                        ratesId = rates.Id,
                        selectionKey = rates.SelectionKey,
                        expectedRevision = 1,
                    },
                    ct
                );
            }
            else if (job.Kind == "create")
            {
                if (!prepared.TryDequeue(out var quote))
                    throw new MissingPrerequisite();
                resource = quote.Id;
                response = await SendOwned(
                    HttpMethod.Post,
                    $"/api/hotels/reservations/{quote.Id}/purchase",
                    new HotelPurchase(Guid.NewGuid(), quote.Revision, true, Party()),
                    ct
                );
            }
            else if (job.Kind == "status")
                response = await SendOwned(
                    HttpMethod.Get,
                    $"/api/hotels/reservations/{statusStock[0].Id}",
                    null,
                    ct
                );
            else
            {
                var stock =
                    Phase(job.Due) == "Burst"
                        ? statusStock[Interlocked.Increment(ref eventIndex) % statusStock.Count]
                        : observationStock[Interlocked.Increment(ref observationIndex)];
                if (Phase(job.Due) != "Burst")
                    firstIdentityOffers.TryAdd(stock.Id, job.Due);
                response = await HotelsHostTests.SendWebhook(
                    host.Client,
                    HotelsHostTests.Event(
                        "evt_l1_" + Interlocked.Increment(ref eventIndex),
                        "stays.booking.created",
                        new JsonObject
                        {
                            ["quote_id"] = stock.QuoteId,
                            ["booking_id"] = stock.BookingId,
                        }
                    ),
                    ct
                );
            }
            using (response)
            {
                status = (int)response.StatusCode;
                if (!response.IsSuccessStatusCode)
                    refusal = "HttpRefusal";
                else if (job.Kind == "callback")
                    useful = true;
                else
                {
                    var json = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct))!;
                    if (job.Kind is "search" or "rates")
                    {
                        kind = json["result"]!["kind"]!.GetValue<string>();
                        useful =
                            (kind is "Complete" or "Limited")
                            && json["result"]!["items"]!.AsArray().Count > 0;
                        if (useful)
                        {
                            var next = new Catalog(
                                json["id"]!.GetValue<Guid>(),
                                json["result"]!["items"]![0]!["selectionKey"]!.GetValue<string>()
                            );
                            if (job.Kind == "search")
                                Volatile.Write(ref latestSearch, next);
                            else
                                Volatile.Write(ref latestRates, next);
                        }
                        else
                            refusal = json["result"]!["reason"]?.GetValue<string>() ?? "NotUseful";
                    }
                    else if (job.Kind == "prepare")
                    {
                        useful = json["outcome"]!.GetValue<string>() == "Quoted";
                        if (useful)
                            prepared.Enqueue(
                                new(
                                    json["id"]!.GetValue<Guid>(),
                                    json["quoteRevision"]!.GetValue<int>()
                                )
                            );
                    }
                    else
                    {
                        kind = json["outcome"]!.GetValue<string>();
                        useful = kind == "Confirmed";
                    }
                }
            }
        }
        catch (MissingPrerequisite)
        {
            refusal = "PrerequisiteUnavailable";
        }
        catch (Exception ex)
            when (ex
                    is HttpRequestException
                        or JsonException
                        or InvalidOperationException
                        or TaskCanceledException
            )
        {
            refusal = "RequestOrDecodeFailure";
        }
        operations.Add(
            new(
                Phase(job.Due),
                job.Kind,
                job.Due,
                started,
                clock.Elapsed.TotalSeconds,
                status,
                kind,
                useful,
                refusal,
                resource
            )
        );
    }

    private sealed class MissingPrerequisite : Exception
    {
        public MissingPrerequisite() { }

        public MissingPrerequisite(string? message)
            : base(message) { }

        public MissingPrerequisite(string? message, Exception? innerException)
            : base(message, innerException) { }
    }

    private async Task<HttpResponseMessage> SendOwned(
        HttpMethod method,
        string path,
        object? body,
        CancellationToken ct
    )
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new("Bearer", "fictional-hotels-owner");
        if (body is not null)
            request.Content = JsonContent.Create(body);
        return await host.Client.SendAsync(request, ct);
    }

    private static HotelParty Party() =>
        new(
            [new("Fictional", "Traveller")],
            "fictional@example.invalid",
            "+44000000000",
            true,
            true
        );

    private static object SearchBody()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return new
        {
            areaKey = "london",
            checkIn = today.AddDays(7),
            checkOut = today.AddDays(9),
            adults = 1,
        };
    }

    private async Task SampleAsync(CancellationToken ct)
    {
        for (var second = 0; second <= 360; second++)
        {
            await WaitUntil(second, ct);
            if (second % 10 == 0)
                await WriteCheckpointAsync();
            var sample = await host.CommandAsync("sample", null, ct);
            if (sample.GetProperty("pid").GetInt32() != host.Process.Id)
                throw new InvalidOperationException("Sample belongs to a different process.");
            host.Process.Refresh();
            resources.Add(
                sample.GetProperty("metrics").Deserialize<Resources>(HotelsL1Frames.Json)! with
                {
                    Rss = host.Process.WorkingSet64,
                    CpuMilliseconds = host.Process.TotalProcessorTime.TotalMilliseconds,
                }
            );
            if (host.Diagnostic)
                memorySamples.Add(
                    new(
                        sample.GetProperty("metrics").GetProperty("at").GetDouble(),
                        L1ProcessMemory.Capture(host.Process),
                        sample
                            .GetProperty("memoryDiagnostic")
                            .Deserialize<L1SampleMemory>(HotelsL1Frames.Json)!
                    )
                );
            foreach (
                var start in sample
                    .GetProperty("starts")
                    .Deserialize<HttpStart[]>(HotelsL1Frames.Json)!
            )
                httpStarts.Enqueue(start);
            foreach (
                var read in sample.GetProperty("reads").Deserialize<L1Read[]>(HotelsL1Frames.Json)!
            )
                readCompletions.Enqueue(read);
            using var driver = Process.GetCurrentProcess();
            driver.Refresh();
            driverResources.Add(
                new
                {
                    at = clock.Elapsed.TotalSeconds,
                    pid = Environment.ProcessId,
                    rss = driver.WorkingSet64,
                    managedHeap = GC.GetTotalMemory(false),
                    cpuMilliseconds = driver.TotalProcessorTime.TotalMilliseconds,
                }
            );
        }
    }

    private async Task WriteCheckpointAsync()
    {
        await checkpointGate.WaitAsync();
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(output, "checkpoint.json"),
                JsonSerializer.Serialize(
                    new
                    {
                        at = clock.Elapsed.TotalSeconds,
                        operations = operations.OrderBy(o => o.Due).ToArray(),
                        resources = resources.OrderBy(r => r.At).ToArray(),
                        httpStarts = httpStarts.ToArray(),
                        readCompletions = readCompletions.ToArray(),
                        finalFacts = finalFacts.ValueKind == JsonValueKind.Undefined
                            ? (JsonElement?)null
                            : finalFacts,
                        supplierStarts = host.SupplierStarts,
                        supplierCreates = host.SupplierCreates,
                        driverResources = driverResources.ToArray(),
                        measuredHostPid = host.Process.Id,
                        driverPid = Environment.ProcessId,
                        memoryDiagnostics = host.Diagnostic ? memorySamples.ToArray() : null,
                    },
                    Json
                ),
                CancellationToken.None
            );
        }
        finally
        {
            checkpointGate.Release();
        }
    }

    private static double P95(IEnumerable<double> values)
    {
        var sorted = values.Order().ToArray();
        return sorted.Length == 0 ? 0 : sorted[(int)Math.Ceiling(sorted.Length * .95) - 1];
    }

    private static object Summary(string phase, Operation[] samples) =>
        new
        {
            phase,
            offeredSeconds = phase is "Warmup" or "Burst" ? 30
            : phase == "Normal" ? 120
            : 60,
            offered = samples.Length,
            completed = samples.Length,
            useful = samples.Count(s => s.Useful),
            usefulPerSecond = samples.Count(s => s.Useful)
                / (
                    phase is "Warmup" or "Burst" ? 30d
                    : phase == "Normal" ? 120d
                    : 60d
                ),
            http429 = samples.Count(s => s.Status == 429),
            http5xx = samples.Count(s => s.Status >= 500),
            limited = samples.Count(s => s.Result == "Limited"),
            logicalRefusals = samples.Count(s => !s.Useful),
            byOperation = samples
                .GroupBy(s => s.Kind)
                .Select(group => new
                {
                    kind = group.Key,
                    offered = group.Count(),
                    useful = group.Count(s => s.Useful),
                    p95Ms = P95(group.Select(s => (s.Completed - s.Due) * 1000)),
                    p95DispatchLagMs = P95(group.Select(s => (s.Started - s.Due) * 1000)),
                }),
            admissionP95Ms = P95(samples.Select(s => (s.Completed - s.Due) * 1000)),
        };

    private static Dictionary<string, bool> Gates(
        Operation[] samples,
        Resources[] telemetry,
        int before,
        int after
    )
    {
        bool Success(string phase, string kind, double latency)
        {
            var subset = samples.Where(s => s.Phase == phase && s.Kind == kind).ToArray();
            return subset.Length > 0
                && subset.Count(s => s.Useful) >= subset.Length * .95
                && P95(subset.Select(s => (s.Completed - s.Due) * 1000)) <= latency;
        }
        var normal = new[] { "Normal", "RecoverySteady" };
        var baseline = telemetry.Last(t => t.At < 30);
        var last = telemetry.Last();
        var recoveryAt = RecoveryAt(samples);
        var firstSteadyRss = telemetry
            .Where(t => t.At >= 300 && t.At < 330)
            .Average(t => (double)t.Rss);
        var lastSteadyRss = telemetry.Where(t => t.At >= 330).Average(t => (double)t.Rss);
        var stableFirst = telemetry.Where(t => t.At >= 350 && t.At < 355).ToArray();
        var stableSecond = telemetry.Where(t => t.At >= 355 && t.At < 360).ToArray();
        return new()
        {
            ["offeredAndCompleted"] = samples.Length == 3152,
            ["normalAndSteadyUsefulSearch"] = normal.All(p => Success(p, "search", 1000)),
            ["normalAndSteadyPurchase"] = normal.All(p =>
                Success(p, "rates", 20000)
                && Success(p, "prepare", 20000)
                && Success(p, "create", 150000)
            ),
            ["normalAndSteadyStatusAckLatency"] = normal.All(p =>
                Success(p, "status", 500) && Success(p, "callback", 500)
            ),
            ["burstStatusAck"] =
                Success("Burst", "status", 1000) && Success("Burst", "callback", 1000),
            ["capacityLanesAndActive"] = telemetry.All(t =>
                t.Capacity.ForegroundStarts <= 24
                && t.Capacity.RecoveryStarts <= 6
                && t.Capacity.ForegroundActive <= 1
                && t.Capacity.RecoveryActive <= 1
                && t.Capacity.MaxActive <= 2
            ),
            ["cacheBounds"] = telemetry.All(t => t.CacheEntries <= 128 && t.CacheBytes <= 16777216),
            ["workingSetCeiling"] = telemetry.All(t => t.Rss <= 1073741824),
            ["fixedReservationCardinality"] =
                before == after && telemetry.All(t => t.Reservations == before),
            ["workingSetRecovery"] =
                last.Rss <= baseline.Rss + 134217728 && lastSteadyRss <= firstSteadyRss,
            ["finalSameRetainedRowsMemoryStable"] =
                stableFirst.Length >= 5
                && stableSecond.Length >= 5
                && stableFirst
                    .Concat(stableSecond)
                    .Select(t => (t.Reservations, t.RetainedInboxRows))
                    .Distinct()
                    .Count() == 1
                && stableFirst
                    .Concat(stableSecond)
                    .All(t =>
                        t.Reservations == before
                        && t.CacheEntries <= 128
                        && t.CacheBytes <= 16777216
                    )
                && stableSecond.Average(t => (double)t.Rss)
                    <= stableFirst.Average(t => (double)t.Rss),
            ["recoveryWithin75Seconds"] = recoveryAt is <= 315,
            ["dueWorkAndInboxSettled"] = last.PendingInbox == 0 && last.DueWork == 0,
        };
    }

    // Earliest complete 60s offered window regaining the unchanged normal scenario gates.
    private static double? RecoveryAt(Operation[] samples)
    {
        foreach (var end in Enumerable.Range(300, 16))
            if (
                new[] { "search", "rates", "prepare", "create" }.All(kind =>
                {
                    var offered = samples
                        .Where(s => s.Due >= end - 60 && s.Due < end && s.Kind == kind)
                        .ToArray();
                    return offered.Length > 0
                        && offered.Count(s => s.Useful && s.Completed <= end)
                            >= offered.Length * .95
                        && (
                            kind != "search"
                            || P95(offered.Select(s => (s.Completed - s.Due) * 1000)) <= 1000
                        );
                })
            )
                return end;
        return null;
    }

    internal static void VerifyManifestFile(string root, string path) =>
        VerifyManifest(
            root,
            JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))
                ?? throw new InvalidOperationException("Manifest is missing.")
        );

    private static void VerifyManifest(string root, Dictionary<string, string> manifest)
    {
        var git = new ProcessStartInfo("git")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in new[] { "ls-files", "-c", "-o", "--exclude-standard" })
            git.ArgumentList.Add(arg);
        using var child =
            Process.Start(git)
            ?? throw new InvalidOperationException("Manifest inventory unavailable.");
        var paths = child
            .StandardOutput.ReadToEnd()
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Where(p =>
                !new[] { "TestResults/", "docs/", "openspec/", ".agents/", ".codex/" }.Any(prefix =>
                    p.StartsWith(prefix, StringComparison.Ordinal)
                )
            )
            .ToHashSet(StringComparer.Ordinal);
        child.WaitForExit();
        paths.Add("TestResults/hotels-m1/l1-profile.md");
        paths.Add("TestResults/hotels-m1/l1-memory-diagnostic-profile.md");
        if (child.ExitCode != 0 || !paths.SetEquals(manifest.Keys))
            throw new InvalidOperationException(
                "Source manifest is incomplete or file inventory changed."
            );
        foreach (var (relative, hash) in manifest)
        {
            var path = Path.GetFullPath(Path.Combine(root, relative));
            if (
                !path.StartsWith(
                    Path.GetFullPath(root) + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase
                )
                || !string.Equals(
                    Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))),
                    hash,
                    StringComparison.OrdinalIgnoreCase
                )
            )
                throw new InvalidOperationException("Source manifest changed.");
        }
    }
}
