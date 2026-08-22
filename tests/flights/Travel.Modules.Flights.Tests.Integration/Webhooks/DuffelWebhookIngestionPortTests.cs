using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ErrorOr;
using JasperFx;
using Marten;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Shouldly;
using Testcontainers.PostgreSql;
using Travel.Modules.Flights.Api.Composition;
using Travel.Modules.Flights.Application.Commands;
using Travel.Modules.Flights.Application.Observability;
using Travel.Modules.Flights.Application.Webhooks;
using Travel.Modules.Flights.Infrastructure.Persistence;
using Travel.Modules.Flights.Infrastructure.Persistence.Entities;
using Travel.Modules.Flights.Infrastructure.Providers.Duffel;
using Travel.Modules.Flights.Infrastructure.Webhooks;
using Travel.Modules.Flights.Tests.Integration.Outbox;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.Marten;
using Wolverine.Runtime;
using Wolverine.Tracking;
using Xunit;

namespace Travel.Modules.Flights.Tests.Integration.Webhooks;

[Trait("Category", "Integration")]
public sealed class DuffelWebhookIngestionPortTests
    : IClassFixture<DuffelWebhookIngestionPortFixture>
{
    private readonly DuffelWebhookIngestionPortFixture _fixture;

    public DuffelWebhookIngestionPortTests(DuffelWebhookIngestionPortFixture fixture) =>
        _fixture = fixture;

    [Fact]
    public async Task Signed_delivery_inserts_one_inbox_row_and_delivers_one_command()
    {
        var ct = TestContext.Current.CancellationToken;
        var eventId = "wh_port_" + Guid.NewGuid().ToString("N");
        var request = _fixture.CreateSignedRequest(eventId, "x-DuFfEl-SiGnAtUrE");

        await using var scope = _fixture.Host.Services.CreateAsyncScope();
        var port = scope.ServiceProvider.GetRequiredService<IWebhookIngestionPort>();
        var result = default(ErrorOr<WebhookIngestionOutcome>);

        await _fixture
            .Host.TrackActivity()
            .Timeout(TimeSpan.FromSeconds(30))
            .WaitForMessageToBeReceivedAt<ProcessDuffelWebhookCommand>(_fixture.Host)
            .ExecuteAndWaitAsync(
                (Func<IMessageContext, Task>)(
                    async _ =>
                    {
                        result = await port.IngestAsync(request, ct);
                    }
                )
            );

        result.IsError.ShouldBeFalse();
        result.Value.ShouldBe(WebhookIngestionOutcome.Accepted);

        await using var verifyScope = _fixture.Host.Services.CreateAsyncScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<FlightsDbContext>();
        var rows =
            await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(
                db.WebhookInbox.Where(x => x.Source == "duffel" && x.EventId == eventId),
                ct
            );
        var row = rows.ShouldHaveSingleItem();
        _fixture.Probe.HandledCount(row.Id).ShouldBe(1);
    }

    [Fact]
    public async Task Concurrent_duplicate_deliveries_persist_and_publish_exactly_once()
    {
        var ct = TestContext.Current.CancellationToken;
        var eventId = "wh_port_concurrent_" + Guid.NewGuid().ToString("N");
        var request = _fixture.CreateSignedRequest(eventId, "X-DUFFEL-SIGNATURE");

        await using var firstScope = _fixture.Host.Services.CreateAsyncScope();
        await using var secondScope = _fixture.Host.Services.CreateAsyncScope();
        var firstPort = firstScope.ServiceProvider.GetRequiredService<IWebhookIngestionPort>();
        var secondPort = secondScope.ServiceProvider.GetRequiredService<IWebhookIngestionPort>();
        ErrorOr<WebhookIngestionOutcome>[] results = [];

        await _fixture
            .Host.TrackActivity()
            .Timeout(TimeSpan.FromSeconds(60))
            .WaitForMessageToBeReceivedAt<ProcessDuffelWebhookCommand>(_fixture.Host)
            .ExecuteAndWaitAsync(
                (Func<IMessageContext, Task>)(
                    async _ =>
                    {
                        results = await Task.WhenAll(
                            firstPort.IngestAsync(request, ct),
                            secondPort.IngestAsync(request, ct)
                        );
                    }
                )
            );

        results.ShouldAllBe(result => !result.IsError);
        results
            .Select(result => result.Value)
            .OrderBy(outcome => outcome)
            .ShouldBe(
                new[] { WebhookIngestionOutcome.Accepted, WebhookIngestionOutcome.Duplicate }
            );

        await using var verifyScope = _fixture.Host.Services.CreateAsyncScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<FlightsDbContext>();
        var rows =
            await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(
                db.WebhookInbox.Where(x => x.Source == "duffel" && x.EventId == eventId),
                ct
            );
        var row = rows.ShouldHaveSingleItem();
        _fixture.Probe.HandledCount(row.Id).ShouldBe(1);
    }

    [Fact]
    public async Task Non_unique_save_failure_rolls_back_inbox_and_outgoing_outbox_row()
    {
        var ct = TestContext.Current.CancellationToken;
        var eventId = "wh_port_failure_" + Guid.NewGuid().ToString("N");
        var request = _fixture.CreateSignedRequest(eventId);
        var handledBefore = _fixture.Probe.TotalHandledCount;
        _fixture.SaveFailure.FailNextSaveFor(eventId);

        await using var scope = _fixture.Host.Services.CreateAsyncScope();
        var port = scope.ServiceProvider.GetRequiredService<IWebhookIngestionPort>();

        var error = await Should.ThrowAsync<InvalidOperationException>(async () =>
            await port.IngestAsync(request, ct)
        );
        error.Message.ShouldContain(eventId);
        _fixture.SaveFailure.WasTriggered(eventId).ShouldBeTrue();

        await using var verifyScope = _fixture.Host.Services.CreateAsyncScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<FlightsDbContext>();
        var inboxCount =
            await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.CountAsync(
                db.WebhookInbox,
                x => x.Source == "duffel" && x.EventId == eventId,
                ct
            );
        inboxCount.ShouldBe(0);

        var runtime = _fixture.Host.Services.GetRequiredService<IWolverineRuntime>();
        var outgoing = await runtime.Storage.Admin.AllOutgoingAsync();
        outgoing
            .Where(envelope => envelope.MessageType == typeof(ProcessDuffelWebhookCommand).FullName)
            .ShouldBeEmpty(
                "the interceptor failure must roll back the real port's buffered command."
            );
        _fixture.Probe.TotalHandledCount.ShouldBe(
            handledBefore,
            "a failed transaction must not deliver and drain the buffered command."
        );
    }

    [Fact]
    public async Task Multiple_signature_values_return_typed_invalid_signature()
    {
        var eventId = "wh_port_multi_signature_" + Guid.NewGuid().ToString("N");
        var request = _fixture.CreateSignedRequest(eventId);
        var signature = request.Headers["X-Duffel-Signature"];
        request = request with
        {
            Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["X-Duffel-Signature"] = $"{signature},{signature}",
            },
        };

        await using var scope = _fixture.Host.Services.CreateAsyncScope();
        var port = scope.ServiceProvider.GetRequiredService<IWebhookIngestionPort>();

        var result = await port.IngestAsync(request, TestContext.Current.CancellationToken);

        result.IsError.ShouldBeTrue();
        result.FirstError.ShouldBe(WebhookIngestionErrors.InvalidSignature);
    }

    [Fact]
    public async Task Invalid_json_returns_typed_invalid_payload()
    {
        var payload = Encoding.UTF8.GetBytes("{not-json");
        var request = _fixture.CreateSignedRequest(payload);

        await using var scope = _fixture.Host.Services.CreateAsyncScope();
        var port = scope.ServiceProvider.GetRequiredService<IWebhookIngestionPort>();

        var result = await port.IngestAsync(request, TestContext.Current.CancellationToken);

        result.IsError.ShouldBeTrue();
        result.FirstError.ShouldBe(WebhookIngestionErrors.InvalidPayload);
    }
}

