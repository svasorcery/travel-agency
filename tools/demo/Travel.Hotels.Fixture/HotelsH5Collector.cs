using System.Diagnostics;

namespace Travel.Hotels.Fixture;

internal interface IHotelH5CollectorProcess : IDisposable
{
    StreamReader Output { get; }
    StreamReader Error { get; }
    bool HasExited { get; }
    int ExitCode { get; }
    Task WaitAsync(CancellationToken ct);
    void Kill();
}

internal sealed class HotelH5CollectorProcess(Process process) : IHotelH5CollectorProcess
{
    public StreamReader Output => process.StandardOutput;
    public StreamReader Error => process.StandardError;
    public bool HasExited => process.HasExited;
    public int ExitCode => process.ExitCode;

    public Task WaitAsync(CancellationToken ct) => process.WaitForExitAsync(ct);

    public void Kill() => process.Kill(entireProcessTree: true);

    public void Dispose() => process.Dispose();
}

internal static class HotelsH5Collector
{
    internal static async Task<HotelH5Map> CaptureAsync(
        string tool,
        string output,
        int pid,
        Action validate,
        Action guard,
        Func<ProcessStartInfo, IHotelH5CollectorProcess> start,
        CancellationToken ct
    )
    {
        ct.ThrowIfCancellationRequested();
        validate();
        guard();
        if (File.Exists(output))
            throw new InvalidDataException("Retain prior native map.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        using var process = start(HotelsH5Map.StartInfo(tool, pid, output));
        var stdout = HotelsH5Map.ReadBoundedAsync(process.Output, deadline.Token);
        var stderr = HotelsH5Map.ReadBoundedAsync(process.Error, deadline.Token);
        var exit = process.WaitAsync(deadline.Token);
        var all = Task.WhenAll(exit, stdout, stderr);
        try
        {
            while (!all.IsCompleted)
            {
                deadline.Token.ThrowIfCancellationRequested();
                // Faulted output reads must stop the process immediately, not after exit.
                if (stdout.IsFaulted)
                    await stdout;
                if (stderr.IsFaulted)
                    await stderr;
                guard();
                if (
                    File.Exists(output)
                    && new FileInfo(output).Length > HotelsH5Map.MaximumXmlBytes
                )
                    throw new InvalidDataException("Native map exceeded its bound.");
                await Task.WhenAny(all, Task.Delay(50, deadline.Token));
            }
            await all;
            deadline.Token.ThrowIfCancellationRequested();
            validate();
            guard();
            if (process.ExitCode != 0 || !string.IsNullOrWhiteSpace(await stderr))
                throw new InvalidDataException("Collector failed; no map accepted.");
            return HotelsH5Map.Read(output, pid);
        }
        finally
        {
            await deadline.CancelAsync();
            if (!process.HasExited)
                process.Kill();
            // Bound all drain work. A timeout remains failure; never hide it as success.
            using var drain = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await process.WaitAsync(drain.Token);
            try
            {
                await all.WaitAsync(drain.Token);
            }
            catch (Exception) when (all.IsCompleted)
            { /* Original failure is preserved. */
            }
        }
    }
}
