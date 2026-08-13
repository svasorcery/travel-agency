extern alias AppHost;
using System.Collections.Concurrent;

using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Travel.Host.Tests.Integration;

[Trait("Category", "AspireSmoke")]
[Collection(HostIntegrationCollection.Name)]
public class AspireStackSmokeTests
{
    private const int MaxDiagnosticLogLines = 40;

    [Fact(Timeout = 180_000)]
    public async Task Full_stack_boots_and_status_endpoint_responds()
    {
        var ct = TestContext.Current.CancellationToken;

        var appHost =
            await DistributedApplicationTestingBuilder.CreateAsync<AppHost::Projects.Travel_AppHost>(
                cancellationToken: ct
            );

        await using var app = await appHost.BuildAsync(ct);
        await using var hostLogs = HostLogCapture.Start(app, ct);
        await app.StartAsync(ct);

        // The status endpoint queries PostgreSQL, so both resources must be ready before polling it.
        // Use a 120-second timeout linked to the test's cancellation token.
        using var healthCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        healthCts.CancelAfter(TimeSpan.FromSeconds(120));
        await Task.WhenAll(
            app.ResourceNotifications.WaitForResourceHealthyAsync("postgres", healthCts.Token),
            app.ResourceNotifications.WaitForResourceHealthyAsync("host", healthCts.Token)
        );

        var http = app.CreateHttpClient("host", "http");
        http.BaseAddress.ShouldNotBeNull();
        http.BaseAddress.Scheme.ShouldBe(Uri.UriSchemeHttp);

        // Host resource being healthy doesn't guarantee Postgres is reachable from the host process
        // (CI containers are slower to warm up than local). Poll /api/status with backoff up to 120s.
        using var pollCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        pollCts.CancelAfter(TimeSpan.FromSeconds(120));
        HttpResponseMessage? response = null;
        Exception? lastError = null;
        while (!pollCts.Token.IsCancellationRequested)
        {
            if (HasFinished(app, "host"))
            {
                lastError = new InvalidOperationException("The host resource exited.");
                break;
            }

            try
            {
                using var requestCts = CancellationTokenSource.CreateLinkedTokenSource(
                    pollCts.Token
                );
                requestCts.CancelAfter(TimeSpan.FromSeconds(10));
                response = await http.GetAsync("/api/status", requestCts.Token);
                if (response.IsSuccessStatusCode)
                    break;

                lastError = new HttpRequestException(
                    $"Status endpoint returned {(int)response.StatusCode} ({response.StatusCode})."
                );
                response.Dispose();
                response = null;
            }
            catch (OperationCanceledException ex) when (!pollCts.Token.IsCancellationRequested)
            {
                lastError = new TimeoutException(
                    "A status request did not complete within 10 seconds.",
                    ex
                );
            }
            catch (Exception ex)
            {
                lastError = ex;
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(2), pollCts.Token);
            }
            catch (OperationCanceledException) when (pollCts.Token.IsCancellationRequested)
            {
                break;
            }
        }

        if (response is null)
        {
            var diagnostics = DescribeFailure(app, hostLogs);
            response.ShouldNotBeNull(
                $"Got no successful response within 120s. Last error: {lastError}. " + diagnostics
            );
        }

        response!.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync(ct);
        body.ShouldContain("\"db\":\"ok\"");
    }

    private static string DescribeResource(DistributedApplication app, string resourceName)
    {
        if (!app.ResourceNotifications.TryGetCurrentState(resourceName, out var resource))
            return $"{resourceName}=unknown";

        var snapshot = resource.Snapshot;
        return $"{resourceName}[state={snapshot.State?.Text ?? "unknown"}, "
            + $"health={snapshot.HealthStatus?.ToString() ?? "unknown"}, "
            + $"exitCode={snapshot.ExitCode?.ToString() ?? "none"}]";
    }

    private static bool HasFinished(DistributedApplication app, string resourceName) =>
        app.ResourceNotifications.TryGetCurrentState(resourceName, out var resource)
        && string.Equals(resource.Snapshot.State?.Text, "Finished", StringComparison.Ordinal);

    private static string DescribeFailure(DistributedApplication app, HostLogCapture hostLogs)
    {
        var resources =
            $"Resources: {DescribeResource(app, "postgres")}; {DescribeResource(app, "host")}";
        var tail = hostLogs.GetTail();

        return tail.Length == 0
            ? $"{resources}. Host log tail: <empty>"
            : $"{resources}. Host log tail:{Environment.NewLine}{string.Join(Environment.NewLine, tail)}";
    }

    private static string SanitizeLogLine(string line)
    {
        var sanitized = System.Text.RegularExpressions.Regex.Replace(
            line,
            """(?i)\b(password|pwd|api[_-]?key|token|secret|client[_-]?secret)\s*[=:]\s*(?:"[^"]*"|'[^']*'|[^;\s,}]+)""",
            "$1=<redacted>"
        );
        sanitized = System.Text.RegularExpressions.Regex.Replace(
            sanitized,
            @"(?i)([a-z][a-z0-9+.-]*://)([^/\s:@]+):([^@/\s]+)@",
            "$1<redacted>@"
        );
        sanitized = System.Text.RegularExpressions.Regex.Replace(
            sanitized,
            @"(?i)\bBearer\s+[A-Za-z0-9._~+/=-]+",
            "Bearer <redacted>"
        );

        return sanitized.Length <= 1_000 ? sanitized : sanitized[..1_000] + "...<truncated>";
    }

    private sealed class HostLogCapture : IAsyncDisposable
    {
        private readonly ConcurrentQueue<string> _tail = new();
        private readonly CancellationTokenSource _cts;
        private readonly Task _captureTask;

        private HostLogCapture(ResourceLoggerService logger, CancellationToken testToken)
        {
            _cts = CancellationTokenSource.CreateLinkedTokenSource(testToken);
            _captureTask = CaptureAsync(logger);
        }

        public static HostLogCapture Start(DistributedApplication app, CancellationToken ct) =>
            new(app.Services.GetRequiredService<ResourceLoggerService>(), ct);

        public string[] GetTail() => _tail.ToArray();

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

        private async Task CaptureAsync(ResourceLoggerService logger)
        {
            await foreach (var batch in logger.WatchAsync("host").WithCancellation(_cts.Token))
            {
                foreach (var line in batch)
                {
                    _tail.Enqueue(SanitizeLogLine(line.Content));
                    while (_tail.Count > MaxDiagnosticLogLines)
                        _tail.TryDequeue(out _);
                }
            }
        }
    }
}
