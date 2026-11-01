using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace Travel.Hotels.Fixture;

internal sealed class HotelsH6Wpr(
    string root,
    string packetPath,
    HotelH6Packet packet,
    HotelH5Packet h5,
    HotelM2LoadRunSpec spec,
    HotelsH6Store store,
    Process driver,
    HotelH6Process driverIdentity,
    string nonce
) : IHotelH6TraceSystem, IDisposable
{
    private Process? target;
    private HotelH6Target? binding;
    private readonly string dll = Assembly.GetExecutingAssembly().Location;
    private readonly string dllHash = HotelsL1HostProcess.Hash(
        Assembly.GetExecutingAssembly().Location
    );
    private readonly string manifest = HotelM2LoadRunSpec.Resolve(root, spec.Manifest, false);
    private readonly string manifestHash = HotelsL1HostProcess.Hash(
        HotelM2LoadRunSpec.Resolve(root, spec.Manifest, false)
    );
    internal static string Wpr =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "wpr.exe");
    internal string Instance => "TravelHotelsH6-" + nonce;
    public bool EtlExists =>
        File.Exists(Path.Combine(store.Root, "heap.etl"))
        && new FileInfo(Path.Combine(store.Root, "heap.etl")).Length > 0;

    internal void Parent()
    {
        if (driver.HasExited)
            throw new InvalidDataException("H6 driver exited.");
        HotelsH6Windows.Validate(
            HotelsH6Windows.ReadProcess(driver.Id),
            driver.Id,
            driverIdentity.StartUtcTicks,
            null,
            driverIdentity.UserSid,
            false,
            Environment.ProcessPath!,
            [dll, "m2-h6", packetPath, "--execute"]
        );
        VerifyIdentityFiles();
    }

    private void VerifyIdentityFiles()
    {
        if (
            HotelsL1HostProcess.Hash(dll) != dllHash
            || HotelsL1HostProcess.Hash(manifest) != manifestHash
        )
            throw new InvalidDataException("H6 frozen identity changed.");
        packet.VerifyRecorder();
    }

    public void ValidateTarget(HotelH6Target value, bool cleanup)
    {
        if (!cleanup)
            Parent();
        else
            VerifyIdentityFiles();
        if (binding is not null && binding != value)
            throw new InvalidDataException("H6 target rebound.");
        var actual = HotelsH6Windows.ReadProcess(value.Pid);
        HotelsH6Windows.Validate(
            actual,
            value.Pid,
            value.StartUtcTicks,
            driver.Id,
            driverIdentity.UserSid,
            false,
            Environment.ProcessPath!,
            [dll, "m2-host", HotelM2LoadRunSpec.Resolve(root, h5.LoadProfile, false)]
        );
        if (target is null)
        {
            target = Process.GetProcessById(value.Pid);
            _ = target.Handle; // Retain the original process object; never act on a recycled PID.
            if (target.HasExited || target.StartTime.ToUniversalTime().Ticks != value.StartUtcTicks)
                throw new InvalidDataException("H6 bind race.");
            binding = value;
        }
        if (target.HasExited)
            throw new InvalidDataException("H6 target exited.");
    }

    public bool TargetExited(HotelH6Target value) =>
        binding == value && target is not null && target.HasExited;

    internal void Resources()
    {
        var output = HotelM2LoadRunSpec.Resolve(root, spec.Output, true);
        HotelM2ResourceGuard.Check(spec, output);
        if (
            new DriveInfo(Path.GetPathRoot(store.Root)!).AvailableFreeSpace
                < spec.MinimumFreeDiskBytes
            || checked(HotelsH6Store.Bytes(output) + HotelsH6Store.Bytes(store.Root))
                > spec.MaximumEvidenceBytes
        )
            throw new InvalidOperationException("H6 resource stop.");
    }

    internal async Task PreflightAsync(CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows() || !HotelsH6Windows.IsAdministrator())
            throw new InvalidOperationException("H6 helper must be elevated.");
        Parent();
        Resources();
        using var ifeo = Registry.LocalMachine.OpenSubKey(
            @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\dotnet.exe",
            false
        );
        if (ifeo is not null)
            throw new InvalidDataException(
                "Existing dotnet IFEO is not qualified; leave it untouched."
            );
        var sessions = HotelsH6Native.Sessions();
        if (
            sessions.Any(s =>
                s.Name.Contains("WPR", StringComparison.OrdinalIgnoreCase)
                || s.Name.Contains(Instance, StringComparison.Ordinal)
            )
        )
            throw new InvalidDataException(
                "Pre-existing WPR session; no cleanup of foreign sessions."
            );
        var status = await CommandAsync("preflight-status", ["-status"], 10, true, ct);
        if (!status.TrimEnd().EndsWith("WPR is not recording", StringComparison.Ordinal))
            throw new InvalidDataException("Unsupported/nonempty WPR status.");
        await SaveAsync("preflight-sessions.json", sessions);
    }

    public async Task RunAsync(
        string operation,
        string[] arguments,
        int seconds,
        CancellationToken ct
    )
    {
        if (operation is "enable" or "start" or "A" or "B")
        {
            Parent();
            Resources();
        }
        if (operation == "stop")
        {
            try
            {
                await SaveAsync("sessions-before-stop.json", HotelsH6Native.Sessions());
            }
            catch
            { /* Loss telemetry is not a prerequisite for stopping owned recording. */
            }
        }
        await CommandAsync(
            operation,
            arguments,
            seconds,
            operation is "disable" or "stop" or "cancel",
            ct
        );
    }

    public Task<HotelH6SessionStatus> SessionAsync(string instance, CancellationToken ct)
    {
        if (instance != Instance)
            throw new InvalidDataException("H6 instance mismatch.");
        try
        {
            return Task.FromResult(HotelsH6Native.OwnSession(HotelsH6Native.Sessions(), instance));
        }
        catch
        {
            return Task.FromResult(HotelH6SessionStatus.Unknown);
        }
    }

    private async Task<string> CommandAsync(
        string operation,
        string[] args,
        int seconds,
        bool cleanup,
        CancellationToken ct
    )
    {
        packet.VerifyRecorder();
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(TimeSpan.FromSeconds(seconds));
        var started = DateTimeOffset.UtcNow;
        var info = new ProcessStartInfo(Wpr)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = store.Root,
        };
        info.Environment.Clear();
        foreach (var name in new[] { "SystemRoot", "WINDIR", "SystemDrive", "ProgramData" })
            if (Environment.GetEnvironmentVariable(name) is string value)
                info.Environment[name] = value;
        info.Environment["TEMP"] = store.Root;
        info.Environment["TMP"] = store.Root;
        info.Environment["PATH"] = Environment.GetFolderPath(Environment.SpecialFolder.System);
        foreach (var argument in args)
            info.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = info };
        string? failure = null,
            stdout = null,
            stderr = null;
        int? pid = null,
            exit = null;
        return await HotelsH6CommandObservation.ExecuteAsync(
            cleanup,
            () => target is not null && !target.HasExited ? L1ProcessMemory.Capture(target) : null,
            RunObserved
        );
        async Task<string> RunObserved(HotelH6Observation before)
        {
            try
            {
                try
                {
                    await SaveAsync(operation + "-intent.json", new { started, args });
                }
                catch when (cleanup) { }
                limit.Token.ThrowIfCancellationRequested();
                if (!process.Start())
                    throw new InvalidOperationException("WPR command did not start.");
                pid = process.Id;
                var output = ReadBoundedAsync(process.StandardOutput, limit.Token);
                var error = ReadBoundedAsync(process.StandardError, limit.Token);
                var exited = process.WaitForExitAsync(limit.Token);
                while (!exited.IsCompleted || !output.IsCompleted || !error.IsCompleted)
                {
                    if (output.IsFaulted)
                        await output;
                    if (error.IsFaulted)
                        await error;
                    limit.Token.ThrowIfCancellationRequested();
                    if (!cleanup)
                    {
                        Parent();
                        Resources();
                    }
                    await Task.Delay(250, limit.Token);
                }
                await exited;
                stdout = await output;
                stderr = await error;
                exit = process.ExitCode;
                if (exit != 0)
                    throw new InvalidOperationException("WPR command failed.");
                return stdout;
            }
            catch (Exception ex)
            {
                failure = ex.GetType().Name;
                throw;
            }
            finally
            {
                if (pid is not null && !process.HasExited)
                {
                    try
                    {
                        process.Kill(entireProcessTree: true);
                        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(1));
                    }
                    catch
                    {
                        failure ??= "OwnedCommandCleanupUnknown";
                    }
                }
                try
                {
                    await SaveAsync(
                        operation + "-result.json",
                        new
                        {
                            started,
                            ended = DateTimeOffset.UtcNow,
                            pid,
                            exit,
                            failure,
                            stdout,
                            stderr,
                            before,
                            after = HotelsH6CommandObservation.Read(
                                cleanup,
                                () =>
                                    target is not null && !target.HasExited
                                        ? L1ProcessMemory.Capture(target)
                                        : null
                            ),
                        }
                    );
                }
                catch when (cleanup) { }
            }
        }
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader, CancellationToken ct)
    {
        var result = new StringBuilder();
        var buffer = new char[2048];
        int n;
        while ((n = await reader.ReadAsync(buffer, ct)) != 0)
        {
            if (result.Length + n > 65536)
                throw new InvalidDataException("WPR output bound.");
            result.Append(buffer, 0, n);
        }
        return result.ToString();
    }

    internal async Task SaveAsync(string name, object value)
    {
        if (Path.GetFileName(name) != name)
            throw new InvalidDataException("H6 filename.");
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await using var file = new FileStream(
            Path.Combine(store.Root, name),
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.Read
        );
        await JsonSerializer.SerializeAsync(file, value, HotelM2LoadRunSpec.Json, limit.Token);
    }

    public void Dispose() => target?.Dispose();
}
