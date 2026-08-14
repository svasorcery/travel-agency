using System.Diagnostics.Metrics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using Shouldly;
using Travel.AI.NlSearch;
using Travel.AI.Observability;
using Travel.AI.Persistence;
using Travel.AI.Persistence.Entities;
using Travel.AI.Tests.Observability;
using Travel.IntegrationContracts.AI.NlSearch;
using Xunit;

namespace Travel.AI.Tests.NlSearch;

[Trait("Category", "Integration")]
public sealed class NlSearchAiHandlerTests : Travel.Shared.TestInfrastructure.IntegrationTestBase
{
    private static readonly DateTimeOffset FixedNow = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    private static readonly Guid CorrelationId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    private static readonly NlSearchRequested Request = new(
        Query: "Хочу слетать из Петербурга в Москву 15 июня",
        CorrelationId: CorrelationId
    );

    private static readonly ParsedSearchCriteriaDto CannedDto = new(
        Origin: "LED",
        Destination: "DME",
        DepartureDate: new DateOnly(2026, 6, 15),
        ReturnDate: null,
        PassengerCount: 1,
        CabinClass: "economy",
        Currency: "RUB"
    );

    private AiDbContext BuildDbContext()
    {
        var opts = new DbContextOptionsBuilder<AiDbContext>()
            .UseNpgsql(ConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
        return new AiDbContext(opts);
    }

    protected override async ValueTask OnInitializedAsync()
    {
        await using var db = BuildDbContext();
        await db.Database.MigrateAsync();
    }

    private static AiMetrics NewMetrics() => new(new TestMeterFactory());

    [Fact]
    public async Task Handle_ReturnsExpectedNlSearchParsed_AndWritesCostLedgerEntry()
    {
        // Arrange
        var fakeChat = new FakeChatClient(CannedDto);
        var time = new FakeTimeProvider();
        time.SetUtcNow(FixedNow);

        await using var db = BuildDbContext();

        // Act
        var result = await NlSearchAiHandler.Handle(
            Request,
            fakeChat,
            db,
            time,
            NewMetrics(),
            NullLogger<NlSearchRequested>.Instance,
            TestContext.Current.CancellationToken
        );

        // Assert — returned record
        result.CorrelationId.ShouldBe(CorrelationId);
        result.Origin.ShouldBe("LED");
        result.Destination.ShouldBe("DME");
        result.DepartureDate.ShouldBe(new DateOnly(2026, 6, 15));
        result.ReturnDate.ShouldBeNull();
        result.PassengerCount.ShouldBe(1);
        result.CabinClass.ShouldBe("economy");
        result.Currency.ShouldBe("RUB");

        // Assert — cost ledger row persisted
        await using var verifyDb = BuildDbContext();
        var entry = await verifyDb.CostLedger.SingleOrDefaultAsync(
            e => e.CorrelationId == CorrelationId,
            TestContext.Current.CancellationToken
        );
        entry.ShouldNotBeNull();
        entry.Feature.ShouldBe("flights.nl_search");
        entry.OccurredAt.ShouldBe(FixedNow);
        AssertMessageIdentityMapped(verifyDb);
        verifyDb
            .Entry(entry)
            .Property<string>("MessageIdentity")
            .CurrentValue.ShouldBe(NlSearchMessageIdentity.Requested);
    }

    [Fact]
    public async Task CostLedger_RejectsDuplicateMessageIdentityAndCorrelationId()
    {
        await using var db = BuildDbContext();
        AssertMessageIdentityMapped(db);

        AddLedgerEntry(db, CorrelationId, NlSearchMessageIdentity.Requested);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        AddLedgerEntry(db, CorrelationId, NlSearchMessageIdentity.Requested);
        var exception = await Should.ThrowAsync<DbUpdateException>(() =>
            db.SaveChangesAsync(TestContext.Current.CancellationToken)
        );

        var postgresException = exception.InnerException.ShouldBeOfType<PostgresException>();
        postgresException.SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
    }

    [Fact]
    public async Task CostLedger_AllowsSameIdentityWithNewCorrelationAndDifferentIdentityWithSameCorrelation()
    {
        await using var db = BuildDbContext();
        AssertMessageIdentityMapped(db);

        AddLedgerEntry(db, CorrelationId, NlSearchMessageIdentity.Requested);
        AddLedgerEntry(
            db,
            Guid.Parse("11111111-2222-3333-4444-555555555555"),
            NlSearchMessageIdentity.Requested
        );
        AddLedgerEntry(db, CorrelationId, NlSearchMessageIdentity.Parsed);

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        (await db.CostLedger.CountAsync(TestContext.Current.CancellationToken)).ShouldBe(3);
    }

    [Fact]
    public async Task Handle_PopulatesModelUsageFieldsOnResult()
    {
        // Arrange — FakeChatClient reports 100 input + 50 output tokens, model claude-opus-4-7.
        var fakeChat = new FakeChatClient(CannedDto);
        var time = new FakeTimeProvider();
        time.SetUtcNow(FixedNow);
        await using var db = BuildDbContext();

        // Act
        var result = await NlSearchAiHandler.Handle(
            Request,
            fakeChat,
            db,
            time,
            NewMetrics(),
            NullLogger<NlSearchRequested>.Instance,
            TestContext.Current.CancellationToken
        );

        // Assert — usage carried back so Travel.Host can record flights.nl_search.* metric
        result.InputTokens.ShouldBe(100);
        result.OutputTokens.ShouldBe(50);
        result.ModelId.ShouldBe("claude-opus-4-7");
        // (100 * $15 + 50 * $75) / 1_000_000
        result.CostUsd.ShouldBe(0.00525m);
    }

    [Fact]
    public async Task Handle_RecordsGenAiTokenUsageMetric()
    {
        // Arrange
        var fakeChat = new FakeChatClient(CannedDto);
        var time = new FakeTimeProvider();
        time.SetUtcNow(FixedNow);
        await using var db = BuildDbContext();

        var factory = new TestMeterFactory();
        var metrics = new AiMetrics(factory);

        var measurements = new List<(long Value, string? TokenType)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instr, l) =>
        {
            if (
                instr.Meter.Name == AiMetrics.MeterName
                && instr.Name == "gen_ai.client.token.usage"
            )
                l.EnableMeasurementEvents(instr);
        };
        listener.SetMeasurementEventCallback<long>(
            (instr, value, tags, _) =>
            {
                string? type = null;
                foreach (var t in tags)
                    if (t.Key == "gen_ai.token.type")
                        type = (string?)t.Value;
                measurements.Add((value, type));
            }
        );
        listener.Start();

        // Act
        await NlSearchAiHandler.Handle(
            Request,
            fakeChat,
            db,
            time,
            metrics,
            NullLogger<NlSearchRequested>.Instance,
            TestContext.Current.CancellationToken
        );

        // Assert — one input + one output measurement
        measurements.ShouldContain(m => m.TokenType == "input" && m.Value == 100);
        measurements.ShouldContain(m => m.TokenType == "output" && m.Value == 50);
    }