public sealed class DuffelWebhookIngestionPortFixture : IAsyncLifetime
{
    internal const string WebhookSecret = "test-webhook-secret-32-chars-long!";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder(
        "pgvector/pgvector:pg17"
    ).Build();

    public IHost Host { get; private set; } = default!;

    public OutboxProbeRecorder Probe { get; } = new();

    public FailWebhookSaveChangesInterceptor SaveFailure { get; } = new();

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();
        var connectionString = _postgres.GetConnectionString();
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();

        builder.Services.AddSingleton(Probe);
        builder.Services.AddSingleton(SaveFailure);
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<IFlightsMetrics, NullWebhookMetrics>();
        builder.Services.Configure<DuffelOptions>(options => options.WebhookSecret = WebhookSecret);
        builder.Services.AddSingleton<DuffelWebhookVerifier>();
        builder.Services.AddScoped<IWebhookIngestionPort, DuffelWebhookIngestionPort>();

        builder.Services.AddDbContext<FlightsDbContext>(
            (services, options) =>
            {
                options.UseNpgsql(connectionString);
                options.UseSnakeCaseNamingConvention();
                options.AddInterceptors(
                    services.GetRequiredService<FailWebhookSaveChangesInterceptor>()
                );
            }
        );

        builder
            .Services.AddMarten(options =>
            {
                options.Connection(connectionString);
                options.AutoCreateSchemaObjects = AutoCreate.All;
                FlightsModule.ConfigureMarten(options);
            })
            .UseLightweightSessions()
            .IntegrateWithWolverine();

