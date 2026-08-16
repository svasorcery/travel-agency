using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Travel.Shared.Infrastructure.Initialization;

/// <summary>
/// Executes module initializers in a deterministic order and exposes their startup state.
/// </summary>
public sealed class AppInitializer(
    IServiceProvider services,
    IHostEnvironment environment,
    ILogger<AppInitializer> logger
) : IHostedService
{
    private readonly object _sync = new();
    private readonly List<InitializerStatus> _initializers = [];
    private Task? _startTask;
    private InitializationState _state = InitializationState.Pending;

    public InitializationState State
    {
        get
        {
            lock (_sync)
            {
                return _state;
            }
        }
    }

    /// <summary>
    /// A secret-safe snapshot of each initializer observed during startup.
    /// </summary>
    public IReadOnlyList<InitializerStatus> Initializers
    {
        get
        {
            lock (_sync)
            {
                return _initializers.ToArray();
            }
        }
    }

    public Task StartAsync(CancellationToken ct)
    {
        lock (_sync)
        {
            return _startTask ??= StartCoreAsync(ct);
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    private async Task StartCoreAsync(CancellationToken ct)
    {
        using var scope = services.CreateScope();
        var initializers = scope
            .ServiceProvider.GetServices<IInitializer>()
            .OrderBy(initializer => initializer.Phase)
            .ThenBy(initializer => initializer.GetType().FullName, StringComparer.Ordinal)
            .Select(initializer => new RegisteredInitializer(initializer, GetName(initializer)))
            .ToArray();

        lock (_sync)
        {
            _state = InitializationState.Running;
            _initializers.AddRange(
                initializers.Select(initializer => new InitializerStatus(
                    initializer.Name,
                    initializer.Initializer.Phase,
                    InitializationState.Pending,
                    null
                ))
            );
        }

        logger.LogInformation("Running {Count} initializer(s)", initializers.Length);

        for (var index = 0; index < initializers.Length; index++)
        {
            var registered = initializers[index];
            logger.LogInformation("Initializing: {Name}", registered.Name);
            SetInitializerState(index, InitializationState.Running, null);

            try
            {
                await registered.Initializer.InitializeAsync(ct);
                SetInitializerState(index, InitializationState.Succeeded, null);
                logger.LogInformation("Done: {Name}", registered.Name);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                SetInitializerState(index, InitializationState.Cancelled, null);
                SetOverallState(InitializationState.Cancelled);
                logger.LogInformation("Initialization cancelled: {Name}", registered.Name);
                throw;
            }
            catch (Exception exception)
            {
                var errorType = GetSafeErrorType(exception);
                SetInitializerState(index, InitializationState.Failed, errorType);
                SetOverallState(InitializationState.Failed);
                logger.LogError(
                    "Initialization failed: {Name}; error type: {ErrorType}",
                    registered.Name,
                    errorType
                );

                if (!environment.IsProduction())
                    throw;

                return;
            }
        }

        SetOverallState(InitializationState.Succeeded);
    }

    private void SetInitializerState(int index, InitializationState state, string? errorType)
    {
        lock (_sync)
        {
            var current = _initializers[index];
            _initializers[index] = current with { State = state, ErrorType = errorType };
        }
    }

    private void SetOverallState(InitializationState state)
    {
        lock (_sync)
        {
            _state = state;
        }
    }

    private static string GetName(IInitializer initializer) =>
        initializer.GetType().FullName ?? initializer.GetType().Name;

    private static string GetSafeErrorType(Exception exception) => exception.GetType().Name;

    private sealed record RegisteredInitializer(IInitializer Initializer, string Name);
}

/// <summary>
/// Secret-safe status for one initializer. Exception messages and stack traces are intentionally omitted.
/// </summary>
public sealed record InitializerStatus(
    string Name,
    InitializationPhase Phase,
    InitializationState State,
    string? ErrorType
);
