extern alias TravelAiApp;

using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shouldly;
using AiDbContext = TravelAiApp::Travel.AI.Persistence.AiDbContext;
using AiDbContextConfiguration = TravelAiApp::Travel.AI.Persistence.AiDbContextConfiguration;
using CostLedgerEntry = TravelAiApp::Travel.AI.Persistence.Entities.CostLedgerEntry;

namespace Travel.Host.Tests.Integration.Aspire;

internal static class AspireSmokeDatabase
{
    private static readonly Guid LedgerId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid LedgerCorrelationId = Guid.Parse(
        "22222222-2222-4222-8222-222222222222"
    );

    private static readonly string[] RequiredTables =
    [
        "ai.__ef_migrations_history",
        "ai.cost_ledger",
        "flights.__ef_migrations_history",
        "flights.deeplink_offers_cache",
        "flights.idempotency_keys",
        "flights.order_read_model",
        "flights.webhook_inbox",
        "public.mt_events",
        "public.mt_streams",
        "public.wolverine_incoming_envelopes",
        "public.wolverine_outgoing_envelopes",
    ];

    public static async Task AssertInitializedSchemaAsync(
        string connectionString,
        CancellationToken ct
    )
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand(
            """
            SELECT table_schema || '.' || table_name
            FROM information_schema.tables
            WHERE table_schema IN ('ai', 'flights', 'public')
            ORDER BY table_schema, table_name;
            """,
            connection
        );
        await using var reader = await command.ExecuteReaderAsync(ct);
        var tables = new HashSet<string>(StringComparer.Ordinal);
        while (await reader.ReadAsync(ct))
            tables.Add(reader.GetString(0));

        foreach (var requiredTable in RequiredTables)
            tables.ShouldContain(requiredTable);
    }

    public static async Task AssertDeterministicStateIsEmptyAsync(
        string connectionString,
        string webhookEventId,
        CancellationToken ct
    )
    {
        var webhookState = await ReadWebhookStateAsync(connectionString, webhookEventId, ct);
        webhookState.Count.ShouldBe(0);

        await using var context = CreateAiDbContext(connectionString);
        var ledgerExists = await context
            .CostLedger.AsNoTracking()
            .AnyAsync(x => x.Id == LedgerId, ct);
        ledgerExists.ShouldBeFalse();
    }

    public static async Task WaitForWebhookProcessedAsync(
        string connectionString,
        string webhookEventId,
        CancellationToken ct
    )
    {
        using var phaseCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        phaseCts.CancelAfter(TimeSpan.FromSeconds(30));
        using var poll = new PeriodicTimer(TimeSpan.FromMilliseconds(50));

        try
        {
            while (await poll.WaitForNextTickAsync(phaseCts.Token))
            {
                var state = await ReadWebhookStateAsync(
                    connectionString,
                    webhookEventId,
                    phaseCts.Token
                );
                if (state is { Count: 1, ProcessedCount: 1 })
                    return;

                state.Count.ShouldBeLessThanOrEqualTo(1);
            }
        }
        catch (OperationCanceledException exception)
            when (!ct.IsCancellationRequested && phaseCts.IsCancellationRequested)
        {
            throw new TimeoutException(
                "Webhook handler did not mark the inbox row processed.",
                exception
            );
        }

        throw new InvalidOperationException("Webhook processing observation ended unexpectedly.");
    }

    public static async Task AssertExactlyOneProcessedWebhookAsync(
        string connectionString,
        string webhookEventId,
        CancellationToken ct
    )
    {
        var state = await ReadWebhookStateAsync(connectionString, webhookEventId, ct);
        state.Count.ShouldBe(1);
        state.ProcessedCount.ShouldBe(1);
    }

    public static async Task WriteAndReadAiLedgerWithProductionOptionsAsync(
        string connectionString,
        CancellationToken ct
    )
    {
        await using var context = CreateAiDbContext(connectionString);
        var expected = new CostLedgerEntry
        {
            Id = LedgerId,
            Feature = "flights.nl_search.smoke",
            Model = "no-anthropic-smoke",
            InputTokens = 0,
            OutputTokens = 0,
            CostUsd = 0m,
            MessageIdentity = "travel.ai.nl-search.requested",
            CorrelationId = LedgerCorrelationId,
            OccurredAt = new DateTimeOffset(2026, 8, 16, 0, 0, 0, TimeSpan.Zero),
        };

        context.CostLedger.Add(expected);
        await context.SaveChangesAsync(ct);
        context.ChangeTracker.Clear();

        var actual = await context.CostLedger.AsNoTracking().SingleAsync(x => x.Id == LedgerId, ct);
        actual.Feature.ShouldBe(expected.Feature);
        actual.Model.ShouldBe(expected.Model);
        actual.MessageIdentity.ShouldBe(expected.MessageIdentity);
        actual.CorrelationId.ShouldBe(expected.CorrelationId);
        actual.CostUsd.ShouldBe(0m);
    }

    private static AiDbContext CreateAiDbContext(string connectionString) =>
        new(
            new DbContextOptionsBuilder<AiDbContext>()
                .UseNpgsql(connectionString, AiDbContextConfiguration.ConfigureNpgsql)
                .UseSnakeCaseNamingConvention()
                .Options
        );

    private static async Task<WebhookState> ReadWebhookStateAsync(
        string connectionString,
        string webhookEventId,
        CancellationToken ct
    )
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand(
            """
            SELECT count(*), count(processed_at)
            FROM flights.webhook_inbox
            WHERE source = 'duffel' AND event_id = @eventId;
            """,
            connection
        );
        command.Parameters.AddWithValue("eventId", webhookEventId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var hasRow = await reader.ReadAsync(ct);
        hasRow.ShouldBeTrue();
        return new WebhookState(reader.GetInt64(0), reader.GetInt64(1));
    }

    private sealed record WebhookState(long Count, long ProcessedCount);
}
