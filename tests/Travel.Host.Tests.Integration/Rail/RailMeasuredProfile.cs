using System.Collections;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ErrorOr;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Travel.Host.Tests.Integration.Hotels;
using Travel.Modules.Rail.Application;
using Travel.Modules.Rail.Core;
using Travel.Modules.Rail.Infrastructure;

namespace Travel.Host.Tests.Integration.Rail;

// Deliberately NOT a Fact/Theory: ordinary discovery cannot run measured load.
public static class RailMeasuredProfile
{
    public static Stream OpenCatalogueFixture(Action<int> received) =>
        new CatalogueStream(received);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private static T Field<T>(object owner, string name) =>
        (T)(
            owner
                .GetType()
                .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(owner)
            ?? throw new InvalidOperationException(
                "Measured retained-state binding changed: " + name
            )
        );

    private static long WorkingSet()
    {
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        return process.WorkingSet64;
    }

    private static long LifetimePeak()
    {
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        return process.PeakWorkingSet64;
    }

    public static async Task<int> RunAsync(
        string profilePath,
        string receiptDirectory,
        CancellationToken ct
    )
    {
        if (Directory.Exists(receiptDirectory))
            throw new InvalidOperationException("Existing attempts cannot be reused.");
        try
        {
            return await RunCoreAsync(profilePath, receiptDirectory, ct);
        }
        catch (Exception error)
        {
            Directory.CreateDirectory(receiptDirectory);
            await using var output = new FileStream(
                Path.Combine(receiptDirectory, "attempt-error.json"),
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.Read
            );
            await JsonSerializer.SerializeAsync(
                output,
                new
                {
                    passed = false,
                    outcome = error is OperationCanceledException ? "cancelled" : "failed",
                    errorType = error.GetType().FullName,
                    profilePath,
                    sourceIdentityPath = receiptDirectory + ".source.json",
                    automaticRetry = false,
                },
                Json,
                CancellationToken.None
            );
            return 1;
        }
    }

