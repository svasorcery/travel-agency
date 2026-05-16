extern alias AppHost;

using Aspire.Hosting;
using Aspire.Hosting.Testing;
using Shouldly;
using Xunit;

namespace Travel.Host.Tests.Integration;

[Trait("Category", "AspireSmoke")]
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

        // Wait until the host resource reports healthy (Aspire health probe).
        // Use a 120-second timeout linked to the test's cancellation token.
        using var healthCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        healthCts.CancelAfter(TimeSpan.FromSeconds(120));
        await app.ResourceNotifications.WaitForResourceHealthyAsync("host", healthCts.Token);

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
                response = await http.GetAsync("/api/status", pollCts.Token);
                if (response.IsSuccessStatusCode)
                    break;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                lastError = ex;
            }
            await Task.Delay(TimeSpan.FromSeconds(2), pollCts.Token);
        }

        response.ShouldNotBeNull($"Got no successful response within 60s. Last error: {lastError}");
        response!.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync(ct);
        body.ShouldContain("\"db\":\"ok\"");
    }
}