        builder.UseWolverine(options =>
        {
            options.Policies.AutoApplyTransactions();
            options.Policies.UseDurableLocalQueues();
            options.UseEntityFrameworkCoreTransactions();
            options.Discovery.IncludeAssembly(typeof(DuffelWebhookIngestionPortTests).Assembly);
            options.Services.RunWolverineInSoloMode();
        });

        Host = builder.Build();
        await Host.StartAsync();

        await using var scope = Host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FlightsDbContext>();
        await db.Database.MigrateAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (Host is not null)
        {
            await Host.StopAsync();
            Host.Dispose();
        }

        await _postgres.DisposeAsync();
    }

    public WebhookIngestionRequest CreateSignedRequest(
        string eventId,
        string signatureHeaderName = "X-Duffel-Signature"
    )
    {
        var payload = Encoding.UTF8.GetBytes(
            $$$"""{"id":"{{{eventId}}}","type":"order.created","created_at":"2026-08-21T00:00:00Z","object":{"id":"ord_test"}}"""
        );
        return CreateSignedRequest(payload, signatureHeaderName);
    }

    public WebhookIngestionRequest CreateSignedRequest(
        byte[] payload,
        string signatureHeaderName = "X-Duffel-Signature"
    ) =>
        new(
            "duffel",
            payload,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [signatureHeaderName] = ComputeSignature(payload),
            }
        );

    private static string ComputeSignature(byte[] payload)
    {
        const long timestamp = 1_700_000_000;
        var timestampText = timestamp.ToString(CultureInfo.InvariantCulture);
        var timestampBytes = Encoding.UTF8.GetBytes(timestampText);
        var signed = new byte[timestampBytes.Length + 1 + payload.Length];
        Buffer.BlockCopy(timestampBytes, 0, signed, 0, timestampBytes.Length);
        signed[timestampBytes.Length] = (byte)'.';
        Buffer.BlockCopy(payload, 0, signed, timestampBytes.Length + 1, payload.Length);

        var key = Encoding.UTF8.GetBytes(WebhookSecret);
        var hash = HMACSHA256.HashData(key, signed);
        return $"t={timestampText},v1={Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    private sealed class NullWebhookMetrics : IFlightsMetrics
    {
        public void RecordSearchLatency(double elapsedMs, string provider, string status) { }

        public void RecordSearchError(string provider) { }

        public void RecordPaymentOutcome(bool success) { }

        public void RecordAggregateEventsAppended(string eventType, long count = 1) { }

        public void RecordNlSearchUsage(int inputTokens, int outputTokens, decimal costUsd) { }

        public void RecordWebhookReceived(string eventType) { }

        public void RecordWebhookProcessingLag(double ms, string eventType) { }

        public void RecordAirlineInitiatedChange() { }

        public void RecordPaymentDuration(double ms, string outcome) { }

        public void RecordNlSearchDuration(double ms) { }

        public void RecordSearchPartialFill(bool partial) { }

        public void RecordOfferShown() { }

        public void RecordOrderBooked() { }
    }
}

public sealed class FailWebhookSaveChangesInterceptor : SaveChangesInterceptor
{
    private readonly ConcurrentDictionary<string, byte> _armed = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _triggered = new(StringComparer.Ordinal);

    public void FailNextSaveFor(string eventId) => _armed[eventId] = 0;

    public bool WasTriggered(string eventId) => _triggered.ContainsKey(eventId);

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default
    )
    {
        var eventId = eventData
            .Context?.ChangeTracker.Entries<WebhookInboxEntity>()
            .Where(entry => entry.State == EntityState.Added)
            .Select(entry => entry.Entity.EventId)
            .FirstOrDefault(candidate => _armed.TryRemove(candidate, out _));

        if (eventId is not null)
        {
            _triggered[eventId] = 0;
            throw new InvalidOperationException(
                $"Injected non-unique SaveChanges failure for {eventId}."
            );
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}
