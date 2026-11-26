using System.Collections.Concurrent;
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
            + $"CapturedLogLines={logs.CapturedLineCount}; tail={logs.LastLine}. "
            + $"PostgresEvents[{logs.PostgresEvents}]. PostgresFatalStates[{logs.PostgresFatalStates}].{httpStatus}";
    }

    private static string DescribeResource(DistributedApplication app, string resourceName)
    {
        if (!app.ResourceNotifications.TryGetCurrentState(resourceName, out var resource))
            return $"{resourceName}=unknown";

        var snapshot = resource.Snapshot;
        var healthReports = snapshot.HealthReports.IsDefaultOrEmpty
            ? "none"
            : string.Join("; ", snapshot.HealthReports.Take(4).Select(DescribeHealthReport));
        return $"{resourceName}[state={SafeDiagnosticToken(snapshot.State?.Text)}, "
            + $"health={snapshot.HealthStatus?.ToString() ?? "unknown"}, "
            + $"exitCode={snapshot.ExitCode?.ToString() ?? "none"}, checks=({healthReports})]";
    }

    internal static string DescribeHealthReport(HealthReportSnapshot report)
    {
        // Health descriptions and exception messages can contain connection strings or credentials.
        // Emit only fixed categories; never echo the check name or free-form source text.
        var exception = report.ExceptionText ?? string.Empty;
        var reason = exception switch
        {
            _ when exception.Contains(
                    "Connection string is unavailable",
                    StringComparison.OrdinalIgnoreCase
                ) => "connection-string-unavailable",
            _ when exception.Contains(
                    "password authentication failed",
                    StringComparison.OrdinalIgnoreCase
                ) => "authentication-failed",
            _ when exception.Contains("Connection refused", StringComparison.OrdinalIgnoreCase) =>
                "connection-refused",
            _ when exception.Contains(
                    "Name or service not known",
                    StringComparison.OrdinalIgnoreCase
                )
                    || exception.Contains(
                        "No such host is known",
                        StringComparison.OrdinalIgnoreCase
                    ) => "name-resolution-failed",
            _ when exception.Contains("TimeoutException", StringComparison.OrdinalIgnoreCase)
                    || exception.Contains("timed out", StringComparison.OrdinalIgnoreCase) =>
                "timeout",
            _ when exception.Contains("Npgsql.PostgresException", StringComparison.Ordinal) =>
                "postgres-error",
            _ when exception.Contains("Npgsql.NpgsqlException", StringComparison.Ordinal) =>
                "postgres-connection-error",
            "" => "not-reported",
            _ => "unclassified",
        };
        return $"status={report.Status?.ToString() ?? "unknown"}, reason={reason}";
    }

    internal static string ClassifyPostgresLogLine(string line) =>
        line switch
        {
            _ when line.Contains(
                    "password authentication failed",
                    StringComparison.OrdinalIgnoreCase
                ) => "authentication-failed",
            _ when line.Contains("FATAL:", StringComparison.Ordinal) => "fatal",
            _ when line.Contains(
                    "database system is ready to accept connections",
                    StringComparison.Ordinal
                ) => "ready",
            _ => "unclassified",
        };

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
    private readonly ConcurrentDictionary<string, int> _fatalStates = new(StringComparer.Ordinal);
    private int _postgresReadyCount;
    private int _postgresAuthenticationFailureCount;
    private int _postgresFatalCount;
    private string _lastLine = "<empty>";

    private AspireSmokeLogCapture(
        ResourceLoggerService logger,
        ResourceNotificationService notifications,
        CancellationToken testToken,
        string[] resourceNames
    )
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(testToken);
        _captureTask = CaptureResourcesAsync(logger, notifications, resourceNames);
    }

    public static AspireSmokeLogCapture Start(
        DistributedApplication app,
        CancellationToken ct,
        params string[] resourceNames
    ) =>
        new(
            app.Services.GetRequiredService<ResourceLoggerService>(),
            app.ResourceNotifications,
            ct,
            resourceNames
        );

    public int CapturedLineCount => Volatile.Read(ref _capturedLineCount);

    public string LastLine => Volatile.Read(ref _lastLine);

    public string PostgresEvents =>
        $"ready={Volatile.Read(ref _postgresReadyCount)}, "
        + $"authenticationFailed={Volatile.Read(ref _postgresAuthenticationFailureCount)}, "
        + $"fatal={Volatile.Read(ref _postgresFatalCount)}";

    public string PostgresFatalStates =>
        string.Join(
            ",",
            _fatalStates
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => $"{pair.Key}:{pair.Value}")
        );

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

    private async Task CaptureResourcesAsync(
        ResourceLoggerService logger,
        ResourceNotificationService notifications,
        string[] resourceNames
    )
    {
        var watchedIds = new HashSet<string>(resourceNames, StringComparer.Ordinal);
        var captures = resourceNames.Select(name => CaptureAsync(logger, name, name)).ToList();
        try
        {
            await foreach (var resource in notifications.WatchAsync(_cts.Token))
            {
                if (
                    resourceNames.Contains(resource.Resource.Name, StringComparer.Ordinal)
                    && watchedIds.Add(resource.ResourceId)
                )
                {
                    captures.Add(CaptureAsync(logger, resource.ResourceId, resource.Resource.Name));
                }
            }
        }
        finally
        {
            await Task.WhenAll(captures);
        }
    }

    private async Task CaptureAsync(
        ResourceLoggerService logger,
        string resourceId,
        string resourceName
    )
    {
        await foreach (var batch in logger.WatchAsync(resourceId).WithCancellation(_cts.Token))
        {
            foreach (var line in batch)
            {
                if (resourceName == "postgres")
                {
                    if (line.Content.Contains("FATAL:", StringComparison.Ordinal))
                        _fatalStates.AddOrUpdate(
                            AspireSmokePostgresProbe.FatalState(line.Content),
                            1,
                            static (_, count) => count + 1
                        );
                    switch (AspireSmokeDiagnostics.ClassifyPostgresLogLine(line.Content))
                    {
                        case "ready":
                            Interlocked.Increment(ref _postgresReadyCount);
                            break;
                        case "authentication-failed":
                            Interlocked.Increment(ref _postgresAuthenticationFailureCount);
                            break;
                        case "fatal":
                            Interlocked.Increment(ref _postgresFatalCount);
                            break;
                    }
                }
                Volatile.Write(ref _lastLine, SanitizeLogLine(line.Content));
                Interlocked.Increment(ref _capturedLineCount);
            }
        }
    }
}
