extern alias AppHost;

using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shouldly;
using Travel.Host.Tests.Integration.Aspire;
using Travel.Modules.Flights.Infrastructure.Providers.Duffel;
using Xunit;

namespace Travel.Host.Tests.Integration;

[Trait("Category", "AspireSmoke")]
[Collection(HostIntegrationCollection.Name)]
public sealed class AspireStackSmokeTests
{
    private const string WebhookEventId = "evt_ws2_4_fresh_volume_smoke";
    private const string WebhookSecret = "ws2-4-disposable-webhook-secret";
    private const string WebhookTimestamp = "1700000000";

    [Fact]
    public async Task Smoke_webhook_fixture_uses_current_signed_v2_bytes()
    {
        using var capture = new CaptureWebhookHandler();
        using var client = new HttpClient(capture) { BaseAddress = new("http://127.0.0.1") };
        await PostWebhookAsync(client, TestContext.Current.CancellationToken);
        capture.Requests.ShouldBe(1);
        var verifier = new DuffelWebhookVerifier(
            Options.Create(new DuffelOptions { WebhookSecret = WebhookSecret })
        );
        var signatureValid = verifier.Verify(capture.Payload, capture.Signature);
        var current = verifier.IsCurrentPayload(capture.Payload);
        using var payload = JsonDocument.Parse(capture.Payload);
        TestContext.Current.TestOutputHelper?.WriteLine(
            JsonSerializer.Serialize(
                new
                {
                    signatureValid,
                    current,
                    type = payload.RootElement.GetProperty("type").GetString(),
                }
            )
        );
        signatureValid.ShouldBeTrue();
        current.ShouldBeTrue();
        payload
            .RootElement.GetProperty("type")
            .GetString()
            .ShouldBe("order.airline_initiated_change_detected");
        payload
            .RootElement.GetProperty("data")
            .GetProperty("object")
            .GetProperty("id")
            .GetString()
            .ShouldBe("ord_ws2_4_smoke");
    }

    private sealed class CaptureWebhookHandler : HttpMessageHandler
    {
        public byte[] Payload { get; private set; } = [];
        public string? Signature { get; private set; }
        public int Requests { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Requests++;
            Payload = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
            Signature = request.Headers.GetValues("X-Duffel-Signature").Single();
            return new(HttpStatusCode.OK);
        }
    }

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

    [Theory]
    [InlineData("Connection string is unavailable", "connection-string-unavailable")]
    [InlineData(
        "Npgsql.PostgresException: 28P01: password authentication failed",
        "authentication-failed"
    )]
    [InlineData("System.Net.Sockets.SocketException: Connection refused", "connection-refused")]
    [InlineData(
        "System.Net.Sockets.SocketException: Name or service not known",
        "name-resolution-failed"
    )]
    [InlineData("System.TimeoutException: Timeout during connection attempt", "timeout")]
    [InlineData("Npgsql.PostgresException: 3D000: database does not exist", "postgres-error")]
    [InlineData(
        "Npgsql.NpgsqlException: Exception while reading from stream",
        "postgres-connection-error"
    )]
    [InlineData("arbitrary failure text", "unclassified")]
    public void Diagnostic_health_reports_expose_safe_causes_only(string exception, string reason)
    {
        var report = new HealthReportSnapshot(
            "secret-check-name",
            Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy,
            "Password=secret-description",
            exception + "; Host=private-host; Password=secret-password"
        );

        var diagnostic = AspireSmokeDiagnostics.DescribeHealthReport(report);

        diagnostic.ShouldBe($"status=Unhealthy, reason={reason}");
        diagnostic.ShouldNotContain("secret");
        diagnostic.ShouldNotContain("private-host");
    }

    [Theory]
    [InlineData("LOG: database system is ready to accept connections", "ready")]
    [InlineData(
        "FATAL: password authentication failed for user secret-user",
        "authentication-failed"
    )]
    [InlineData("FATAL: arbitrary error Password=secret-password", "fatal")]
    [InlineData("Password=secret-password", "unclassified")]
    public void Diagnostic_postgres_events_expose_fixed_categories_only(
        string line,
        string category
    )
    {
        AspireSmokeDiagnostics.ClassifyPostgresLogLine(line).ShouldBe(category);
    }

