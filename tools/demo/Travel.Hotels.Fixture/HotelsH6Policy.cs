using System.Globalization;
using System.Text.Json;

namespace Travel.Hotels.Fixture;

internal sealed record HotelH6Target(int Pid, long StartUtcTicks);

internal sealed record HotelH6Comparison(bool Comparable, string[] Changed);

internal sealed record HotelH6TraceReceipt(
    bool TrackingClosed,
    bool SessionClosed,
    int Snapshots,
    bool EtlSaved,
    string[] Errors
);

internal enum HotelH6SessionStatus
{
    Absent,
    Owned,
    Unknown,
}

internal interface IHotelH6TraceSystem
{
    void ValidateTarget(HotelH6Target target, bool cleanup);
    bool TargetExited(HotelH6Target target);
    Task RunAsync(string operation, string[] arguments, int seconds, CancellationToken ct);
    Task<HotelH6SessionStatus> SessionAsync(string instance, CancellationToken ct);
    bool EtlExists { get; }
}

// One target, one instance, A then B, and independent cleanup. Never a generic WPR broker.
internal sealed class HotelsH6Trace(IHotelH6TraceSystem system, string nonce, string traceRoot)
{
    private readonly object gate = new();
    private readonly List<string> errors = [];
    private HotelH6Target? target;
    private bool trackingMayExist,
        sessionMayExist,
        attached,
        failed,
        busy;
    private int snapshots;
    private Task<HotelH6TraceReceipt>? finish;
    internal string Instance => "TravelHotelsH6-" + nonce;

    internal async Task AttachAsync(HotelH6Target value, CancellationToken ct)
    {
        if (!Guid.TryParseExact(nonce, "N", out _) || !Path.IsPathFullyQualified(traceRoot))
            throw new ArgumentException("H6 needs a nonce and absolute owned trace root.");
        ct.ThrowIfCancellationRequested();
        system.ValidateTarget(value, false);
        lock (gate)
        {
            if (target is not null || finish is not null || busy)
                throw new InvalidOperationException("H6 attach is one-shot.");
            target = value;
            busy = true;
        }
        var pid = value.Pid.ToString(CultureInfo.InvariantCulture);
        try
        {
            trackingMayExist = true; // Set before the side effect, including ambiguous timeout.
            await Run("enable", ["-snapshotconfig", "heap", "-pid", pid, "enable"], 15, ct);
            system.ValidateTarget(value, false);
            sessionMayExist = true;
            await Run(
                "start",
                [
                    "-start",
                    "HeapSnapshot.Verbose",
                    "-filemode",
                    "-recordtempto",
                    Path.Combine(traceRoot, "temp"),
                    "-instancename",
                    Instance,
                ],
                15,
                ct
            );
            attached = true;
        }
        finally
        {
            lock (gate)
                busy = false;
        }
    }

    internal async Task SnapshotAsync(string name, CancellationToken ct)
    {
        lock (gate)
        {
            if (
                !attached
                || failed
                || busy
                || finish is not null
                || name
                    != (
                        snapshots == 0 ? "A"
                        : snapshots == 1 ? "B"
                        : ""
                    )
            )
                throw new InvalidOperationException("H6 requires exactly A then B.");
            busy = true;
        }
        try
        {
            ct.ThrowIfCancellationRequested();
            system.ValidateTarget(target!, false);
            await Run(
                name,
                [
                    "-singlesnapshot",
                    "heap",
                    target!.Pid.ToString(CultureInfo.InvariantCulture),
                    "-instancename",
                    Instance,
                ],
                30,
                ct
            );
            snapshots++;
        }
        catch
        {
            failed = true;
            throw;
        }
        finally
        {
            lock (gate)
                busy = false;
        }
    }

    private async Task Run(string operation, string[] args, int seconds, CancellationToken ct)
    {
        try
        {
            await system.RunAsync(operation, args, seconds, ct);
            ct.ThrowIfCancellationRequested();
        }
        catch (Exception ex)
        {
            failed = true;
            errors.Add(operation + ":" + ex.GetType().Name);
            throw;
        }
    }

    internal Task<HotelH6TraceReceipt> FinishAsync(CancellationToken ct)
    {
        lock (gate)
            return finish ??= FinishCore(ct);
    }

