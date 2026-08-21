namespace Travel.Host.Tests.Integration.NlSearch;

internal sealed class TransportFixtureLifecycle : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly HashSet<Task> _pendingOperations = [];
    private readonly List<CleanupRegistration> _cleanupRegistrations = [];
    private readonly TimeSpan _cleanupTimeout;
    private Task<IReadOnlyList<Exception>>? _cleanupTask;
    private bool _disposed;

    public TransportFixtureLifecycle(TimeSpan cleanupTimeout)
    {
        _cleanupTimeout = cleanupTimeout;
    }

    public void RegisterCleanup(string resource, Func<CancellationToken, ValueTask> disposeAsync)
    {
        lock (_gate)
        {
            if (_disposed)
                throw new ObjectDisposedException(GetType().Name);

            _cleanupRegistrations.Add(new CleanupRegistration(resource, disposeAsync));
        }
    }

    public async Task RunCancellablePhaseAsync(
        string phase,
        Func<CancellationToken, Task> action,
        TimeSpan? timeout,
        CancellationToken ct
    )
    {
        using var phaseCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (timeout is { } value)
            phaseCts.CancelAfter(value);

        var operation = action(phaseCts.Token);
        Track(operation);

        try
        {
            await operation.WaitAsync(phaseCts.Token);
        }
        catch (OperationCanceledException exception)
            when (!ct.IsCancellationRequested && phaseCts.IsCancellationRequested)
        {
            throw new TimeoutException($"Transport fixture timed out during {phase}.", exception);
        }
    }

    public async Task<T> RunOwnedPhaseAsync<T>(
        string phase,
        Func<Task<T>> action,
        Func<T, CancellationToken, ValueTask> disposeLateResource,
        TimeSpan timeout,
        CancellationToken ct
    )
    {
        using var phaseCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        phaseCts.CancelAfter(timeout);

        var startup = action();
        Track(startup);

        try
        {
            return await startup.WaitAsync(phaseCts.Token);
        }
        catch (OperationCanceledException exception)
            when (!ct.IsCancellationRequested && phaseCts.IsCancellationRequested)
        {
            Track(DisposeLateResourceAsync(startup, disposeLateResource));
            throw new TimeoutException($"Transport fixture timed out during {phase}.", exception);
        }
        catch (OperationCanceledException)
        {
            Track(DisposeLateResourceAsync(startup, disposeLateResource));
            throw;
        }
    }

    public async Task<T> RunCancellableOwnedPhaseAsync<T>(
        string phase,
        Func<CancellationToken, Task<T>> action,
        Func<T, CancellationToken, ValueTask> disposeLateResource,
        TimeSpan timeout,
        CancellationToken ct
    )
    {
        using var phaseCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        phaseCts.CancelAfter(timeout);

        var startup = action(phaseCts.Token);
        Track(startup);

        try
        {
            return await startup.WaitAsync(phaseCts.Token);
        }
        catch (OperationCanceledException exception)
            when (!ct.IsCancellationRequested && phaseCts.IsCancellationRequested)
        {
            Track(DisposeLateResourceAsync(startup, disposeLateResource));
            throw new TimeoutException($"Transport fixture timed out during {phase}.", exception);
        }
        catch (OperationCanceledException)
        {
            Track(DisposeLateResourceAsync(startup, disposeLateResource));
            throw;
        }
    }

    public static async Task<string> CaptureDiagnosticsAsync(
        string diagnostic,
        Func<CancellationToken, Task<string>> capture,
        TimeSpan timeout,
        CancellationToken ct,
        int maxCharacters = 4_000
    )
    {
        const string TruncationMarker = "... [truncated]";
        ArgumentOutOfRangeException.ThrowIfLessThan(maxCharacters, TruncationMarker.Length);

        using var diagnosticCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        diagnosticCts.CancelAfter(timeout);

        try
        {
            var captureTask = capture(diagnosticCts.Token);
            Observe(captureTask);
            var result = await captureTask.WaitAsync(diagnosticCts.Token);
            return result.Length <= maxCharacters
                ? result
                : string.Concat(
                    result.AsSpan(0, maxCharacters - TruncationMarker.Length),
                    TruncationMarker
                );
        }
        catch (OperationCanceledException)
            when (!ct.IsCancellationRequested && diagnosticCts.IsCancellationRequested)
        {
            return $"{diagnostic} unavailable: timed out.";
        }
        catch (OperationCanceledException)
        {
            return $"{diagnostic} unavailable: canceled.";
        }
        catch (Exception exception)
        {
            return $"{diagnostic} unavailable: {exception.Message}";
        }
    }

    private async Task DisposeLateResourceAsync<T>(
        Task<T> startup,
        Func<T, CancellationToken, ValueTask> disposeLateResource
    )
    {
        try
        {
            var resource = await startup;
            using var cleanupCts = new CancellationTokenSource(_cleanupTimeout);
            var cleanup = disposeLateResource(resource, cleanupCts.Token).AsTask();
            Observe(cleanup);
            await cleanup.WaitAsync(cleanupCts.Token);
        }
        catch (Exception exception)
        {
            _ = exception;
        }
    }

    private void Track(Task operation)
    {
        lock (_gate)
        {
            if (_disposed)
                throw new ObjectDisposedException(GetType().Name);

            _pendingOperations.Add(operation);
        }

        _ = operation.ContinueWith(
            completed =>
            {
                _ = completed.Exception;
                lock (_gate)
                    _pendingOperations.Remove(completed);
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default
        );
    }

    private static void Observe(Task operation) =>
        _ = operation.ContinueWith(
            completed => _ = completed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default
        );

    public Task<IReadOnlyList<Exception>> DisposeBestEffortAsync()
    {
        lock (_gate)
        {
            if (_cleanupTask is not null)
                return _cleanupTask;

            _disposed = true;
            var cleanupRegistrations = _cleanupRegistrations.AsEnumerable().Reverse().ToArray();
            var pendingOperations = _pendingOperations.ToArray();
            _cleanupTask = CleanupCoreAsync(cleanupRegistrations, pendingOperations);
            return _cleanupTask;
        }
    }

    private async Task<IReadOnlyList<Exception>> CleanupCoreAsync(
        CleanupRegistration[] cleanupRegistrations,
        Task[] pendingOperations
    )
    {
        var errors = new List<Exception>();
        foreach (var registration in cleanupRegistrations)
        {
            using var operationCts = new CancellationTokenSource(_cleanupTimeout);
            Task cleanup;
            try
            {
                cleanup = registration.DisposeAsync(operationCts.Token).AsTask();
                Observe(cleanup);
            }
            catch (Exception exception)
            {
                errors.Add(
                    new InvalidOperationException(
                        $"Transport fixture cleanup failed for {registration.Resource}.",
                        exception
                    )
                );
                continue;
            }

            try
            {
                await cleanup.WaitAsync(operationCts.Token);
            }
            catch (OperationCanceledException exception) when (operationCts.IsCancellationRequested)
            {
                errors.Add(
                    new TimeoutException(
                        $"Transport fixture cleanup timed out at {registration.Resource}.",
                        exception
                    )
                );
            }
            catch (Exception exception)
            {
                errors.Add(
                    new InvalidOperationException(
                        $"Transport fixture cleanup failed for {registration.Resource}.",
                        exception
                    )
                );
            }
        }

        foreach (var operation in pendingOperations)
        {
            try
            {
                await operation.WaitAsync(_cleanupTimeout);
            }
            catch (Exception exception)
            {
                // Tracking owns observation; a stuck upstream task cannot be forcibly stopped.
                _ = exception;
            }
        }

        return errors;
    }

    public async ValueTask DisposeAsync()
    {
        var errors = await DisposeBestEffortAsync();
        if (errors.Count > 0)
            throw new AggregateException("Transport fixture cleanup failed.", errors);
    }

    private sealed record CleanupRegistration(
        string Resource,
        Func<CancellationToken, ValueTask> DisposeAsync
    );
}
