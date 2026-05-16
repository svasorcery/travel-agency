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