    private async Task<HotelH6TraceReceipt> FinishCore(CancellationToken ct)
    {
        var trackingClosed = !trackingMayExist;
        var sessionClosed = !sessionMayExist;
        if (trackingMayExist && target is not null)
        {
            try
            {
                if (!system.TargetExited(target))
                {
                    system.ValidateTarget(target, true);
                    await system.RunAsync(
                        "disable",
                        [
                            "-snapshotconfig",
                            "heap",
                            "-pid",
                            target.Pid.ToString(CultureInfo.InvariantCulture),
                            "disable",
                        ],
                        15,
                        ct
                    );
                }
                trackingClosed = true; // Exit also ends PID-local settings; never disable a reused PID.
            }
            catch (Exception ex)
            {
                errors.Add("disable:" + ex.GetType().Name);
            }
        }
        if (sessionMayExist)
        {
            try
            {
                // The random instance was verified absent before our sole start attempt.
                // Stop is scoped even when start timed out; it never targets the default instance.
                await system.RunAsync(
                    "stop",
                    [
                        "-stop",
                        Path.Combine(traceRoot, "heap.etl"),
                        "-skipPdbGen",
                        "-instancename",
                        Instance,
                    ],
                    45,
                    ct
                );
                sessionClosed =
                    await system.SessionAsync(Instance, ct) == HotelH6SessionStatus.Absent;
                if (!sessionClosed)
                    errors.Add("stop:SessionNotClosed");
            }
            catch (Exception ex)
            {
                errors.Add("stop:" + ex.GetType().Name);
            }
            if (!sessionClosed)
            {
                try
                {
                    var status = await system.SessionAsync(Instance, ct);
                    if (status == HotelH6SessionStatus.Owned)
                    {
                        await system.RunAsync(
                            "cancel",
                            ["-cancel", "-instancename", Instance],
                            10,
                            ct
                        );
                        sessionClosed =
                            await system.SessionAsync(Instance, ct) == HotelH6SessionStatus.Absent;
                    }
                    else
                        sessionClosed = status == HotelH6SessionStatus.Absent;
                    if (!sessionClosed)
                        errors.Add("cleanup:SessionUnknown");
                }
                catch (Exception ex)
                {
                    errors.Add("cancel:" + ex.GetType().Name);
                }
            }
        }
        return new(trackingClosed, sessionClosed, snapshots, system.EtlExists, errors.ToArray());
    }
}

internal static class HotelsH6Comparison
{
    internal static HotelH6Comparison Compare(JsonElement before, JsonElement after)
    {
        var changed = new List<string>();
        try
        {
            foreach (var group in new[] { "sourceMeasurements", "sourceCapacity" })
            {
                var left = Providers(before.GetProperty(group));
                var right = Providers(after.GetProperty(group));
                foreach (var provider in left.Keys)
                {
                    var a = left[provider];
                    var b = right[provider];
                    if (group == "sourceMeasurements")
                    {
                        foreach (
                            var name in new[]
                            {
                                "starts",
                                "completions",
                                "active",
                                "overflow",
                                "unknownEffects",
                                "effects",
                                "retained",
                            }
                        )
                            Same(
                                a.GetProperty(name),
                                b.GetProperty(name),
                                group + "." + provider + "." + name
                            );
                        if (
                            b.GetProperty("active").GetInt32() != 0
                            || b.GetProperty("overflow").GetBoolean()
                        )
                            changed.Add(group + "." + provider + ".notIdle");
                    }
                    else
                    {
                        Same(
                            a.GetProperty("releases"),
                            b.GetProperty("releases"),
                            group + "." + provider + ".releases"
                        );
                        foreach (
                            var name in new[]
                            {
                                "totalStarts",
                                "foregroundActive",
                                "recoveryActive",
                            }
                        )
                            Same(
                                a.GetProperty("capacity").GetProperty(name),
                                b.GetProperty("capacity").GetProperty(name),
                                group + "." + provider + "." + name
                            );
                        if (
                            b.GetProperty("capacity").GetProperty("foregroundActive").GetInt32()
                                != 0
                            || b.GetProperty("capacity").GetProperty("recoveryActive").GetInt32()
                                != 0
                        )
                            changed.Add(group + "." + provider + ".notIdle");
                    }
                }
            }
            foreach (
                var name in new[]
                {
                    "reservations",
                    "retainedInboxRows",
                    "retainedInboxCiphertextBytes",
                    "cacheEntries",
                    "cacheBytes",
                    "pendingInbox",
                    "dueWork",
                    "futureWork",
                }
            )
                Same(
                    before.GetProperty("metrics").GetProperty(name),
                    after.GetProperty("metrics").GetProperty(name),
                    "metrics." + name
                );
            if (
                after.GetProperty("metrics").GetProperty("pendingInbox").GetInt32() != 0
                || after.GetProperty("metrics").GetProperty("dueWork").GetInt32() != 0
            )
                changed.Add("metrics.notIdle");
        }
        catch (Exception ex)
            when (ex is KeyNotFoundException or InvalidOperationException or ArgumentException)
        {
            changed.Add("unsupported-observation-schema");
        }
        return new(changed.Count == 0, changed.Distinct().ToArray());

        void Same(JsonElement a, JsonElement b, string path)
        {
            if (Canonical(a) != Canonical(b))
                changed.Add(path);
        }
    }

    private static Dictionary<string, JsonElement> Providers(JsonElement array)
    {
        var result = array
            .EnumerateArray()
            .ToDictionary(v => v.GetProperty("provider").GetString()!);
        if (
            result.Count != 2
            || !result.ContainsKey("duffel-stays")
            || !result.ContainsKey("hotelbeds")
        )
            throw new InvalidOperationException("Both sources required.");
        return result;
    }

    private static string Canonical(JsonElement e) =>
        e.ValueKind switch
        {
            JsonValueKind.Object => "{"
                + string.Join(
                    ",",
                    e.EnumerateObject()
                        .OrderBy(p => p.Name, StringComparer.Ordinal)
                        .Select(p => JsonSerializer.Serialize(p.Name) + ":" + Canonical(p.Value))
                )
                + "}",
            JsonValueKind.Array => "["
                + string.Join(",", e.EnumerateArray().Select(Canonical))
                + "]",
            _ => e.GetRawText(),
        };
}

internal interface IHotelsH6Hooks
{
    Task BeforeMapAsync(string name, CancellationToken ct);
    HotelH6Comparison Compare(JsonElement before, JsonElement after);
}
