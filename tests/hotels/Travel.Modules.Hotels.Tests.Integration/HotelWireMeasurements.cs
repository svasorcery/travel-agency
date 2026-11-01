using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Travel.Modules.Hotels.Application;
using Travel.Modules.Hotels.Core;
using Travel.Modules.Hotels.Infrastructure.Providers.Duffel;

namespace Travel.Modules.Hotels.Tests.Integration;

public sealed record HotelWireRetention(int Rates, int Quotes, int Bookings, long? ValueBytes)
{
    // Stored key character payload, excluding object headers/dictionary overhead.
    public long? KeyUtf16Bytes { get; init; }

    // Unique immutable load-Duffel UTF8 payloads; logical ValueBytes still counts both aliases.
    public long? StoredRatePayloadBytes { get; init; }
}

public sealed record HotelWireEvent(
    long Sequence,
    long Timestamp,
    string Operation,
    string Stage,
    int? Status,
    HotelSourceCapacitySnapshot? Capacity,
    int? JobId = null,
    int? StepOrdinal = null,
    int? ReadAttempt = null
);

public sealed record HotelWireSnapshot(
    string Provider,
    int Starts,
    int Completions,
    int Active,
    int PeakActive,
    bool Overflow,
    bool Recording,
    long TimestampFrequency,
    HotelWireRetention Retained,
    IReadOnlyDictionary<string, int> Effects,
    int UnknownEffects,
    long? EffectBytes,
    HotelWireEvent[] Events
);

public sealed record HotelObservedPurchase(
    Guid Id,
    Guid? AttemptId,
    string Provider,
    bool Confirmed,
    bool SourceMatched
)
{
    public static HotelObservedPurchase Capture(
        Guid id,
        Guid? attemptId,
        string provider,
        ReservationState state,
        IEnumerable<IHotelBookingProvider> providers
    ) =>
        new(
            id,
            attemptId,
            provider,
            state.Outcome == ReservationOutcome.Confirmed,
            attemptId is not null
                && attemptId != Guid.Empty
                && state.AdmissionBinding is { } binding
                && providers.SingleOrDefault(p => p.Key == provider)
                    is IHotelSourceDescriptor source
                && source.CurrentSource.Matches(binding)
        );
}

// Test-only, fixed-size observations for the existing owned transports. No URI, body,
// reference, credential or exception text enters a frame. Complete history belongs to the parent.
public sealed class HotelWireMeasurements(string provider)
{
    private readonly object gate = new();
    private readonly Queue<HotelWireEvent> events = new(64);
    private readonly Dictionary<string, int> effects = new(StringComparer.Ordinal);
    private int starts,
        completions,
        peak;
    private int unknownEffects;
    private long sequence;
    private bool overflow;
    private bool recording;
    public HotelCapacity? Capacity { private get; set; }

    public void StartRecording()
    {
        lock (gate)
        {
            if (recording || starts != completions)
                throw new InvalidOperationException(
                    "Timed observation requires a quiescent one-time start."
                );
            recording = true; // Preserve all cumulative counts, effects and retained data.
        }
    }

    public static string Correlation(string provider, Guid attemptId) =>
        Digest(
            provider,
            provider == "hotelbeds" ? attemptId.ToString("N")[..20] : attemptId.ToString("N")
        );

    public void RecordEffect(string? reference)
    {
        lock (gate)
        {
            var length = provider == "hotelbeds" ? 20 : 32;
            if (
                reference is null
                || reference.Length != length
                || !reference.All(char.IsAsciiHexDigit)
            )
            {
                unknownEffects++;
                return;
            }
            var key = Digest(provider, reference.ToLowerInvariant());
            if (!effects.ContainsKey(key) && effects.Count == 64)
            {
                unknownEffects++;
                overflow = true;
                return;
            }
            effects[key] = effects.GetValueOrDefault(key) + 1;
        }
    }

    private static string Digest(string provider, string reference) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(provider + ":" + reference)));

    public async Task<HttpResponseMessage> ObserveAsync(
        string operation,
        Func<Task<HttpResponseMessage>> send,
        int? jobId = null,
        int? stepOrdinal = null,
        int? readAttempt = null
    )
    {
        var capacity = Capacity?.GetSourceSnapshots().Single(s => s.Provider == provider);
        long id;
        lock (gate)
        {
            id = ++sequence;
            starts++;
            peak = Math.Max(peak, starts - completions);
            Add(
                new(
                    id,
                    Stopwatch.GetTimestamp(),
                    operation,
                    "Started",
                    null,
                    capacity,
                    jobId,
                    stepOrdinal,
                    readAttempt
                )
            );
        }
        var stage = "Fault";
        int? status = null;
        try
        {
            var reply = await send();
            status = (int)reply.StatusCode;
            stage = "Response";
            return reply;
        }
        catch (OperationCanceledException)
        {
            stage = "Cancelled";
            throw;
        }
        finally
        {
            // Cancellation of the caller alone cannot close an uncooperative underlying send.
            lock (gate)
            {
                completions++;
                Add(
                    new(
                        id,
                        Stopwatch.GetTimestamp(),
                        operation,
                        stage,
                        status,
                        null,
                        jobId,
                        stepOrdinal,
                        readAttempt
                    )
                );
            }
        }
    }

    public HotelWireSnapshot Capture(HotelWireRetention retained)
    {
        lock (gate)
        {
            var pending = events.ToArray();
            events.Clear();
            return new(
                provider,
                starts,
                completions,
                starts - completions,
                peak,
                overflow,
                recording,
                Stopwatch.Frequency,
                retained,
                new Dictionary<string, int>(effects, StringComparer.Ordinal),
                unknownEffects,
                retained.ValueBytes is null
                    ? null
                    : JsonSerializer.SerializeToUtf8Bytes(effects).Length,
                pending
            );
        }
    }

    private void Add(HotelWireEvent observation)
    {
        if (!recording)
            return; // Seed counts are captured at the boundary; no unbounded pre-run event history.
        if (events.Count == 64)
            overflow = true;
        else
            events.Enqueue(observation);
    }
}