    // ─── Fake IChatClient ───────────────────────────────────────────────────────

    private static void AssertMessageIdentityMapped(AiDbContext db)
    {
        var property = db
            .Model.FindEntityType(typeof(CostLedgerEntry))
            ?.FindProperty("MessageIdentity");
        property.ShouldNotBeNull(
            "the cost ledger model must persist the canonical request message identity"
        );
        property.IsNullable.ShouldBeFalse();
    }

    private static void AddLedgerEntry(AiDbContext db, Guid correlationId, string messageIdentity)
    {
        var entry = new CostLedgerEntry
        {
            Id = Guid.NewGuid(),
            Feature = "flights.nl_search",
            Model = "claude-opus-4-7",
            InputTokens = 100,
            OutputTokens = 50,
            CostUsd = 0.00525m,
            CorrelationId = correlationId,
            OccurredAt = FixedNow,
        };
        db.CostLedger.Add(entry);
        db.Entry(entry).Property<string>("MessageIdentity").CurrentValue = messageIdentity;
    }

    /// <summary>
    /// Returns a canned <see cref="ParsedSearchCriteriaDto"/> serialized as JSON in a
    /// <see cref="ChatResponse"/>, mimicking the real Anthropic structured-output response.
    /// </summary>
    private sealed class FakeChatClient(ParsedSearchCriteriaDto dto) : IChatClient
    {
        public ChatClientMetadata Metadata => new("fake", null, null);

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default
        )
        {
            var json = System.Text.Json.JsonSerializer.Serialize(dto);
            var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, json))
            {
                ModelId = "claude-opus-4-7",
                Usage = new UsageDetails { InputTokenCount = 100, OutputTokenCount = 50 },
            };
            return Task.FromResult(response);
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException("Streaming not used by NlSearchAiHandler.");

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }
}

[Trait("Category", "Integration")]
public sealed class CostLedgerMigrationTests : Travel.Shared.TestInfrastructure.IntegrationTestBase
{
    private const string CostLedgerInitMigrationId = "20260514090212_CostLedgerInit";

    private const string IdempotencyMigrationSuffix = "_CostLedgerMessageIdempotency";

