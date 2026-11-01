using System.Diagnostics;
using Travel.Modules.Hotels.Tests.Integration;

namespace Travel.Hotels.Fixture;

internal sealed record HotelM2ExecutionFailure(
    int? JobId,
    string? Kind,
    double? Due,
    double? ObservedAt,
    double? AttemptedStart,
    string Stage,
    string ExceptionType,
    string[] CallSites
)
{
    public int? ObservedHttpStatus { get; init; }
}

internal sealed record HotelM2ExecutionFailures(
    HotelM2ExecutionFailure? First,
    bool Overflow,
    HotelM2ExecutionFailure[] Failures
);

// Only the fixed Hotels workload: unexpected driver/observer errors stop the shared run.
internal sealed class HotelsM2Execution(
    HotelsLoadLedger ledger,
    CancellationTokenSource stop,
    Func<double> elapsed,
    Func<double, CancellationToken, Task> wait,
    Func<HotelM2PlannedJob, CancellationToken, Task<HotelM2DispatchResult>> dispatch,
    Action<HotelM2PlannedJob, HotelM2DispatchResult> record
)
{
    private readonly object gate = new();
    private readonly CancellationToken stopToken = stop.Token;
    private int stopping;
    private bool StopRequested =>
        Volatile.Read(ref stopping) != 0 || stopToken.IsCancellationRequested;
    private readonly List<HotelM2ExecutionFailure> failures = [];
    private readonly TaskCompletionSource failed = new(
        TaskCreationOptions.RunContinuationsAsynchronously
    );
    private bool overflow,
        sealedEvidence;

    internal HotelM2ExecutionFailures Snapshot()
    {
        lock (gate)
            return new(failures.FirstOrDefault(), overflow, failures.ToArray());
    }

    internal HotelM2ExecutionFailures Seal()
    {
        lock (gate)
        {
            sealedEvidence = true;
            return Snapshot();
        }
    }

    internal static HotelM2ExecutionFailure Describe(
        Exception ex,
        string stage,
        double at,
        HotelM2PlannedJob? job = null,
        double? attemptedStart = null
    ) =>
        new(
            job?.Id,
            job?.Kind,
            job?.Due,
            Finite(at) ? at : null,
            attemptedStart is { } start && Finite(start) ? start : null,
            stage,
            ex.GetType().FullName ?? ex.GetType().Name,
            new StackTrace(ex, false)
                .GetFrames()
                .Take(4)
                .Select(frame =>
                {
                    var method = frame.GetMethod();
                    var name =
                        (method?.DeclaringType?.FullName ?? "unknown")
                        + "."
                        + (method?.Name ?? "unknown");
                    return name[..Math.Min(name.Length, 256)];
                })
                .ToArray()
        );

    private void Fail(
        Exception ex,
        string stage,
        HotelM2PlannedJob? job = null,
        double? attemptedStart = null
    ) => Report(Describe(ex, stage, elapsed(), job, attemptedStart));

    private void Report(HotelM2ExecutionFailure failure)
    {
        bool first;
        lock (gate)
        {
            if (sealedEvidence)
                return;
            first = failures.Count == 0;
            if (failures.Count < 64)
                failures.Add(failure);
            else
                overflow = true;
            Volatile.Write(ref stopping, 1);
        }
        // Close admission before waking the supervisor; linked token callbacks may run later.
        if (first)
            _ = CancelAndObserveAsync();
        failed.TrySetResult();
    }

    private async Task CancelAndObserveAsync()
    {
        try
        {
            await stop.CancelAsync();
        }
        catch (Exception ex)
        {
            Fail(ex, "cancellation");
        }
    }

    internal async Task RunJobAsync(HotelM2PlannedJob job, CancellationToken ct)
    {
        var stage = "wait";
        double? attemptedStart = null;
        HotelM2DispatchResult? result = null;
        try
        {
            if (StopRequested)
                return;
            ct.ThrowIfCancellationRequested();
            await wait(job.Due, ct);
            ct.ThrowIfCancellationRequested();
            stage = "begin";
            attemptedStart = elapsed();
            if (!ledger.Begin(job.Id, attemptedStart.Value))
                return;
            stage = "dispatch";
            if (StopRequested)
                return;
            result = await dispatch(job, ct);
            if (result.DriverFailure is { } dispatchFailure)
                Report(dispatchFailure);
            stage = "terminal";
            if (!ledger.Complete(job.Id, result.Terminal, result.Completed, result.Status))
                return;
            stage = "record";
            record(job, result);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested || StopRequested) { }
        catch (Exception ex)
        {
            var at = elapsed();
            Exception? accountingError = null;
            try
            {
                if (result is null && Finite(at))
                    ledger.FailBeforeResponse(job.Id, at);
            }
            catch (Exception secondary)
            {
                accountingError = secondary;
            }
            Report(
                Describe(ex, stage, at, job, attemptedStart) with
                {
                    ObservedHttpStatus = result?.Status,
                }
            );
            if (accountingError is not null)
                Fail(accountingError, "failure-accounting", job, attemptedStart);
        }
    }

    internal async Task ObserveAsync(Func<Task> action, string stage)
    {
        try
        {
            await action();
        }
        catch (OperationCanceledException) when (StopRequested) { }
        catch (Exception ex)
        {
            Fail(ex, stage);
        }
    }

    private async Task WatchAsync(Task task, string stage)
    {
        try
        {
            await task;
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Fail(ex, stage);
        }
    }

    internal async Task WaitAsync(Task[] requests, Task sampling, Task database)
    {
        var cancelled = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        using var registration = stopToken.Register(() => cancelled.TrySetResult());
        var all = Task.WhenAll(
            Task.WhenAll(requests),
            WatchAsync(sampling, "sampling"),
            WatchAsync(database, "database")
        );
        await Task.WhenAny(all, failed.Task, cancelled.Task);
        if (Snapshot().First is { } first)
            throw new InvalidOperationException(
                "M2 execution failed in " + first.Stage + "; see execution-failures.json."
            );
        stopToken.ThrowIfCancellationRequested();
        await all;
    }

    private static bool Finite(double value) => double.IsFinite(value) && value >= 0;
}
