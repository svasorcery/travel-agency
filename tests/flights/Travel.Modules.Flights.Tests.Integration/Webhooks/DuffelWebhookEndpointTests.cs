using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Testcontainers.PostgreSql;
using Travel.Modules.Flights.Api.Endpoints;
using Travel.Modules.Flights.Application.Commands;
using Travel.Modules.Flights.Application.Observability;
using Travel.Modules.Flights.Infrastructure.Persistence;
using Travel.Modules.Flights.Infrastructure.Providers.Duffel;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Xunit;

namespace Travel.Modules.Flights.Tests.Integration.Webhooks;

[Trait("Category", "Integration")]
public sealed class DuffelWebhookEndpointTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder(
        "pgvector/pgvector:pg17"
    ).Build();

    private FlightsDbContext _db = default!;

    private const string WebhookSecret = "test-webhook-secret-32-chars-long!";

    public async ValueTask InitializeAsync()
    {
        await _pg.StartAsync();

        var efOptions = new DbContextOptionsBuilder<FlightsDbContext>()
            .UseNpgsql(_pg.GetConnectionString())
            .UseSnakeCaseNamingConvention()
            .Options;
        _db = new FlightsDbContext(efOptions);
        await _db.Database.EnsureCreatedAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _db.DisposeAsync();
        await _pg.DisposeAsync();
    }

    // ─── helpers ───────────────────────────────────────────────────────────────

    private DuffelWebhookVerifier CreateVerifier() =>
        new DuffelWebhookVerifier(
            Options.Create(new DuffelOptions { WebhookSecret = WebhookSecret })
        );

    // Real Duffel scheme: header is X-Duffel-Signature, value is "t=<unix>,v1=<hex>",
    // HMAC-SHA256 over "<timestamp>.<body>". See DuffelWebhookVerifier docs.
    private static string ComputeSignature(byte[] body, string secret, long unixSeconds)
    {
        var ts = unixSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var tsBytes = Encoding.UTF8.GetBytes(ts);
        var signed = new byte[tsBytes.Length + 1 + body.Length];
        Buffer.BlockCopy(tsBytes, 0, signed, 0, tsBytes.Length);
        signed[tsBytes.Length] = (byte)'.';
        Buffer.BlockCopy(body, 0, signed, tsBytes.Length + 1, body.Length);

        var key = Encoding.UTF8.GetBytes(secret);
        var hash = HMACSHA256.HashData(key, signed);
        return $"t={ts},v1={Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    private static (HttpRequest Request, byte[] Body) BuildRequest(
        object payload,
        string? overrideSignature = null,
        string secret = WebhookSecret
    )
    {
        var json = JsonSerializer.Serialize(payload);
        var body = Encoding.UTF8.GetBytes(json);
        var sig = overrideSignature ?? ComputeSignature(body, secret, unixSeconds: 1700000000);

        var ctx = new DefaultHttpContext();
        ctx.Request.Method = HttpMethods.Post;
        ctx.Request.ContentType = "application/json";
        ctx.Request.Body = new MemoryStream(body);
        ctx.Request.Headers["X-Duffel-Signature"] = sig;

        return (ctx.Request, body);
    }

    private static object BuildDuffelEvent(string eventId, string type, object obj) =>
        new
        {
            id = eventId,
            type,
            created_at = DateTimeOffset.UtcNow,
            @object = obj,
        };

    private static readonly IFlightsMetrics NullMetrics = new NullFlightsMetrics();

    // ─── recording fake outbox ────────────────────────────────────────────────
    //
    // Static-invocation tests cannot exercise the real Wolverine EF outbox (that
    // requires the host); a recording fake captures the publish calls and forwards
    // the flush to db.SaveChangesAsync so the inbox row commits. The atomicity and
    // 23505-on-concurrent-insert guarantees are covered by DuffelWebhookEndpointOutboxTests.

    private sealed class RecordingDbContextOutbox(FlightsDbContext db)
        : IDbContextOutbox<FlightsDbContext>
    {
        public List<object> Published { get; } = new();

        public FlightsDbContext DbContext => db;

        public DbContext? ActiveContext => db;

        public string? TenantId { get; set; }

        public void Enroll(DbContext dbContext) { }

        public ValueTask PublishAsync<T>(T message, DeliveryOptions? options = null)
        {
            Published.Add(message!);
            return ValueTask.CompletedTask;
        }

        public ValueTask SendAsync<T>(T message, DeliveryOptions? options = null) =>
            throw new NotImplementedException();

        public ValueTask BroadcastToTopicAsync(
            string topicName,
            object message,
            DeliveryOptions? options = null
        ) => throw new NotImplementedException();

        public async Task SaveChangesAndFlushMessagesAsync(CancellationToken token = default)
        {
            // Mimic Wolverine's atomic save+flush: SaveChangesAsync surfaces the unique
            // violation as DbUpdateException, which the endpoint catches. The "flush"
            // half is a no-op here because the recording fake holds messages in-memory.
            await db.SaveChangesAsync(token);
        }

        public Task FlushOutgoingMessagesAsync() => Task.CompletedTask;

        public IDestinationEndpoint EndpointFor(string endpointName) =>
            throw new NotImplementedException();

        public IDestinationEndpoint EndpointFor(Uri uri) => throw new NotImplementedException();

        public Task InvokeAsync(
            object message,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        ) => throw new NotImplementedException();

        public Task InvokeAsync(
            object message,
            DeliveryOptions options,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        ) => throw new NotImplementedException();

        public Task<T> InvokeAsync<T>(
            object message,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        ) => throw new NotImplementedException();

        public Task<T> InvokeAsync<T>(
            object message,
            DeliveryOptions options,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        ) => throw new NotImplementedException();

        public Task InvokeForTenantAsync(
            string tenantId,
            object message,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        ) => throw new NotImplementedException();

        public Task<T> InvokeForTenantAsync<T>(
            string tenantId,
            object message,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        ) => throw new NotImplementedException();

        public IReadOnlyList<Envelope> PreviewSubscriptions(object message) =>
            throw new NotImplementedException();

        public IReadOnlyList<Envelope> PreviewSubscriptions(
            object message,
            DeliveryOptions options
        ) => throw new NotImplementedException();
    }

    // ─── tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ValidSignature_NewEvent_ReturnsOk_InboxRowInserted_CommandPublished()
    {
        var ct = TestContext.Current.CancellationToken;
        var eventId = "wh_" + Guid.NewGuid().ToString("N");
        var payload = BuildDuffelEvent(eventId, "order.created", new { id = "ord_test" });
        var (req, _) = BuildRequest(payload);

        var verifier = CreateVerifier();
        var outbox = new RecordingDbContextOutbox(_db);
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);

        var result = await DuffelWebhookEndpoint.Receive(
            req,
            verifier,
            _db,
            outbox,
            NullMetrics,
            time,
            NullLogger<DuffelWebhookEndpoint>.Instance,
            ct
        );

        // Returns Ok
        result.ShouldBeOfType<Ok>();

        // Inbox row inserted
        var row = await _db.WebhookInbox.FirstOrDefaultAsync(
            x => x.Source == "duffel" && x.EventId == eventId,
            ct
        );
        row.ShouldNotBeNull();
        row.EventType.ShouldBe("order.created");
        row.ProcessedAt.ShouldBeNull(); // not yet processed — handler does that

        // Command published
        outbox
            .Published.OfType<ProcessDuffelWebhookCommand>()
            .ShouldHaveSingleItem()
            .InboxId.ShouldBe(row.Id);
    }

    [Fact]
    public async Task InvalidSignature_ReturnsUnauthorized_NoInboxRow()
    {
        var ct = TestContext.Current.CancellationToken;
        var eventId = "wh_" + Guid.NewGuid().ToString("N");
        var payload = BuildDuffelEvent(eventId, "order.created", new { id = "ord_bad_sig" });
        var (req, _) = BuildRequest(payload, overrideSignature: "t=1700000000,v1=badbadbadbad");

        var verifier = CreateVerifier();
        var outbox = new RecordingDbContextOutbox(_db);
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);

        var result = await DuffelWebhookEndpoint.Receive(
            req,
            verifier,
            _db,
            outbox,
            NullMetrics,
            time,
            NullLogger<DuffelWebhookEndpoint>.Instance,
            ct
        );

        // Returns Unauthorized
        result.ShouldBeOfType<UnauthorizedHttpResult>();

        // No inbox row created
        var count = await _db.WebhookInbox.CountAsync(
            x => x.Source == "duffel" && x.EventId == eventId,
            ct
        );
        count.ShouldBe(0);

        // No command published
        outbox.Published.ShouldBeEmpty();
    }

    [Fact]
    public async Task DuplicateEventId_ReturnsOk_NoSecondRowInserted_NoCommandPublished()
    {
        var ct = TestContext.Current.CancellationToken;
        var eventId = "wh_" + Guid.NewGuid().ToString("N");
        var payload = BuildDuffelEvent(eventId, "order.created", new { id = "ord_dup" });

        // First call
        var (req1, _) = BuildRequest(payload);
        var verifier = CreateVerifier();
        var outbox1 = new RecordingDbContextOutbox(_db);
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);

        await DuffelWebhookEndpoint.Receive(
            req1,
            verifier,
            _db,
            outbox1,
            NullMetrics,
            time,
            NullLogger<DuffelWebhookEndpoint>.Instance,
            ct
        );

        // Second call with the same event id
        var (req2, _) = BuildRequest(payload);
        var outbox2 = new RecordingDbContextOutbox(_db);

        var result = await DuffelWebhookEndpoint.Receive(
            req2,
            verifier,
            _db,
            outbox2,
            NullMetrics,
            time,
            NullLogger<DuffelWebhookEndpoint>.Instance,
            ct
        );

        // Returns Ok
        result.ShouldBeOfType<Ok>();

        // Only one inbox row
        var count = await _db.WebhookInbox.CountAsync(
            x => x.Source == "duffel" && x.EventId == eventId,
            ct
        );
        count.ShouldBe(1);

        // No command published on second call
        outbox2.Published.ShouldBeEmpty();
    }
}

file sealed class NullFlightsMetrics : IFlightsMetrics
{
    public void RecordSearchLatency(double elapsedMs, string provider, string status) { }

    public void RecordSearchError(string provider) { }

    public void RecordPaymentOutcome(bool success) { }

    public void RecordAggregateEventsAppended(string eventType, long count = 1) { }

    public void RecordNlSearchUsage(int inputTokens, int outputTokens, decimal costUsd) { }

    public void RecordWebhookReceived(string eventType) { }

    public void RecordWebhookProcessingLag(double ms, string eventType) { }
}
