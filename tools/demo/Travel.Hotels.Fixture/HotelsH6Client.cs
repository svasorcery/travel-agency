using System.Diagnostics;
using System.IO.Pipes;
using System.Reflection;
using System.Text.Json;

namespace Travel.Hotels.Fixture;

internal sealed class HotelsH6Client : IDisposable, IHotelsH6Hooks
{
    private readonly NamedPipeServerStream pipe;
    private Process? helper;
    private int sequence;
    private bool broken,
        finished;
    internal string Nonce { get; } = Guid.NewGuid().ToString("N");
    internal string TraceRoot => HotelsH6Store.Location(Nonce);
    internal HotelH6TraceReceipt? Receipt { get; private set; }
    internal int? HelperPid { get; private set; }

    private HotelsH6Client(string sid)
    {
        pipe = HotelsH6Wire.Server("TravelHotelsH6-" + Nonce, sid);
    }

    internal static HotelsH6Client Create() =>
        new(HotelsH6Windows.ReadProcess(Environment.ProcessId).UserSid);

    internal async Task LaunchAsync(string packetPath, CancellationToken ct)
    {
        if (HotelsH6Windows.IsAdministrator())
            throw new InvalidDataException("H6 driver must not be elevated.");
        var identity = HotelsH6Windows.ReadProcess(Environment.ProcessId);
        var client = this;
        var info = new ProcessStartInfo(Environment.ProcessPath!)
        {
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System),
        };
        foreach (
            var arg in new[]
            {
                "--fx-version",
                "10.0.9",
                Assembly.GetExecutingAssembly().Location,
                "m2-h6-helper",
                packetPath,
                client.Nonce,
                identity.Pid.ToString(System.Globalization.CultureInfo.InvariantCulture),
                identity.StartUtcTicks.ToString(System.Globalization.CultureInfo.InvariantCulture),
                identity.UserSid,
            }
        )
            info.ArgumentList.Add(arg);
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(TimeSpan.FromSeconds(30));
        var launch = Task.Run(() => Process.Start(info));
        try
        {
            client.helper =
                await launch.WaitAsync(limit.Token)
                ?? throw new InvalidOperationException("H6 elevation absent.");
            _ = client.helper.Handle;
            client.HelperPid = client.helper.Id;
            await client.pipe.WaitForConnectionAsync(limit.Token);
            if (
                HotelsH6Windows.ClientPid(client.pipe) != client.helper.Id
                || client.helper.HasExited
            )
                throw new InvalidDataException("H6 helper peer mismatch.");
            var ready = await HotelsH6Wire.ReadAsync<HotelH6Reply>(client.pipe, limit.Token);
            if (ready.Sequence != 0 || !ready.Ok)
                throw new InvalidDataException("H6 helper preflight refused.");
            return;
        }
        catch
        {
            // A late UAC acceptance finds a closed pipe and cannot bind/start a Host or tracing.
            _ = launch.ContinueWith(
                t =>
                {
                    if (t.Status == TaskStatus.RanToCompletion)
                        t.Result?.Dispose();
                    else
                        _ = t.Exception;
                },
                TaskScheduler.Default
            );
            client.broken = true;
            client.pipe.Dispose();
            throw;
        }
    }

    private async Task<HotelH6Reply> ExchangeAsync(
        string operation,
        HotelH6Target? target,
        string? hash,
        int seconds,
        CancellationToken ct
    )
    {
        if (broken || helper is null || helper.HasExited)
            throw new InvalidOperationException("H6 channel unavailable.");
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(TimeSpan.FromSeconds(seconds));
        var request = new HotelH6Message(++sequence, operation, target, hash);
        try
        {
            await HotelsH6Wire.WriteAsync(pipe, request, limit.Token);
            var result = await HotelsH6Wire.ReadAsync<HotelH6Reply>(pipe, limit.Token);
            if (result.Sequence != request.Sequence)
                throw new InvalidDataException("H6 response identity.");
            // A framed negative reply is unambiguous; keep the channel available for finish.
            return result;
        }
        catch
        {
            broken = true;
            pipe.Dispose();
            throw;
        }
    }

    internal async Task AttachAsync(HotelsL1HostProcess host, CancellationToken ct)
    {
        var reply = await ExchangeAsync(
            "attach",
            new(host.Process.Id, host.Process.StartTime.ToUniversalTime().Ticks),
            null,
            31,
            ct
        );
        if (!reply.Ok)
            throw new InvalidDataException("H6 attach failed:" + reply.Error);
    }

    public async Task BeforeMapAsync(string name, CancellationToken ct)
    {
        var operation =
            name == "P1" ? "A"
            : name == "P2" ? "B"
            : throw new InvalidDataException("H6 map.");
        var reply = await ExchangeAsync(operation, null, null, 30, ct);
        if (!reply.Ok)
            throw new InvalidDataException("H6 snapshot failed:" + reply.Error);
    }

    public HotelH6Comparison Compare(JsonElement before, JsonElement after) =>
        HotelsH6Comparison.Compare(before, after);

    internal async Task FinishAsync(string output, Action resources, CancellationToken ct)
    {
        if (finished)
            throw new InvalidOperationException("H6 finish already consumed.");
        finished = true;
        var response = await ExchangeAsync("finish", null, null, 61, ct);
        if (!response.Ok || response.Data is null)
            throw new InvalidDataException("H6 finish failed.");
        var handoff =
            response.Data.Value.Deserialize<HotelH6Handoff>(HotelsH6Wire.Json)
            ?? throw new InvalidDataException("H6 handoff absent.");
        Receipt = handoff.Trace;
        var destination = Path.Combine(output, "trace");
        if (Directory.Exists(destination))
            throw new InvalidDataException("H6 trace copy exists.");
        Directory.CreateDirectory(destination);
        using var transfer = CancellationTokenSource.CreateLinkedTokenSource(ct);
        transfer.CancelAfter(TimeSpan.FromSeconds(8));
        foreach (var source in HotelsH6Store.Files(TraceRoot))
        {
            resources();
            var path = Path.GetFullPath(
                Path.Combine(destination, Path.GetRelativePath(TraceRoot, source))
            );
            if (
                !path.StartsWith(
                    destination + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase
                )
            )
                throw new InvalidDataException("H6 copy escaped.");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await HotelsH6Copy.FileAsync(
                source,
                path,
                () =>
                    536870912
                    - checked(HotelsH6Store.Bytes(output) + HotelsH6Store.Bytes(TraceRoot)),
                resources,
                transfer.Token
            );
        }
        if (HotelsH6Store.Fingerprint(destination) != handoff.Fingerprint)
            throw new InvalidDataException("H6 trace copy mismatch.");
        var released = await ExchangeAsync("release", null, handoff.Fingerprint, 5, transfer.Token);
        if (!released.Ok)
            throw new InvalidDataException("H6 protected output retained.");
        await helper!.WaitForExitAsync(transfer.Token);
        if (
            helper.ExitCode != 0
            || Directory.Exists(TraceRoot)
            || !Receipt.TrackingClosed
            || !Receipt.SessionClosed
        )
            throw new InvalidDataException("H6 helper cleanup incomplete.");
    }

    internal void CheckResources(HotelM2LoadRunSpec spec, string output)
    {
        HotelM2ResourceGuard.Check(spec, output);
        if (
            new DriveInfo(Path.GetPathRoot(TraceRoot)!).AvailableFreeSpace
                < spec.MinimumFreeDiskBytes
            || checked(HotelsH6Store.Bytes(output) + HotelsH6Store.Bytes(TraceRoot))
                > spec.MaximumEvidenceBytes
        )
            throw new InvalidOperationException("H6 combined resource stop.");
    }

    public void Dispose()
    {
        pipe.Dispose();
        helper?.Dispose();
    }
}