    [Theory]
    [InlineData("smoke-pg-state=3D000 FATAL: hidden value", "3D000")]
    [InlineData("smoke-pg-state=28P01 FATAL: hidden value", "28P01")]
    [InlineData("smoke-pg-state=TOKEN FATAL: confidential", "other")]
    [InlineData("FATAL: missing marker", "other")]
    public void Diagnostic_fatal_codes_are_allowlisted(string line, string expected)
    {
        AspireSmokePostgresProbe.FatalState(line).ShouldBe(expected);
    }

    [Fact]
    public void Diagnostic_probe_uses_typed_errors_without_leaking_free_text()
    {
        var pg = new Npgsql.PostgresException("sensitive-value", "FATAL", "FATAL", "3D000");
        AspireSmokePostgresProbe
            .Failure(new InvalidOperationException("sensitive-value", pg))
            .ShouldBe("postgres-3D000");
        AspireSmokePostgresProbe
            .Failure(new InvalidOperationException("sensitive-value"))
            .ShouldBe("other");
        AspireSmokePostgresProbe
            .Failure(new TimeoutException("sensitive-value"))
            .ShouldBe("timeout");
    }

    [Fact]
    public async Task Diagnostic_probe_retains_completed_checks_when_another_exceeds_the_budget()
    {
        using var expired = new CancellationTokenSource();
        await expired.CancelAsync();
        var pending = new TaskCompletionSource<string>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        (await AspireSmokePostgresProbe.Observe(Task.FromResult("ok"), expired.Token)).ShouldBe(
            "ok"
        );
        (await AspireSmokePostgresProbe.Observe(pending.Task, expired.Token)).ShouldBe("cancelled");
    }

    [Fact]
    public async Task Diagnostic_logs_follow_runtime_resource_ids()
    {
        var builder = DistributedApplication.CreateBuilder(["--environment=Testing"]);
        var postgres = builder.AddContainer("postgres", "unused-diagnostic-fixture");
        await using var app = builder.Build();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var capture = AspireSmokeLogCapture.Start(app, timeout.Token, "postgres");
        await app.ResourceNotifications.PublishUpdateAsync(
            postgres.Resource,
            "postgres-runtime-instance",
            snapshot => snapshot with { State = new ResourceStateSnapshot("Running", null) }
        );
        app.Services.GetRequiredService<ResourceLoggerService>()
            .GetLogger("postgres-runtime-instance")
            .LogInformation(
                "database system is ready to accept connections Password=secret-password"
            );
        app.Services.GetRequiredService<ResourceLoggerService>()
            .GetLogger("postgres-runtime-instance")
            .LogInformation("smoke-pg-state=3D000 FATAL: database name withheld");
        using var poll = new PeriodicTimer(TimeSpan.FromMilliseconds(10));
        try
        {
            while (capture.CapturedLineCount < 2 && await poll.WaitForNextTickAsync(timeout.Token))
            { }
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested) { }

        capture.CapturedLineCount.ShouldBe(2);
        capture.PostgresFatalStates.ShouldBe("3D000:1");
        capture.PostgresEvents.ShouldBe("ready=1, authenticationFailed=0, fatal=1");
        capture.LastLine.ShouldBe("<content withheld>");
    }

