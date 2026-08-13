extern alias AppHost;

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
    [Theory]
    [InlineData("ANTHROPIC_API_KEY=env-secret", "env-secret")]
    [InlineData("{\"access_token\":\"json-secret\"}", "json-secret")]
    [InlineData("Authorization: Basic basic-secret", "basic-secret")]
    [InlineData("Cookie: session=cookie-secret", "cookie-secret")]
    public void Diagnostic_log_content_is_always_withheld(string line, string secret)
    {
        var sanitized = SanitizeLogLine(line);

        sanitized.ShouldBe("<content withheld>");
        sanitized.ShouldNotContain(secret);
    }

    [Fact(Timeout = 300_000)]
    public async Task Full_stack_boots_and_status_endpoint_responds()
    {
        using var overallCts = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken
        );
        overallCts.CancelAfter(TimeSpan.FromSeconds(270));
        var ct = overallCts.Token;

        var appHost =
            await DistributedApplicationTestingBuilder.CreateAsync<AppHost::Projects.Travel_AppHost>(
                cancellationToken: ct
            );

        await using var app = await appHost.BuildAsync(ct);
        await using var hostLogs = HostLogCapture.Start(app, ct);
        var phase = "start";
        Exception? failure = null;
        try
        {
            await app.StartAsync(ct);

            phase = "resource-health";
            await Task.WhenAll(
                app.ResourceNotifications.WaitForResourceHealthyAsync("postgres", ct),
                app.ResourceNotifications.WaitForResourceHealthyAsync("host", ct)
            );

            phase = "status-endpoint";
            using var http = app.CreateHttpClient("host", "http");
            http.BaseAddress.ShouldNotBeNull();
            http.BaseAddress.Scheme.ShouldBe(Uri.UriSchemeHttp);

            HttpResponseMessage? response = null;
            Exception? lastError = null;
            while (!ct.IsCancellationRequested)
            {
                if (HasFinished(app, "host"))
                {
                    lastError = new InvalidOperationException("The host resource exited.");
                    break;
                }

                try
                {
                    using var requestCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    requestCts.CancelAfter(TimeSpan.FromSeconds(10));
                    response = await http.GetAsync("/api/status", requestCts.Token);
                    if (response.IsSuccessStatusCode)
                        break;

                    lastError = new HttpRequestException(
                        $"Status endpoint returned {(int)response.StatusCode}."
                    );
                    response.Dispose();
                    response = null;
                }
                catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
                {
                    lastError = new TimeoutException("A status request timed out.", ex);
                }
                catch (Exception ex)
                {
                    lastError = ex;
                }

                await Task.Delay(TimeSpan.FromSeconds(2), ct);
            }

            if (response is null)
            {
                failure = lastError;
            }
            else
            {
                using (response)
                {
                    var body = await response.Content.ReadAsStringAsync(ct);
                    body.ShouldContain("\"db\":\"ok\"");
                }

                return;
            }
        }
        catch (Exception ex)
        {
            failure = ex;
        }

        throw new InvalidOperationException(DescribeFailure(app, hostLogs, phase, failure));
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

    private static bool HasFinished(DistributedApplication app, string resourceName) =>
        app.ResourceNotifications.TryGetCurrentState(resourceName, out var resource)
        && string.Equals(resource.Snapshot.State?.Text, "Finished", StringComparison.Ordinal);

    private static string DescribeFailure(
        DistributedApplication app,
        HostLogCapture hostLogs,
        string phase,
        Exception? failure
    )
    {
        var resources =
            $"Resources: {DescribeResource(app, "postgres")}; {DescribeResource(app, "host")}";
        var errorType = failure?.GetType().Name ?? "none";
        return $"Aspire smoke failed during {SafeDiagnosticToken(phase)}. ErrorType={errorType}. "
            + $"{resources}. Host logs captured={hostLogs.CapturedLineCount}; "
            + $"tail={hostLogs.LastLine}.";
    }

    private static string SafeDiagnosticToken(string? value) =>
        value
            is "start"
                or "resource-health"
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

    private static string SanitizeLogLine(string _) => "<content withheld>";

    private sealed class HostLogCapture : IAsyncDisposable
    {
        private readonly CancellationTokenSource _cts;
        private readonly Task _captureTask;
        private int _capturedLineCount;
        private string _lastLine = "<empty>";

        private HostLogCapture(ResourceLoggerService logger, CancellationToken testToken)
        {
            _cts = CancellationTokenSource.CreateLinkedTokenSource(testToken);
            _captureTask = CaptureAsync(logger);
        }

        public static HostLogCapture Start(DistributedApplication app, CancellationToken ct) =>
            new(app.Services.GetRequiredService<ResourceLoggerService>(), ct);

        public int CapturedLineCount => Volatile.Read(ref _capturedLineCount);

        public string LastLine => Volatile.Read(ref _lastLine);

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
                    Volatile.Write(ref _lastLine, SanitizeLogLine(line.Content));
                    Interlocked.Increment(ref _capturedLineCount);
                }
            }
        }
    }
}
