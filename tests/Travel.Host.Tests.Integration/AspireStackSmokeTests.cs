extern alias AppHost;

using Aspire.Hosting;
using Aspire.Hosting.Testing;
using Shouldly;
using Xunit;

namespace Travel.Host.Tests.Integration;

[Trait("Category", "AspireSmoke")]
[Collection(HostIntegrationCollection.Name)]
public class AspireStackSmokeTests
{
    [Fact(Timeout = 180_000)]
    public async Task Full_stack_boots_and_status_endpoint_responds()
    {
        var ct = TestContext.Current.CancellationToken;

        var appHost =
            await DistributedApplicationTestingBuilder.CreateAsync<AppHost::Projects.Travel_AppHost>(
                cancellationToken: ct
            );

        await using var app = await appHost.BuildAsync(ct);
        await app.StartAsync(ct);

        // The status endpoint queries PostgreSQL, so both resources must be ready before polling it.
        // Use a 120-second timeout linked to the test's cancellation token.
        using var healthCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        healthCts.CancelAfter(TimeSpan.FromSeconds(120));
        await Task.WhenAll(
            app.ResourceNotifications.WaitForResourceHealthyAsync("postgres", healthCts.Token),
            app.ResourceNotifications.WaitForResourceHealthyAsync("host", healthCts.Token)
        );

        var http = app.CreateHttpClient("host");

        // Host resource being healthy doesn't guarantee Postgres is reachable from the host process
        // (CI containers are slower to warm up than local). Poll /api/status with backoff up to 60s.
        using var pollCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        pollCts.CancelAfter(TimeSpan.FromSeconds(60));
        HttpResponseMessage? response = null;
        Exception? lastError = null;
        while (!pollCts.Token.IsCancellationRequested)
        {
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

        response.ShouldNotBeNull($"Got no successful response within 60s. Last error: {lastError}");
        response!.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync(ct);
        body.ShouldContain("\"db\":\"ok\"");
    }
}
