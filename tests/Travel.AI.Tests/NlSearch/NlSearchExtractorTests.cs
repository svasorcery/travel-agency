using Microsoft.Extensions.AI;
using Shouldly;
using Travel.AI.NlSearch;
using Xunit;

namespace Travel.AI.Tests.NlSearch;

/// <summary>
/// Unit tests for <see cref="NlSearchExtractor" /> using a fake <see cref="IChatClient" />
/// that captures the messages it receives. No network or container required.
/// </summary>
public sealed class NlSearchExtractorTests
{
    [Theory]
    [InlineData("null")]
    [InlineData("empty")]
    [InlineData("whitespace")]
    [InlineData("characters")]
    [InlineData("input-budget")]
    public async Task Bounded_input_stops_before_model_work(string kind)
    {
        var query = kind switch
        {
            "null" => null,
            "empty" => "",
            "whitespace" => "  ",
            "characters" => new string('x', 1001),
            _ => new string('я', 900),
        };
        using var chat = new RawNlChatClient(
            System.Text.Json.JsonSerializer.Serialize(CannedDto),
            new UsageDetails { InputTokenCount = 10, OutputTokenCount = 5 }
        );
        await Should.ThrowAsync<InvalidOperationException>(() =>
            NlSearchExtractor.ExtractAsync(
                chat,
                query!,
                new DateOnly(2026, 10, 16),
                TestContext.Current.CancellationToken
            )
        );
        chat.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task Model_output_is_capped_at512tokens()
    {
        using var chat = new RawNlChatClient(
            System.Text.Json.JsonSerializer.Serialize(CannedDto),
            new UsageDetails { InputTokenCount = 10, OutputTokenCount = 5 }
        );
        await NlSearchExtractor.ExtractAsync(
            chat,
            "Moscow to Kazan tomorrow",
            new DateOnly(2026, 10, 16),
            TestContext.Current.CancellationToken
        );
        chat.CapturedOptions.ShouldNotBeNull();
        chat.CapturedOptions.MaxOutputTokens.ShouldBe(512);
    }

    [Fact]
    public async Task Oversized_response_is_rejected_even_when_whitespace_makes_it_valid_json()
    {
        using var chat = new RawNlChatClient(
            System.Text.Json.JsonSerializer.Serialize(CannedDto) + new string(' ', 17 * 1024),
            new UsageDetails { InputTokenCount = 10, OutputTokenCount = 5 }
        );
        await Should.ThrowAsync<InvalidOperationException>(() =>
            NlSearchExtractor.ExtractAsync(
                chat,
                "Moscow to Kazan tomorrow",
                new DateOnly(2026, 10, 16),
                TestContext.Current.CancellationToken
            )
        );
        chat.Calls.ShouldBe(1);
    }

    private static readonly ParsedSearchCriteriaDto CannedDto = new(
        Origin: "LED",
        Destination: "SVO",
        DepartureDate: new DateOnly(2026, 5, 14),
        ReturnDate: null,
        PassengerCount: 1,
        CabinClass: "economy",
        Currency: "RUB"
    );

    [Fact]
    public async Task Extractor_injects_today_token()
    {
        // Arrange
        var capturingClient = new CapturingChatClient(CannedDto);
        var today = new DateOnly(2026, 5, 14);

        // Act
        await NlSearchExtractor.ExtractAsync(
            capturingClient,
            "из Москвы в Питер на выходных",
            today: today,
            ct: TestContext.Current.CancellationToken
        );

        // Assert — the last user message must contain the [today: yyyy-MM-dd] token.
        var userMessage = capturingClient
            .CapturedMessages.Where(m => m.Role == ChatRole.User)
            .Last();

        userMessage.Text.ShouldContain("[today: 2026-05-14]");
    }

    [Fact]
    public async Task Null_response_exception_never_contains_private_query()
    {
        const string sentinel = "private-query-sentinel-548a";
        using var client = new RawNlChatClient(
            "null",
            new UsageDetails { InputTokenCount = 10, OutputTokenCount = 5 }
        );
        var error = await Should.ThrowAsync<InvalidOperationException>(() =>
            NlSearchExtractor.ExtractAsync(client, sentinel, new DateOnly(2026, 10, 16))
        );
        error.ToString().ShouldNotContain(sentinel);
        client.Calls.ShouldBe(1);
    }

    // ── Fake IChatClient ───────────────────────────────────────────────────────

    private sealed class CapturingChatClient(ParsedSearchCriteriaDto dto) : IChatClient
    {
        public List<ChatMessage> CapturedMessages { get; } = [];

        public ChatClientMetadata Metadata => new("capturing", null, null);

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default
        )
        {
            CapturedMessages.AddRange(messages);
            var json = System.Text.Json.JsonSerializer.Serialize(dto);
            var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, json))
            {
                ModelId = "claude-opus-4-7",
                Usage = new UsageDetails { InputTokenCount = 10, OutputTokenCount = 5 },
            };
            return Task.FromResult(response);
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }
}
