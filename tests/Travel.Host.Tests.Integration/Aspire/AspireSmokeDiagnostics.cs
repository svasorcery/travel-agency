using System.Net;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;

namespace Travel.Host.Tests.Integration.Aspire;

internal static class AspireSmokeDiagnostics
{
    private static readonly string[] ResourceNames =
    [
        "postgres",
        "nats",
        "redis",
        "keycloak",
        "host",
        "ai",
    ];

    public static string DescribeFailure(
        DistributedApplication app,
        AspireSmokeLogCapture logs,
        string phase,
        Exception failure
    )
    {
        var resources = string.Join(
            "; ",
            ResourceNames.Select(resourceName => DescribeResource(app, resourceName))
        );
        var httpStatus = failure is HttpRequestException { StatusCode: { } statusCode }
            ? $" HttpStatus={(int)statusCode}."
            : string.Empty;
        return $"Aspire smoke failed during {SafeDiagnosticToken(phase)}. "
            + $"ErrorType={failure.GetType().Name}. Resources: {resources}. "
            + $"CapturedLogLines={logs.CapturedLineCount}; tail={logs.LastLine}.{httpStatus}";
    }

    private static string DescribeResource(DistributedApplication app, string resourceName)
    {
        if (!app.ResourceNotifications.TryGetCurrentState(resourceName, out var resource))
            return $"{resourceName}=unknown";

        var snapshot = resource.Snapshot;
        return $"{resourceName}[state={SafeDiagnosticToken(snapshot.State?.Text)}, "
            + $"health={snapshot.HealthStatus?.ToString() ?? "unknown"}, "
            + $"exitCode={snapshot.ExitCode?.ToString() ?? "none"}]";
    }

    private static string SafeDiagnosticToken(string? value) =>
        value
            is "start"
                or "resource-health"
                or "connection-string"
                or "schema"
                or "webhook"
                or "webhook-duplicate"
                or "ai-ledger"
                or "status-endpoint"
                or "Starting"
                or "Running"
                or "Finished"
                or "FailedToStart"
                or "Waiting"
                or "NotStarted"
                or "Stopping"
                or "Stopped"
            ? value
            : "unknown";
}

internal sealed class AspireSmokeLogCapture : IAsyncDisposable
{
    private readonly CancellationTokenSource _cts;
    private readonly Task _captureTask;
    private int _capturedLineCount;
    private string _lastLine = "<empty>";

    private AspireSmokeLogCapture(
        ResourceLoggerService logger,
        CancellationToken testToken,
        string[] resourceNames
    )
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(testToken);
        _captureTask = Task.WhenAll(
            resourceNames.Select(resourceName => CaptureAsync(logger, resourceName))
        );
    }

    public static AspireSmokeLogCapture Start(
        DistributedApplication app,
        CancellationToken ct,
        params string[] resourceNames
    ) => new(app.Services.GetRequiredService<ResourceLoggerService>(), ct, resourceNames);

    public int CapturedLineCount => Volatile.Read(ref _capturedLineCount);

    public string LastLine => Volatile.Read(ref _lastLine);

    public static string SanitizeLogLine(string _) => "<content withheld>";

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        try
        {
            await _captureTask;
        }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested) { }
        finally
        {
            _cts.Dispose();
        }
    }

    private async Task CaptureAsync(ResourceLoggerService logger, string resourceName)
    {
        await foreach (var batch in logger.WatchAsync(resourceName).WithCancellation(_cts.Token))
        {
            foreach (var line in batch)
            {
                Volatile.Write(ref _lastLine, SanitizeLogLine(line.Content));
                Interlocked.Increment(ref _capturedLineCount);
            }
        }
    }
}
