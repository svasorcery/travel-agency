using System.Net;
using System.Text.Json;
using Shouldly;
using Travel.Hotels.Fixture;

namespace Travel.Modules.Hotels.Tests.Integration;

public sealed class HotelM2ExecutionReviewTests
{
    private static HotelM2PlannedJob Job(int id) => new(id, 0, "search", null, null, null, null);

    private static HotelM2DispatchResult Response(HotelM2PlannedJob j) =>
        new(j.Id, "Response", 0, 0, 200, "Complete", true, null, null, null, false);

    [Fact]
    public async Task Shared_stop_blocks_future_dispatch_when_linked_cancellation_is_delayed_and_disposed()
    {
        using var stop = new CancellationTokenSource();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
        var token = linked.Token;
        using var release = new ManualResetEventSlim();
        var callbackEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var callbackExited = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        using var registration = stop.Token.Register(() =>
        {
            callbackEntered.SetResult();
            release.Wait(TestContext.Current.CancellationToken);
            callbackExited.SetResult();
        });
        var future = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ledger = new HotelsLoadLedger([new(0, "search", 0), new(1, "search", 0)]);
        var calls = new List<int>();
        var execution = new HotelsM2Execution(
            ledger,
            stop,
            () => 0,
            (_, ct) => calls.Count == 0 ? Task.CompletedTask : future.Task,
            (job, ct) =>
            {
                calls.Add(job.Id);
                if (job.Id == 0)
                    throw new InvalidOperationException("first");
                return Task.FromResult(Response(job));
            },
            (_, _) => { }
        );
        var first = execution.RunJobAsync(Job(0), token);
        try
        {
            await callbackEntered.Task.WaitAsync(
                TimeSpan.FromSeconds(1),
                TestContext.Current.CancellationToken
            );
            token.IsCancellationRequested.ShouldBeFalse();
            linked.Dispose();
            var second = execution.RunJobAsync(Job(1), token);
            future.SetResult();
            await second;
            calls.ShouldBe(new[] { 0 });
        }
        finally
        {
            release.Set();
            await callbackExited.Task.WaitAsync(
                TimeSpan.FromSeconds(1),
                TestContext.Current.CancellationToken
            );
            await first;
        }
    }

    [Theory]
    [InlineData("sampling", false)]
    [InlineData("database", false)]
    [InlineData("sampling", true)]
    public async Task Actual_observer_boundary_keeps_original_fault_before_cancellation_callbacks(
        string stage,
        bool throwing
    )
    {
        using var stop = new CancellationTokenSource();
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = stop.Token.Register(() =>
        {
            entered.SetResult();
            try
            {
                if (throwing)
                    throw new ApplicationException("private callback");
                release.Wait(TestContext.Current.CancellationToken);
            }
            finally
            {
                exited.SetResult();
            }
        });
        var ledger = new HotelsLoadLedger([new(0, "search", 0)]);
        var execution = new HotelsM2Execution(
            ledger,
            stop,
            () => 0,
            (_, _) => Task.CompletedTask,
            (job, _) => Task.FromResult(Response(job)),
            (_, _) => { }
        );
        var observer = execution.ObserveAsync(
            () => Task.FromException(new IOException("private source")),
            stage
        );
        var waiting = execution.WaitAsync(
            [],
            stage == "sampling" ? observer : Task.CompletedTask,
            stage == "database" ? observer : Task.CompletedTask
        );
        try
        {
            await entered.Task.WaitAsync(
                TimeSpan.FromSeconds(1),
                TestContext.Current.CancellationToken
            );
            await Should.ThrowAsync<InvalidOperationException>(() =>
                waiting.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken)
            );
            execution.Snapshot().First!.Stage.ShouldBe(stage);
            execution.Snapshot().First!.ExceptionType.ShouldBe(typeof(IOException).FullName);
            JsonSerializer.Serialize(execution.Snapshot()).ShouldNotContain("private source");
        }
        finally
        {
            release.Set();
            await exited.Task.WaitAsync(
                TimeSpan.FromSeconds(1),
                TestContext.Current.CancellationToken
            );
            try
            {
                await observer;
            }
            catch { }
        }
    }

    [Theory]
    [InlineData("bad-json")]
    [InlineData("transport")]
    public async Task Actual_dispatch_fault_stops_executor_and_keeps_observed_http_status(
        string fault
    )
    {
        using var stop = new CancellationTokenSource();
        var plan = new HotelM2LoadSchedule(new("S", new(2026, 11, 6)));
        var job = plan.Jobs[0];
        using var client = new HttpClient(new BrokenWire(fault))
        {
            BaseAddress = new("https://owned.invalid"),
        };
        var dispatcher = new HotelsM2LoadDispatch(
            client,
            plan,
            () => 0,
            (_, _) => throw new InvalidOperationException()
        );
        var ledger = new HotelsLoadLedger([new(job.Id, job.Kind, job.Due)]);
        var recorded = new List<HotelM2DispatchResult>();
        var execution = new HotelsM2Execution(
            ledger,
            stop,
            () => 0,
            (_, _) => Task.CompletedTask,
            dispatcher.ExecuteAsync,
            (_, result) => recorded.Add(result)
        );
        await Should.ThrowAsync<InvalidOperationException>(() =>
            execution.WaitAsync(
                [execution.RunJobAsync(job, stop.Token)],
                Task.CompletedTask,
                Task.CompletedTask
            )
        );
        stop.IsCancellationRequested.ShouldBeTrue();
        execution.Snapshot().First!.Stage.ShouldBe("dispatch");
        recorded.Count.ShouldBe(1);
        recorded[0].Status.ShouldBe(fault == "bad-json" ? 200 : null);
        ledger
            .Snapshot()
            .Entries[0]
            .Terminal.ShouldBe(fault == "bad-json" ? "Response" : "FailedBeforeResponse");
        JsonSerializer.Serialize(execution.Snapshot()).ShouldNotContain("private");
    }

    private sealed class BrokenWire(string fault) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct
        ) =>
            fault == "transport"
                ? Task.FromException<HttpResponseMessage>(
                    new HttpRequestException("private transport data")
                )
                : Task.FromResult(
                    new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("{ invalid json private }"),
                    }
                );
    }
}
