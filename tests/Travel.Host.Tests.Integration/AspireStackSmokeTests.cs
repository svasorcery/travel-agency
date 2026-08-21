extern alias AppHost;

using System.Net;
using System.Security.Cryptography;
using System.Text;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Shouldly;
using Travel.Host.Tests.Integration.Aspire;
using Xunit;

namespace Travel.Host.Tests.Integration;

[Trait("Category", "AspireSmoke")]
[Collection(HostIntegrationCollection.Name)]
public sealed class AspireStackSmokeTests
{
    private const string WebhookEventId = "evt_ws2_4_fresh_volume_smoke";
    private const string WebhookSecret = "ws2-4-disposable-webhook-secret";
    private const string WebhookTimestamp = "1700000000";

    [Theory]
    [InlineData("ANTHROPIC_API_KEY=env-secret", "env-secret")]
    [InlineData("{\"access_token\":\"json-secret\"}", "json-secret")]
    [InlineData("Authorization: Basic basic-secret", "basic-secret")]
    [InlineData("Cookie: session=cookie-secret", "cookie-secret")]
    public void Diagnostic_log_content_is_always_withheld(string line, string secret)
    {
        var sanitized = AspireSmokeLogCapture.SanitizeLogLine(line);

        sanitized.ShouldBe("<content withheld>");
        sanitized.ShouldNotContain(secret);
    }

    [Fact(Timeout = 300_000)]
    public async Task Fresh_stack_initializes_and_executes_persistence_backed_flows()
    {
        using var overallCts = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken
        );
        overallCts.CancelAfter(TimeSpan.FromSeconds(270));
        var ct = overallCts.Token;

        var appHost =
            await DistributedApplicationTestingBuilder.CreateAsync<AppHost::Projects.Travel_AppHost>(
                ["--environment=Testing", "UseVolumes=false"],
                cancellationToken: ct
            );

        AssertNoNamedDataVolumes(appHost);
        appHost
            .CreateResourceBuilder<ProjectResource>("host")
            .WithEnvironment("Flights__Duffel__WebhookSecret", WebhookSecret);

        await using var app = await appHost.BuildAsync(ct);
        await using var logs = AspireSmokeLogCapture.Start(app, ct, "host", "ai");
        var phase = "start";

        try
        {
            await app.StartAsync(ct);

            phase = "resource-health";
            await Task.WhenAll(
                RequiredHealthyResources.Select(resourceName =>
                    app.ResourceNotifications.WaitForResourceHealthyAsync(resourceName, ct)
                )
            );

            phase = "connection-string";
            var connectionString = await app.GetConnectionStringAsync("travel", ct);
            connectionString.ShouldNotBeNullOrWhiteSpace();

            phase = "schema";
            await AspireSmokeDatabase.AssertInitializedSchemaAsync(connectionString, ct);
            await AspireSmokeDatabase.AssertDeterministicStateIsEmptyAsync(
                connectionString,
                WebhookEventId,
                ct
            );

            phase = "webhook";
            using var hostClient = app.CreateHttpClient("host", "http");
            await PostWebhookAsync(hostClient, ct);
            await AspireSmokeDatabase.WaitForWebhookProcessedAsync(
                connectionString,
                WebhookEventId,
                ct
            );

            phase = "webhook-duplicate";
            await PostWebhookAsync(hostClient, ct);
            await AspireSmokeDatabase.AssertExactlyOneProcessedWebhookAsync(
                connectionString,
                WebhookEventId,
                ct
            );

            phase = "ai-ledger";
            await AspireSmokeDatabase.WriteAndReadAiLedgerWithProductionOptionsAsync(
                connectionString,
                ct
            );

            phase = "status-endpoint";
            using var statusResponse = await hostClient.GetAsync("/api/status", ct);
            statusResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
            var statusBody = await statusResponse.Content.ReadAsStringAsync(ct);
            statusBody.ShouldContain("\"db\":\"ok\"");
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                AspireSmokeDiagnostics.DescribeFailure(app, logs, phase, exception)
            );
        }
    }

    private static readonly string[] RequiredHealthyResources =
    [
        "postgres",
        "nats",
        "redis",
        "keycloak",
        "host",
        "ai",
    ];

    private static void AssertNoNamedDataVolumes(IDistributedApplicationTestingBuilder builder)
    {
        var namedVolumes = builder.Resources.SelectMany(resource =>
            resource
                .Annotations.OfType<ContainerMountAnnotation>()
                .Where(annotation => annotation.Type == ContainerMountType.Volume)
                .Select(annotation => $"{resource.Name}:{annotation.Source}")
        );

        namedVolumes.ShouldBeEmpty();
    }

    private static async Task PostWebhookAsync(HttpClient client, CancellationToken ct)
    {
        const string payload =
            "{\"id\":\"evt_ws2_4_fresh_volume_smoke\",\"type\":\"order.airline_initiated_change\",\"object\":{\"id\":\"ord_ws2_4_smoke\"},\"created_at\":\"2026-08-16T00:00:00Z\"}";
        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        var signedBytes = Encoding.UTF8.GetBytes($"{WebhookTimestamp}.{payload}");
        var signature = Convert
            .ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(WebhookSecret), signedBytes))
            .ToLowerInvariant();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/duffel")
        {
            Content = new ByteArrayContent(payloadBytes),
        };
        request.Content.Headers.ContentType = new("application/json");
        request.Headers.TryAddWithoutValidation(
            "X-Duffel-Signature",
            $"t={WebhookTimestamp},v1={signature}"
        );

        using var response = await client.SendAsync(request, ct);
        if (response.StatusCode != HttpStatusCode.OK)
            throw new HttpRequestException(null, null, response.StatusCode);
    }
}