    private static async Task<int> RunCoreAsync(
        string profilePath,
        string receiptDirectory,
        CancellationToken ct
    )
    {
        if (Directory.Exists(receiptDirectory))
            throw new InvalidOperationException(
                "Use a new receipt directory; original attempts must be preserved."
            );
        var profileBytes = await File.ReadAllBytesAsync(profilePath, ct);
        using var profile = JsonDocument.Parse(profileBytes);
        var p = profile.RootElement;
        if (
            p.GetProperty("id").GetString() != "rail-m1-host-memory-v1"
            || p.GetProperty("cycles").GetInt32() != 3
            || p.GetProperty("usefulUncachedPerCycle").GetInt32() != 1000
            || p.GetProperty("catalogueStations").GetInt32() != 100000
            || p.GetProperty("catalogueTargetBytes").GetInt64() != 41943040
            || p.GetProperty("baselineSamples").GetInt32() != 5
            || p.GetProperty("baselineWindowSeconds").GetInt32() != 30
            || p.GetProperty("recoverySeconds").GetInt32() != 120
            || p.GetProperty("p95Milliseconds").GetInt32() != 1000
            || p.GetProperty("recoveryExtraBytes").GetInt64() != 16777216
            || p.GetProperty("recoveryExtraFraction").GetDecimal() != .15m
        )
            throw new InvalidOperationException(
                "Profile thresholds changed; obtain a new reviewed profile instead of silently changing an attempt."
            );
        var state = p.GetProperty("retainedState");
        if (
            !p.GetProperty("admittedProfiles")
                .EnumerateArray()
                .Select(x => x.GetInt32())
                .SequenceEqual(new[] { 1, 2 })
            || p.GetProperty("cachePressureEntries").GetInt32() != 128
            || p.GetProperty("baselineStatistic").GetString() != "median"
            || !p.GetProperty("fictional").GetBoolean()
            || !p.GetProperty("actualHost").GetBoolean()
            || !p.GetProperty("requiresSeparateAuthorization").GetBoolean()
            || !p.GetProperty("saturationSeparate").GetBoolean()
            || p.GetProperty("automaticRetries").GetInt32() != 0
            || state.GetProperty("bundles").GetInt32() != 1
            || state.GetProperty("searchEntries").GetInt32() != 0
            || state.GetProperty("activeChildren").GetInt32() != 0
            || state.GetProperty("referenceLoaders").GetInt32() != 0
            || state.GetProperty("availableSearchPermits").GetInt32() != 2
        )
            throw new InvalidOperationException("Reviewed profile metadata changed.");
        Directory.CreateDirectory(receiptDirectory);
        var sourceIdentity = JsonSerializer.Deserialize<JsonElement>(
            await File.ReadAllTextAsync(receiptDirectory + ".source.json", ct)
        );
        await File.WriteAllTextAsync(
            Path.Combine(receiptDirectory, "source-identity.json"),
            JsonSerializer.Serialize(sourceIdentity, Json),
            ct
        );
        var started = TimeProvider.System.GetUtcNow();
        var clock = new FakeTimeProvider(started);
        using var wire = new Wire();
        await using var host = new HotelsHostFixture
        {
            Clock = clock,
            ConfigureTestServices = services =>
            {
                services.Configure<RailOptions>(options =>
                {
                    options.Enabled = true;
                    options.Fictional = true;
                });
                services.RemoveAll<IRailReferenceLoader>();
                services.RemoveAll<IRailScheduleProvider>();
                services.AddSingleton<IRailReferenceLoader>(s => new Reference(
                    new YandexRailClient(
                        new HttpClient(wire, false),
                        s.GetRequiredService<RailRuntime>(),
                        s.GetRequiredService<RailQuotaLedger>(),
                        clock
                    ),
                    clock
                ));
                services.AddSingleton<IRailScheduleProvider>(s => new YandexRailScheduleProvider(
                    new YandexRailClient(
                        new HttpClient(wire, false),
                        s.GetRequiredService<RailRuntime>(),
                        s.GetRequiredService<RailQuotaLedger>(),
                        clock
                    ),
                    clock
                ));
            },
        };
        // First side effect requiring the separately authorized Host/DB stage.
        await host.StartAsync(ct);
        var cache = (RailSearchCache)host.Services.GetRequiredService<IRailSearchCache>();
        var references = host.Services.GetRequiredService<RailReferenceService>();
        var searches = host.Services.GetRequiredService<RailSearchService>();
        async Task<RailRequest> Load()
        {
            using var response = await host.Client.GetAsync("/api/rail/stations?q=Учебная", ct);
            response.EnsureSuccessStatusCode();
            using var body = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(ct));
            var generation =
                body.RootElement.GetProperty("catalogueGeneration").GetString()
                ?? throw new InvalidOperationException("No measured catalogue generation");
            if (wire.CatalogueBytes != 41943040)
                throw new InvalidOperationException(
                    "Catalogue decoded dataset differs from reviewed40MiB profile."
                );
            return new(
                StationRef.Create("yandex-rasp", "s1").Value,
                StationRef.Create("yandex-rasp", "s2").Value,
                generation,
                clock
                    .GetUtcNow()
                    .ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                "all"
            );
        }
        object State()
        {
            var bundles = typeof(RailReferenceService)
                .GetField("bundle", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(references)
                is null
                ? 0
                : 1;
            var loaders = typeof(RailReferenceService)
                .GetField("flight", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(references)
                is null
                ? 0
                : 1;
            var entries = Field<IDictionary>(cache, "entries").Count;
            var permits = Field<SemaphoreSlim>(searches, "admission").CurrentCount;
            if (bundles != 1 || entries != 0 || wire.Active != 0 || loaders != 0 || permits != 2)
                throw new InvalidOperationException(
                    "Warm baseline/recovery business states differ."
                );
            return new
            {
                bundles,
                searchEntries = entries,
                activeChildren = wire.Active,
                referenceLoaders = loaders,
                availableSearchPermits = permits,
            };
        }
        var request = await Load();
        using (var warm = await host.Client.PostAsJsonAsync("/api/rail/search", request, ct))
        {
            warm.EnsureSuccessStatusCode();
            using var body = JsonDocument.Parse(await warm.Content.ReadAsByteArrayAsync(ct));
            if (
                body.RootElement.GetProperty("kind").GetString() != "complete"
                || body.RootElement.GetProperty("items").GetArrayLength() != 2
            )
                throw new InvalidOperationException("Warmup was not complete/useful.");
        }
        cache.Clear();
        var warmState = State();
        async Task<(object Receipt, bool Passed)> CancellationPressure(RailRequest current)
        {
            cache.Clear();
            wire.Hold = true;
            using var cancel = new CancellationTokenSource();
            var beforeCalls = wire.SearchCalls;
            var first = searches.SearchAsync(current, cancel.Token);
            var second = searches.SearchAsync(current, cancel.Token);
            try
            {
                var refusal = await searches.SearchAsync(current, ct);
                cancel.Cancel();
                var cancelled = 0;
                foreach (var task in new[] { first, second })
                    try
                    {
                        await task;
                    }
                    catch (OperationCanceledException)
                    {
                        cancelled++;
                    }
                var heldRefusal = await searches.SearchAsync(current, ct);
                var ok =
                    refusal.IsError
                    && refusal.FirstError.Code == "Rail.Capacity"
                    && heldRefusal.IsError
                    && heldRefusal.FirstError.Code == "Rail.Capacity"
                    && cancelled == 2
                    && wire.Active == 4
                    && wire.SearchCalls - beforeCalls == 4;
                return (
                    new
                    {
                        attempts = 4,
                        useful = 0,
                        cancellations = cancelled,
                        refusals = 2,
                        upstreamCalls = wire.SearchCalls - beforeCalls,
                        passed = ok,
                    },
                    ok
                );
            }
            finally
            {
                cancel.Cancel();
                wire.Release();
                for (
                    var i = 0;
                    i < 100 && Field<SemaphoreSlim>(searches, "admission").CurrentCount != 2;
                    i++
                )
                    await Task.Delay(10, CancellationToken.None);
            }
        }
        var baselineSamples = new List<long>();
        for (var i = 0; i < 5; i++)
        {
            if (i > 0)
                await Task.Delay(TimeSpan.FromSeconds(7.5), ct);
            baselineSamples.Add(WorkingSet());
        }
        var baseline = baselineSamples.Order().ElementAt(2);
        var threshold = baseline + Math.Max(16777216L, (long)(baseline * .15));
        await File.WriteAllTextAsync(
            Path.Combine(receiptDirectory, "baseline.json"),
            JsonSerializer.Serialize(
                new
                {
                    started,
                    sourceIdentity,
                    profileSha256 = Convert.ToHexStringLower(SHA256.HashData(profileBytes)),
                    baselineSamples,
                    baseline,
                    threshold,
                    warmState,
                },
                Json
            ),
            ct
        );
        var passed = true;
        var receipts = new List<object>();
        foreach (var concurrency in new[] { 1, 2 })
            for (var cycle = 1; cycle <= 3; cycle++)
            {
                var latencies = new List<double>();
                var attempts = 0;
                var completed = 0;
                var cancellations = 0;
                var inFlight = 0;
                var useful = 0;
                var failures = 0;
                var startCalls = wire.SearchCalls;
                var startReferences = wire.ReferenceCalls;
                var interrupted = true;
                var phase = "cold";
                var outcome = "in-progress";
                object? saturationReceipt = null;
                try
                {
                    clock.Advance(TimeSpan.FromMinutes(31));
                    request = await Load();
                    cache.Clear();
                    State();
                    long peak = WorkingSet();
                    phase = "warm";
                    for (var offset = 0; offset < 1000; offset += concurrency)
                    {
                        var batch = Enumerable
                            .Range(offset, Math.Min(concurrency, 1000 - offset))
                            .Select(async index =>
                            {
                                var query = request with
                                {
                                    To = StationRef.Create("yandex-rasp", "s" + (index + 2)).Value,
                                };
                                var watch = Stopwatch.StartNew();
                                lock (latencies)
                                {
                                    attempts++;
                                    inFlight++;
                                }
                                try
                                {
                                    using var response = await host.Client.PostAsJsonAsync(
                                        "/api/rail/search",
                                        query,
                                        ct
                                    );
                                    using var data = JsonDocument.Parse(
                                        await response.Content.ReadAsByteArrayAsync(ct)
                                    );
                                    var valid =
                                        response.StatusCode == HttpStatusCode.OK
                                        && data.RootElement.GetProperty("kind").GetString()
                                            == "complete"
                                        && data.RootElement.GetProperty("items").GetArrayLength()
                                            == 2;
                                    lock (latencies)
                                    {
                                        if (valid)
                                            useful++;
                                        else
                                            failures++;
                                    }
                                }
                                catch (OperationCanceledException)
                                {
                                    lock (latencies)
                                        cancellations++;
                                    throw;
                                }
                                catch (Exception error)
                                    when (error is not OperationCanceledException)
                                {
                                    lock (latencies)
                                    {
                                        failures++;
                                    }
                                }
                                finally
                                {
                                    lock (latencies)
                                    {
                                        completed++;
                                        inFlight--;
                                        latencies.Add(watch.Elapsed.TotalMilliseconds);
                                    }
                                }
                            });
                        await Task.WhenAll(batch);
                        peak = Math.Max(peak, WorkingSet());
                    }
                    var cachePressure = Field<IDictionary>(cache, "entries").Count;
                    if (cachePressure != 128)
                        passed = false;
                    var uncached = wire.SearchCalls - startCalls == 2000;
                    phase = "cancellation-expiry";
                    var saturation = await CancellationPressure(request);
                    saturationReceipt = saturation.Receipt;
                    clock.Advance(TimeSpan.FromMinutes(31));
                    request = await Load();
                    cache.Clear();
                    State();
                    phase = "recovery";
                    await Task.Delay(TimeSpan.FromSeconds(120), ct);
                    var recovered = WorkingSet();
                    var recoveryState = State();
                    var sorted = latencies.Order().ToArray();
                    var p50 = sorted[499];
                    var p95 = sorted[949];
                    var ok =
                        attempts == 1000
                        && useful == 1000
                        && failures == 0
                        && uncached
                        && cachePressure == 128
                        && saturation.Passed
                        && p95 <= 1000
                        && recovered <= threshold;
                    passed &= ok;
                    var receipt = new
                    {
                        sourceIdentity,
                        concurrency,
                        cycle,
                        attempts,
                        useful,
                        failures,
                        refusals = 0,
                        cancellations,
                        completed,
                        inFlight,
                        p50,
                        p95,
                        sampledWarmPeak = peak,
                        processLifetimePeakWorkingSet = LifetimePeak(),
                        peakScope = "process lifetime includes cold catalogue and previous cycles",
                        recovered,
                        threshold,
                        cachePressure,
                        uncached,
                        upstreamSearchDispatches = wire.SearchCalls - startCalls,
                        upstreamReferenceDispatches = wire.ReferenceCalls - startReferences,
                        workingSetRecovery = recovered <= threshold,
                        performance = p95 <= 1000,
                        usefulDenominator = attempts == 1000 && useful == 1000 && failures == 0,
                        saturation = saturation.Receipt,
                        recoveryState,
                        passed = ok,
                    };
                    receipts.Add(receipt);
                    phase = "receipt";
                    await File.WriteAllTextAsync(
                        Path.Combine(receiptDirectory, $"profile-{concurrency}-cycle-{cycle}.json"),
                        JsonSerializer.Serialize(receipt, Json),
                        ct
                    );
                    interrupted = false;
                    phase = "complete";
                    outcome = "complete";
                }
                catch (OperationCanceledException)
                {
                    outcome = "cancelled";
                    throw;
                }
                catch (Exception)
                {
                    outcome = "failed";
                    throw;
                }
                finally
                {
                    await using var output = new FileStream(
                        Path.Combine(
                            receiptDirectory,
                            $"profile-{concurrency}-cycle-{cycle}-denominator.json"
                        ),
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.Read
                    );
                    await JsonSerializer.SerializeAsync(
                        output,
                        new
                        {
                            sourceIdentity,
                            concurrency,
                            cycle,
                            phase,
                            outcome,
                            interrupted,
                            attempts,
                            completed,
                            inFlight,
                            useful,
                            failures,
                            cancellations,
                            latencySamples = latencies.ToArray(),
                            upstreamSearchDispatches = wire.SearchCalls - startCalls,
                            upstreamReferenceDispatches = wire.ReferenceCalls - startReferences,
                            saturation = saturationReceipt,
                            fullUsefulDenominator = !interrupted
                                && attempts == 1000
                                && useful == 1000
                                && failures == 0
                                && cancellations == 0,
                        },
                        Json,
                        CancellationToken.None
                    );
                }
            }
        await File.WriteAllTextAsync(
            Path.Combine(receiptDirectory, "verdict.json"),
            JsonSerializer.Serialize(
                new
                {
                    passed,
                    sourceIdentity,
                    profileSha256 = Convert.ToHexStringLower(SHA256.HashData(profileBytes)),
                    baseline,
                    threshold,
                    receipts,
                    realSupplier = false,
                    HotelsWaiver = false,
                },
                Json
            ),
            ct
        );
        return passed ? 0 : 1;
    }

