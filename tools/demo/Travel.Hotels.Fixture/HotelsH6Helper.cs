using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace Travel.Hotels.Fixture;

internal static class HotelsH6Helper
{
    internal static async Task RunAsync(string root, string[] args, CancellationToken ct)
    {
        if (
            !OperatingSystem.IsWindows()
            || !Environment.Is64BitProcess
            || !HotelsH6Windows.IsAdministrator()
            || args.Length != 6
            || !Guid.TryParseExact(args[2], "N", out _)
        )
            throw new InvalidDataException("H6 internal helper invocation.");
        var packetPath = Path.GetFullPath(args[1]);
        if (args[1] != packetPath)
            throw new InvalidDataException("H6 absolute packet required.");
        var nonce = args[2];
        var pid = int.Parse(args[3], CultureInfo.InvariantCulture);
        var ticks = long.Parse(args[4], CultureInfo.InvariantCulture);
        using var driver = Process.GetProcessById(pid);
        _ = driver.Handle;
        var identity = HotelsH6Windows.ReadProcess(pid);
        HotelsH6Windows.Validate(
            identity,
            pid,
            ticks,
            null,
            args[5],
            false,
            Environment.ProcessPath!,
            [Assembly.GetExecutingAssembly().Location, "m2-h6", packetPath, "--execute"]
        );
        using var pipe = HotelsH6Wire.Client("TravelHotelsH6-" + nonce);
        using (var connect = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            connect.CancelAfter(TimeSpan.FromSeconds(5));
            await pipe.ConnectAsync(connect.Token);
        }
        if (HotelsH6Windows.ServerPid(pipe) != pid || driver.HasExited)
            throw new InvalidDataException("H6 peer driver mismatch.");
        var (packet, h5, spec) = HotelH6Packet.Read(root, packetPath);
        using var store = HotelsH6Store.Create(nonce, identity.UserSid);
        using var system = new HotelsH6Wpr(
            root,
            packetPath,
            packet,
            h5,
            spec,
            store,
            driver,
            identity,
            nonce
        );
        var trace = new HotelsH6Trace(system, nonce, store.Root);
        HotelH6TraceReceipt? receipt = null;
        var released = false;
        var remaining =
            480 - (DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc)).TotalSeconds;
        using var work = CancellationTokenSource.CreateLinkedTokenSource(ct);
        work.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(remaining, 0.001, 480)));
        using var watcherStop = new CancellationTokenSource();
        var watcher = WatchAsync();
        async Task WatchAsync()
        {
            try
            {
                while (!watcherStop.IsCancellationRequested)
                {
                    if (driver.HasExited)
                    {
                        await work.CancelAsync();
                        return;
                    }
                    system.Resources();
                    await Task.Delay(500, watcherStop.Token);
                }
            }
            catch (OperationCanceledException) when (watcherStop.IsCancellationRequested) { }
            catch
            {
                await work.CancelAsync();
            }
        }
        try
        {
            await system.PreflightAsync(work.Token);
            Directory.CreateDirectory(Path.Combine(store.Root, "temp"));
            await HotelsH6Wire.WriteAsync(pipe, new HotelH6Reply(0, true), work.Token);
            var protocol = new HotelsH6Protocol(
                trace,
                async value =>
                {
                    receipt = value;
                    await system.SaveAsync("trace-receipt.json", value);
                    return HotelsH6Store.Fingerprint(store.Root);
                },
                hash =>
                {
                    store.Release(hash);
                    released = true;
                }
            );
            while (!released)
            {
                // Stop resource monitoring before cleanup; low resources must not block stopping ETW.
                var request = await HotelsH6Wire.ReadAsync<HotelH6Message>(pipe, work.Token);
                system.Parent();
                if (request.Operation == "finish")
                {
                    await watcherStop.CancelAsync();
                    await watcher;
                    using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                    var reply = await protocol.ApplyAsync(request, cleanup.Token);
                    using var handoff = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    await HotelsH6Wire.WriteAsync(pipe, reply, handoff.Token);
                    var release = await HotelsH6Wire.ReadAsync<HotelH6Message>(pipe, handoff.Token);
                    if (release.Operation != "release")
                        throw new InvalidDataException("H6 release required.");
                    system.Parent();
                    var acknowledgement = await protocol.ApplyAsync(release, handoff.Token);
                    await HotelsH6Wire.WriteAsync(pipe, acknowledgement, handoff.Token);
                    if (!acknowledgement.Ok)
                        throw new InvalidDataException("H6 release failed.");
                    break;
                }
                await HotelsH6Wire.WriteAsync(
                    pipe,
                    await protocol.ApplyAsync(request, work.Token),
                    work.Token
                );
            }
        }
        finally
        {
            await watcherStop.CancelAsync();
            await watcher;
            // Independent from the peer, cancelled work, JSON writes and release success.
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            receipt ??= await trace.FinishAsync(cleanup.Token);
            if (!released)
            {
                try
                {
                    await system.SaveAsync(
                        "emergency-cleanup.json",
                        new { receipt, retainedTraceRoot = store.Root }
                    );
                }
                catch
                { /* Protected raw directory is retained for explicit inspection; no false success. */
                }
            }
        }
    }
}