    [Fact(Timeout = 300_000)]
    public async Task Fresh_stack_initializes_and_executes_persistence_backed_flows()
    {
        using var progress = AspireSmokeProgress.Begin();
        using var overallCts = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken
        );
        overallCts.CancelAfter(TimeSpan.FromSeconds(270));
        var ct = overallCts.Token;
        using var cancellation = ct.Register(() =>
            progress.Record("operation-deadline-or-cancellation")
        );
        await RunFreshStackAsync(progress, ct);
        progress.Record("disposed");
    }

    private static async Task RunFreshStackAsync(AspireSmokeProgress progress, CancellationToken ct)
    {
        progress.Record("create-builder");
        var appHost =
            await DistributedApplicationTestingBuilder.CreateAsync<AppHost::Projects.Travel_AppHost>(
                ["--environment=Testing", "UseVolumes=false"],
                cancellationToken: ct
            );

        AssertNoNamedDataVolumes(appHost);
        appHost
            .CreateResourceBuilder<PostgresServerResource>("postgres")
            .WithArgs("-c", "log_line_prefix=" + AspireSmokePostgresProbe.LogPrefix);
        appHost
            .CreateResourceBuilder<ProjectResource>("host")
            .WithEnvironment("Flights__Duffel__WebhookSecret", WebhookSecret);

        foreach (var pair in Travel.Tests.Fixtures.TestPii.Configuration)
            appHost
                .CreateResourceBuilder<ProjectResource>("host")
                .WithEnvironment(pair.Key.Replace(":", "__"), pair.Value);

        progress.Record("build");
        await using var app = await appHost.BuildAsync(ct);
        await using var logs = AspireSmokeLogCapture.Start(app, ct, "postgres", "host", "ai");
        var phase = progress.Record("start");

        try
        {
            await app.StartAsync(ct);

            phase = progress.Record("resource-health");
            await Task.WhenAll(
                RequiredHealthyResources.Select(async resourceName =>
                {
                    await app.ResourceNotifications.WaitForResourceHealthyAsync(resourceName, ct);
                    progress.Record("healthy-" + resourceName);
                })
            );

            phase = progress.Record("connection-string");
            var connectionString = await app.GetConnectionStringAsync("travel", ct);
            connectionString.ShouldNotBeNullOrWhiteSpace();

            phase = progress.Record("schema");
            await AspireSmokeDatabase.AssertInitializedSchemaAsync(connectionString, ct);
            await AspireSmokeDatabase.AssertDeterministicStateIsEmptyAsync(
                connectionString,
                WebhookEventId,
                ct
            );

            phase = progress.Record("webhook");
            using var hostClient = app.CreateHttpClient("host", "http");
            await PostWebhookAsync(hostClient, ct);
            await AspireSmokeDatabase.WaitForWebhookProcessedAsync(
                connectionString,
                WebhookEventId,
                ct
            );

            phase = progress.Record("webhook-duplicate");
            await PostWebhookAsync(hostClient, ct);
            await AspireSmokeDatabase.AssertExactlyOneProcessedWebhookAsync(
                connectionString,
                WebhookEventId,
                ct
            );

            phase = progress.Record("ai-ledger");
            await AspireSmokeDatabase.WriteAndReadAiLedgerWithProductionOptionsAsync(
                connectionString,
                ct
            );

            phase = progress.Record("status-endpoint");
            using var statusResponse = await hostClient.GetAsync("/api/status", ct);
            statusResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
            var statusBody = await statusResponse.Content.ReadAsStringAsync(ct);
            statusBody.ShouldContain("\"db\":\"ok\"");
            TestContext.Current.TestOutputHelper?.WriteLine(
                $"Aspire smoke completed. PostgresEvents[{logs.PostgresEvents}]."
            );
        }
        catch (Exception exception)
        {
            var diagnostic = AspireSmokeDiagnostics.DescribeFailure(app, logs, phase, exception);
            if (phase is "start" or "resource-health")
                diagnostic += " " + await AspireSmokePostgresProbe.OnStartupFailureAsync(app);
            progress.Record(diagnostic);
            throw new InvalidOperationException(diagnostic);
        }
        finally
        {
            progress.Record("dispose");
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
            "{\"id\":\"evt_ws2_4_fresh_volume_smoke\",\"type\":\"order.airline_initiated_change_detected\",\"api_version\":\"v2\",\"live_mode\":false,\"data\":{\"object\":{\"id\":\"ord_ws2_4_smoke\"}},\"created_at\":\"2026-08-16T00:00:00Z\"}";
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