    private sealed class Reference(YandexRailClient client, TimeProvider clock)
        : IRailReferenceLoader
    {
        public async Task<ErrorOr<ReferenceData>> LoadAsync(
            RailProfile profile,
            CancellationToken ct
        )
        {
            var result = await new YandexRailReferenceLoader(client, clock).LoadAsync(profile, ct);
            return result.IsError
                ? result.Errors
                : result.Value with
                {
                    Attribution = result.Value.Attribution with
                    {
                        FrameUrl = "/rail-fictional-banner.html",
                    },
                };
        }
    }

    private sealed class Wire : HttpMessageHandler
    {
        public int Active;
        public int SearchCalls;
        public int ReferenceCalls;
        public long CatalogueBytes;
        public bool Hold;
        private readonly List<(
            TaskCompletionSource<HttpResponseMessage> Task,
            HttpResponseMessage Response
        )> held = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct
        )
        {
            if (request.RequestUri!.Host != "api.rasp.yandex-net.ru")
                throw new InvalidOperationException("Measured external destination denied.");
            var path = request.RequestUri.AbsolutePath;
            if (path is "/v3.0/stations_list/" or "/v3.0/copyright/")
                Interlocked.Increment(ref ReferenceCalls);
            if (path == "/v3.0/stations_list/")
            {
                CatalogueBytes = 0;
                return Task.FromResult(
                    new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new CatalogueContent(count =>
                            Interlocked.Add(ref CatalogueBytes, count)
                        ),
                    }
                );
            }
            object body;
            if (path == "/v3.0/copyright/")
                body = new
                {
                    copyright = new
                    {
                        logo_vm = "<iframe src=\"//yandex.st/rasp/media/apicc/copyright_vert_mono.html\" width=\"240\" height=\"130\"></iframe>",
                        text = "Fictional measured source",
                        url = "https://rasp.yandex.ru/",
                    },
                };
            else if (path == "/v3.0/search/")
            {
                Interlocked.Increment(ref SearchCalls);
                var q = request
                    .RequestUri.Query.TrimStart('?')
                    .Split('&')
                    .Select(x => x.Split('=', 2))
                    .ToDictionary(
                        x => x[0],
                        x => Uri.UnescapeDataString(x[1]),
                        StringComparer.Ordinal
                    );
                var from = new { code = q["from"], type = "station" };
                var to = new { code = q["to"], type = "station" };
                var date = q["date"];
                var mode = q["transport_types"];
                body = new
                {
                    search = new
                    {
                        from,
                        to,
                        date,
                    },
                    pagination = new
                    {
                        offset = 0,
                        limit = 50,
                        total = 1,
                    },
                    segments = new[]
                    {
                        new
                        {
                            from,
                            to,
                            thread = new { uid = "measured-" + mode, transport_type = mode },
                            has_transfers = false,
                            departure = date + "T10:00:00+03:00",
                            arrival = date + "T12:00:00+03:00",
                            duration = 7200,
                        },
                    },
                };
            }
            else
                throw new InvalidOperationException("Measured method denied.");
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(body)),
            };
            if (path == "/v3.0/search/" && Hold)
            {
                Interlocked.Increment(ref Active);
                var task = new TaskCompletionSource<HttpResponseMessage>(
                    TaskCreationOptions.RunContinuationsAsynchronously
                );
                lock (held)
                    held.Add((task, response));
                return Complete(task.Task);
            }
            return Task.FromResult(response);
        }

        private async Task<HttpResponseMessage> Complete(Task<HttpResponseMessage> task)
        {
            try
            {
                return await task;
            }
            finally
            {
                Interlocked.Decrement(ref Active);
            }
        }

        public void Release()
        {
            Hold = false;
            lock (held)
            {
                foreach (var item in held)
                    item.Task.TrySetResult(item.Response);
                held.Clear();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                Release();
            base.Dispose(disposing);
        }
    }

    private sealed class CatalogueContent(Action<int> received) : HttpContent
    {
        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }

        protected override Task<Stream> CreateContentReadStreamAsync() =>
            Task.FromResult<Stream>(new CatalogueStream(received));

        protected override async Task SerializeToStreamAsync(
            Stream stream,
            TransportContext? context
        )
        {
            await using var source = new CatalogueStream(received);
            await source.CopyToAsync(stream);
        }
    }

    private sealed class CatalogueStream(Action<int> received) : Stream
    {
        private const string Prefix =
            "{\"countries\":[{\"codes\":{\"yandex_code\":\"l1\"},\"regions\":[{\"settlements\":[{\"stations\":[{\"codes\":{\"yandex_code\":\"s900001\"},\"title\":\"Foreign\",\"transport_type\":\"train\",\"station_type\":\"station\"}]}]}]},{\"codes\":{\"yandex_code\":\"l225\"},\"regions\":[{\"title\":\"RF\",\"settlements\":[{\"title\":\"Fictional\",\"stations\":[";
        private const string Suffix = "]}]}]}]}";
        private static readonly long ItemBudget =
            41943040
            - Encoding.UTF8.GetByteCount(Prefix)
            - Encoding.UTF8.GetByteCount(Suffix)
            - 99999;
        private int station;
        private byte[] current = Encoding.UTF8.GetBytes(Prefix);
        private int position;
        private bool ended;

        public override int Read(byte[] buffer, int offset, int count) =>
            Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> target)
        {
            var written = 0;
            while (written < target.Length)
            {
                if (position == current.Length)
                {
                    if (ended)
                        break;
                    if (station < 100000)
                    {
                        station++;
                        var empty = JsonSerializer.Serialize(
                            new
                            {
                                codes = new { yandex_code = "s" + station },
                                title = "Учебная " + station,
                                transport_type = "train",
                                station_type = "station",
                                padding = "",
                            }
                        );
                        var size = ItemBudget / 100000 + (station <= ItemBudget % 100000 ? 1 : 0);
                        var padding = new string(
                            'x',
                            checked((int)(size - Encoding.UTF8.GetByteCount(empty)))
                        );
                        current = Encoding.UTF8.GetBytes(
                            (station > 1 ? "," : "") + empty[..^2] + padding + "\"}"
                        );
                    }
                    else
                    {
                        current = Encoding.UTF8.GetBytes(Suffix);
                        ended = true;
                    }
                    position = 0;
                }
                var take = Math.Min(target.Length - written, current.Length - position);
                current.AsSpan(position, take).CopyTo(target[written..]);
                position += take;
                written += take;
            }
            received(written);
            return written;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }
}
