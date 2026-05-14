using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Travel.AI.NlSearch;
using Travel.AI.NlSearch.Contracts;
using Travel.AI.Persistence;
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