    private static readonly Guid CorrelationId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    private static readonly DateTimeOffset FixedNow = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    private AiDbContext BuildDbContext()
    {
        var options = new DbContextOptionsBuilder<AiDbContext>()
            .UseNpgsql(ConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
        return new AiDbContext(options);
    }

    protected override async ValueTask OnInitializedAsync()
    {
        await using var db = BuildDbContext();
        await db.GetService<IMigrator>().MigrateAsync(CostLedgerInitMigrationId);
    }

    [Fact]
    public async Task LegacyMigrationSetup_StartsAtCostLedgerInitOnly()
    {
        await using var db = BuildDbContext();

        var appliedMigrations = await db.Database.GetAppliedMigrationsAsync(
            TestContext.Current.CancellationToken
        );
        appliedMigrations.ShouldBe([CostLedgerInitMigrationId]);
        (await MessageIdentityColumnExistsAsync()).ShouldBeFalse();
    }

    [Fact]
    public async Task CostLedgerMessageIdempotency_BackfillsCanonicalRequestIdentity()
    {
        await using var db = BuildDbContext();
        await AssertInitialMigrationStateAsync(db);
        var migrationId = GetIdempotencyMigrationId(db);

        await InsertLegacyLedgerEntryAsync(db, Guid.NewGuid(), CorrelationId);

        await db.GetService<IMigrator>()
            .MigrateAsync(migrationId, TestContext.Current.CancellationToken);

        var messageIdentity = await ReadMessageIdentityAsync(CorrelationId);
        messageIdentity.ShouldBe(NlSearchMessageIdentity.Requested);
    }

    [Fact]
    public async Task CostLedgerMessageIdempotency_FailsClosedForDuplicateLegacyCorrelations()
    {
        await using var db = BuildDbContext();
        await AssertInitialMigrationStateAsync(db);
        var migrationId = GetIdempotencyMigrationId(db);

        await InsertLegacyLedgerEntryAsync(db, Guid.NewGuid(), CorrelationId);
        await InsertLegacyLedgerEntryAsync(db, Guid.NewGuid(), CorrelationId);

        var exception = await Should.ThrowAsync<PostgresException>(() =>
            db.GetService<IMigrator>()
                .MigrateAsync(migrationId, TestContext.Current.CancellationToken)
        );
        exception.SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);

        var appliedMigrations = await db.Database.GetAppliedMigrationsAsync(
            TestContext.Current.CancellationToken
        );
        appliedMigrations.ShouldBe([CostLedgerInitMigrationId]);
        (await MessageIdentityColumnExistsAsync()).ShouldBeFalse();
    }

    private static async Task AssertInitialMigrationStateAsync(AiDbContext db)
    {
        var appliedMigrations = await db.Database.GetAppliedMigrationsAsync(
            TestContext.Current.CancellationToken
        );
        appliedMigrations.ShouldBe([CostLedgerInitMigrationId]);

        await using var connection = new NpgsqlConnection(db.Database.GetConnectionString());
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        (await MessageIdentityColumnExistsAsync(connection)).ShouldBeFalse();
    }

    private static string GetIdempotencyMigrationId(AiDbContext db)
    {
        var migrationId = db
            .Database.GetMigrations()
            .SingleOrDefault(id =>
                id.EndsWith(IdempotencyMigrationSuffix, StringComparison.Ordinal)
            );
        migrationId.ShouldNotBeNull(
            "the source migration must exist before its PostgreSQL behavior can be verified"
        );
        return migrationId;
    }

    private static Task<int> InsertLegacyLedgerEntryAsync(
        AiDbContext db,
        Guid id,
        Guid correlationId
    ) =>
        db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO ai.cost_ledger
                (id, feature, model, input_tokens, output_tokens, cost_usd, user_id, correlation_id, occurred_at)
            VALUES
                ({id}, {"flights.nl_search"}, {"legacy-model"}, {1}, {1}, {0.00009m}, {null}, {correlationId}, {FixedNow})
            """,
            TestContext.Current.CancellationToken
        );

    private async Task<string?> ReadMessageIdentityAsync(Guid correlationId)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT message_identity FROM ai.cost_ledger WHERE correlation_id = @correlation_id",
            connection
        );
        command.Parameters.AddWithValue("correlation_id", correlationId);
        return (string?)await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
    }

    private async Task<bool> MessageIdentityColumnExistsAsync()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        return await MessageIdentityColumnExistsAsync(connection);
    }

    private static async Task<bool> MessageIdentityColumnExistsAsync(NpgsqlConnection connection)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT EXISTS (
                SELECT 1
                FROM information_schema.columns
                WHERE table_schema = 'ai'
                  AND table_name = 'cost_ledger'
                  AND column_name = 'message_identity'
            )
            """,
            connection
        );
        return (bool)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }
}
