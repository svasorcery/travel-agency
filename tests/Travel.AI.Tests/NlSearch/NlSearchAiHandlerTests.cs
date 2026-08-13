using System.Diagnostics.Metrics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Travel.AI.NlSearch;
using Travel.AI.Observability;
using Travel.AI.Persistence;
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
