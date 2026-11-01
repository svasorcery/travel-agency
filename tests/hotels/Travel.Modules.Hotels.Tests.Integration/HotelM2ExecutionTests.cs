using System.Text.Json;
using Shouldly;
using Travel.Hotels.Fixture;

namespace Travel.Modules.Hotels.Tests.Integration;

public sealed class HotelM2ExecutionTests
{
    private static HotelM2PlannedJob Job(int id, double due = 0) =>
        new(id, due, "search", null, null, null, null);

    private static HotelM2DispatchResult Response(HotelM2PlannedJob job, double at) =>
        new(job.Id, "Response", at, at, 200, "Complete", true, null, null, null, false);

    [Theory]
    [InlineData("begin")]
    [InlineData("dispatch")]
    [InlineData("record")]
    public async Task First_job_fault_stops_future_dispatch_and_returns_before_uncooperative_work(
        string stage
    )
    {
        using var stop = new CancellationTokenSource();
        var jobs = new[] { Job(0, stage == "begin" ? 42 : 0), Job(1, 100) };
        var ledger = new HotelsLoadLedger(jobs.Select(j => new HotelLoadJob(j.Id, j.Kind, j.Due)));
        var blocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var called = new List<int>();
        var now = stage == "begin" ? 41.9995 : 0d;
        var execution = new HotelsM2Execution(
            ledger,
            stop,
            () => now,
            (due, ct) => due == 100 ? blocked.Task.WaitAsync(ct) : Task.CompletedTask,
            (job, ct) =>
            {
                called.Add(job.Id);
                if (stage == "dispatch")
                    throw new InvalidOperationException("private-secret-should-not-be-written");
                return Task.FromResult(Response(job, now));
            },
            (job, result) =>
            {
                if (stage == "record")
                    throw new InvalidOperationException("private-secret-should-not-be-written");
            }
        );
        var requests = jobs.Select(j => execution.RunJobAsync(j, stop.Token)).ToArray();
        var finished = execution.WaitAsync(requests, Task.CompletedTask, Task.CompletedTask);
        try
        {
            await Should.ThrowAsync<InvalidOperationException>(() =>
                finished.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken)
            );
            stop.IsCancellationRequested.ShouldBeTrue();
            called.ShouldNotContain(1);
            var failure = execution.Snapshot().First!;
            failure.ShouldNotBeNull();
            failure.JobId.ShouldBe(0);
            failure.Stage.ShouldBe(stage);
            failure.ObservedAt.ShouldBe(now);
            if (stage == "begin")
                failure.AttemptedStart.ShouldBe(41.9995);
            JsonSerializer.Serialize(execution.Snapshot()).ShouldNotContain("private-secret");
            ledger.Seal(now);
            ledger
                .Snapshot()
                .Entries[0]
                .Terminal.ShouldBe(stage == "record" ? "Response" : "FailedBeforeResponse");
            ledger.Snapshot().Entries[1].Terminal.ShouldBe("NotRun");
        }
        finally
        {
            await stop.CancelAsync();
            blocked.TrySetResult();
            try
            {
                await Task.WhenAll(requests);
            }
            catch { }
        }
    }

    [Theory]
    [InlineData("sampling")]
    [InlineData("database")]
    public async Task Observer_fault_stops_dispatch_even_when_an_active_request_ignores_cancellation(
        string stage
    )
    {
        using var stop = new CancellationTokenSource();
        var ledger = new HotelsLoadLedger([new(0, "search", 0)]);
        var stubborn = new TaskCompletionSource<HotelM2DispatchResult>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var execution = new HotelsM2Execution(
            ledger,
            stop,
            () => 0,
            (_, _) => Task.CompletedTask,
            (_, _) => stubborn.Task,
            (_, _) => { }
        );
        var request = execution.RunJobAsync(Job(0), stop.Token);
        var fault = Task.FromException(new IOException("sensitive payload"));
        var finished = execution.WaitAsync(
            [request],
            stage == "sampling" ? fault : Task.CompletedTask,
            stage == "database" ? fault : Task.CompletedTask
        );
        try
        {
            await Should.ThrowAsync<InvalidOperationException>(() =>
                finished.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken)
            );
            stop.IsCancellationRequested.ShouldBeTrue();
            execution.Snapshot().First!.Stage.ShouldBe(stage);
            execution.Snapshot().First!.JobId.ShouldBeNull();
            request.IsCompleted.ShouldBeFalse();
        }
        finally
        {
            stubborn.TrySetResult(Response(Job(0), 0));
            try
            {
                await request;
            }
            catch { }
        }
    }

    [Theory]
    [InlineData("S")]
    [InlineData("L")]
    public async Task Full_fixed_schedule_uses_actual_execution_and_preserves_terminal_sample(
        string code
    )
    {
        var plan = new HotelM2LoadSchedule(new(code, new(2026, 11, 6)));
        using var stop = new CancellationTokenSource();
        var ledger = new HotelsLoadLedger(
            plan.Jobs.Select(j => new HotelLoadJob(j.Id, j.Kind, j.Due, j.Provider))
        );
        var now = 0d;
        var recorded = new List<int>();
        var terminal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var execution = new HotelsM2Execution(
            ledger,
            stop,
            () => now,
            (due, ct) =>
                HotelsLoadTiming.WaitUntilAsync(
                    due,
                    () => now,
                    ct,
                    (delay, token) =>
                    {
                        now += Math.Floor(delay.TotalMilliseconds) / 1000;
                        return Task.CompletedTask;
                    }
                ),
            (job, ct) => Task.FromResult(Response(job, now)),
            (job, result) => recorded.Add(job.Id)
        );
        var requests = plan.Jobs.Select(job => execution.RunJobAsync(job, stop.Token)).ToArray();
        var finished = execution.WaitAsync(requests, terminal.Task, Task.CompletedTask);
        finished.IsCompleted.ShouldBeFalse();
        recorded.Count.ShouldBe(3152);
        now = 360;
        terminal.SetResult();
        await finished;
        ledger.Seal(now);
        ledger.Snapshot().AllObserved.ShouldBeTrue();
        ledger.Snapshot().Entries.ShouldAllBe(e => e.Started >= e.Due);
        execution.Seal().Failures.ShouldBeEmpty();
    }

    [Fact]
    public async Task Cancellation_returns_before_stubborn_request_and_late_result_cannot_rewrite_stop()
    {
        using var stop = new CancellationTokenSource();
        var ledger = new HotelsLoadLedger([new(0, "search", 0)]);
        var stubborn = new TaskCompletionSource<HotelM2DispatchResult>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var records = 0;
        var execution = new HotelsM2Execution(
            ledger,
            stop,
            () => 0,
            (_, _) => Task.CompletedTask,
            (_, _) => stubborn.Task,
            (_, _) => records++
        );
        var request = execution.RunJobAsync(Job(0), stop.Token);
        var finished = execution.WaitAsync([request], Task.CompletedTask, Task.CompletedTask);
        await stop.CancelAsync();
        await Should.ThrowAsync<OperationCanceledException>(() =>
            finished.WaitAsync(TimeSpan.FromSeconds(1))
        );
        request.IsCompleted.ShouldBeFalse();
        ledger.Seal(0);
        execution.Seal().Failures.ShouldBeEmpty();
        stubborn.SetResult(Response(Job(0), 0));
        await request;
        records.ShouldBe(0);
        ledger.Snapshot().Entries[0].Terminal.ShouldBe("Unfinished");
    }

    [Fact]
    public async Task Simultaneous_faults_have_bounded_sanitized_evidence_and_complete_accounting()
    {
        using var stop = new CancellationTokenSource();
        var jobs = Enumerable.Range(0, 80).Select(i => Job(i)).ToArray();
        var ledger = new HotelsLoadLedger(jobs.Select(j => new HotelLoadJob(j.Id, j.Kind, j.Due)));
        var responses = jobs.Select(_ => new TaskCompletionSource<HotelM2DispatchResult>(
                TaskCreationOptions.RunContinuationsAsynchronously
            ))
            .ToArray();
        var execution = new HotelsM2Execution(
            ledger,
            stop,
            () => 0,
            (_, _) => Task.CompletedTask,
            (job, _) => responses[job.Id].Task,
            (_, _) => { }
        );
        var tasks = jobs.Select(j => execution.RunJobAsync(j, stop.Token)).ToArray();
        foreach (var response in responses)
            response.SetException(new IOException("private-secret"));
        await Task.WhenAll(tasks);
        var evidence = execution.Seal();
        evidence.Failures.Length.ShouldBe(64);
        evidence.Overflow.ShouldBeTrue();
        evidence.Failures.ShouldAllBe(f =>
            f.CallSites.Length <= 4 && f.CallSites.All(s => s.Length <= 256)
        );
        JsonSerializer.Serialize(evidence).ShouldNotContain("private-secret");
        ledger.Seal(0);
        ledger.Snapshot().Counts["FailedBeforeResponse"].ShouldBe(80);
    }

    [Fact]
    public async Task Cancellation_between_wait_and_begin_prevents_dispatch()
    {
        using var stop = new CancellationTokenSource();
        var ledger = new HotelsLoadLedger([new(0, "search", 0)]);
        var calls = 0;
        var execution = new HotelsM2Execution(
            ledger,
            stop,
            () => 0,
            async (_, _) => await stop.CancelAsync(),
            (job, ct) =>
            {
                calls++;
                return Task.FromResult(Response(job, 0));
            },
            (_, _) => { }
        );
        await execution.RunJobAsync(Job(0), stop.Token);
        calls.ShouldBe(0);
        ledger.Seal(0);
        ledger.Snapshot().Entries[0].Terminal.ShouldBe("NotRun");
        execution.Seal().Failures.ShouldBeEmpty();
    }

    [Fact]
    public async Task Expected_http_refusal_does_not_stop_work_and_terminal_sampler_must_finish()
    {
        using var stop = new CancellationTokenSource();
        var ledger = new HotelsLoadLedger([new(0, "search", 0)]);
        var terminal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var execution = new HotelsM2Execution(
            ledger,
            stop,
            () => 0,
            (_, _) => Task.CompletedTask,
            (job, ct) => Task.FromResult(Response(job, 0) with { Status = 429, Useful = false }),
            (_, _) => { }
        );
        var finished = execution.WaitAsync(
            [execution.RunJobAsync(Job(0), stop.Token)],
            terminal.Task,
            Task.CompletedTask
        );
        finished.IsCompleted.ShouldBeFalse();
        stop.IsCancellationRequested.ShouldBeFalse();
        terminal.SetResult();
        await finished;
        execution.Snapshot().Failures.ShouldBeEmpty();
        ledger.Snapshot().Entries[0].Status.ShouldBe(429);
    }
}
