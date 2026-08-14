using System.Collections.Concurrent;
using System.Diagnostics;
using Shouldly;
using Xunit;

namespace Travel.Host.Tests.Integration.NlSearch;

public sealed class TransportFixtureLifecycleTests
{
    [Fact]
    public async Task Cancellable_phase_receives_linked_token_and_observes_a_late_fault()
    {
        await using var lifecycle = new TransportFixtureLifecycle(TimeSpan.FromMilliseconds(250));
        using var callerCts = new CancellationTokenSource();
        var operation = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var receivedToken = CancellationToken.None;

        var exception = await Should.ThrowAsync<TimeoutException>(() =>
            lifecycle.RunCancellablePhaseAsync(
                "cancellable phase",
                token =>
                {
                    receivedToken = token;
                    return operation.Task;
                },
                TimeSpan.FromMilliseconds(25),
                callerCts.Token
            )
        );

        exception.Message.ShouldBe("Transport fixture timed out during cancellable phase.");
        receivedToken.CanBeCanceled.ShouldBeTrue();
        receivedToken.IsCancellationRequested.ShouldBeTrue();

        operation.SetException(new InvalidOperationException("late failure"));
    }

    [Fact]
    public async Task Caller_cancellation_is_not_reported_as_a_phase_timeout()
    {
        await using var lifecycle = new TransportFixtureLifecycle(TimeSpan.FromMilliseconds(250));
        using var callerCts = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken
        );
        var operation = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        callerCts.CancelAfter(TimeSpan.FromMilliseconds(25));

        var exception = await Should.ThrowAsync<OperationCanceledException>(() =>
            lifecycle.RunCancellablePhaseAsync(
                "caller-canceled phase",
                _ => operation.Task,
                TimeSpan.FromSeconds(1),
                callerCts.Token
            )
        );

        exception.ShouldNotBeOfType<TimeoutException>();
        operation.SetCanceled(callerCts.Token);
    }

    [Fact]
    public async Task Owned_phase_disposes_a_resource_that_completes_after_timeout()
    {
        await using var lifecycle = new TransportFixtureLifecycle(TimeSpan.FromMilliseconds(250));
        var startup = new TaskCompletionSource<FakeAsyncDisposable>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        await Should.ThrowAsync<TimeoutException>(() =>
            lifecycle.RunOwnedPhaseAsync(
                "host startup",
                () => startup.Task,
                static (resource, token) => resource.DisposeAsync(token),
                TimeSpan.FromMilliseconds(25),
                TestContext.Current.CancellationToken
            )
        );

        var resource = new FakeAsyncDisposable();
        startup.SetResult(resource);

        await resource.Disposed.Task.WaitAsync(
            TimeSpan.FromSeconds(1),
            TestContext.Current.CancellationToken
        );
        resource.DisposeCount.ShouldBe(1);
    }

    [Fact]
    public async Task Cancellable_owned_phase_disposes_a_late_resource_with_its_linked_token()
    {
        await using var lifecycle = new TransportFixtureLifecycle(TimeSpan.FromMilliseconds(250));
        var startup = new TaskCompletionSource<FakeAsyncDisposable>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var receivedToken = CancellationToken.None;

        await Should.ThrowAsync<TimeoutException>(() =>
            lifecycle.RunCancellableOwnedPhaseAsync(
                "subscription startup",
                token =>
                {
                    receivedToken = token;
                    return startup.Task;
                },
                static (resource, token) => resource.DisposeAsync(token),
                TimeSpan.FromMilliseconds(25),
                TestContext.Current.CancellationToken
            )
        );

        receivedToken.IsCancellationRequested.ShouldBeTrue();
        var resource = new FakeAsyncDisposable();
        startup.SetResult(resource);

        await resource.Disposed.Task.WaitAsync(
            TimeSpan.FromSeconds(1),
            TestContext.Current.CancellationToken
        );
        resource.DisposeCount.ShouldBe(1);
    }

    [Fact]
    public async Task Cleanup_is_reverse_bounded_idempotent_and_attempts_every_resource()
    {
        var lifecycle = new TransportFixtureLifecycle(TimeSpan.FromMilliseconds(75));
        var calls = new ConcurrentQueue<string>();
        var neverCompletes = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        lifecycle.RegisterCleanup(
            "first",
            _ =>
            {
                calls.Enqueue("first");
                return ValueTask.CompletedTask;
            }
        );
        lifecycle.RegisterCleanup(
            "second",
            _ =>
            {
                calls.Enqueue("second");
                throw new InvalidOperationException("second failed");
            }
        );
        lifecycle.RegisterCleanup(
            "third",
            _ =>
            {
                calls.Enqueue("third");
                return new ValueTask(neverCompletes.Task);
            }
        );

        var stopwatch = Stopwatch.StartNew();
        var errors = await lifecycle.DisposeBestEffortAsync();
        stopwatch.Stop();

        calls.ToArray().ShouldBe(["third", "second", "first"]);
        errors.Count.ShouldBe(2);
        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(1));

        var repeatedErrors = await lifecycle.DisposeBestEffortAsync();
        calls.Count.ShouldBe(3);
        repeatedErrors.ShouldBeSameAs(errors);

        neverCompletes.SetException(new InvalidOperationException("late cleanup failure"));
    }

    [Fact]
    public async Task Diagnostic_collection_is_bounded_and_cancels_its_linked_token()
    {
        var diagnostics = new TaskCompletionSource<string>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var receivedToken = CancellationToken.None;
        var stopwatch = Stopwatch.StartNew();

        var result = await TransportFixtureLifecycle.CaptureDiagnosticsAsync(
            "NATS logs",
            token =>
            {
                receivedToken = token;
                return diagnostics.Task;
            },
            TimeSpan.FromMilliseconds(25),
            TestContext.Current.CancellationToken
        );
        stopwatch.Stop();

        result.ShouldBe("NATS logs unavailable: timed out.");
        receivedToken.IsCancellationRequested.ShouldBeTrue();
        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(1));

        diagnostics.SetException(new InvalidOperationException("late diagnostics failure"));
    }

    [Fact]
    public async Task Diagnostic_collection_returns_failure_text_instead_of_throwing()
    {
        var result = await TransportFixtureLifecycle.CaptureDiagnosticsAsync(
            "NATS logs",
            _ => Task.FromException<string>(new InvalidOperationException("daemon unavailable")),
            TimeSpan.FromSeconds(1),
            TestContext.Current.CancellationToken
        );

        result.ShouldBe("NATS logs unavailable: daemon unavailable");
    }

    [Fact]
    public async Task Diagnostic_collection_truncates_successful_output_to_its_size_bound()
    {
        var result = await TransportFixtureLifecycle.CaptureDiagnosticsAsync(
            "NATS logs",
            _ => Task.FromResult("abcdefghijklmnopqrstuvwxyz"),
            TimeSpan.FromSeconds(1),
            TestContext.Current.CancellationToken,
            maxCharacters: 20
        );

        result.ShouldBe("abcde... [truncated]");
    }

    private sealed class FakeAsyncDisposable
    {
        public TaskCompletionSource Disposed { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int DisposeCount { get; private set; }

        public ValueTask DisposeAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            DisposeCount++;
            Disposed.TrySetResult();
            return ValueTask.CompletedTask;
        }
    }
}
