using Microsoft.Extensions.AI;

namespace Travel.AI.Tests.NlSearch;

internal sealed class RawNlChatClient(string text, UsageDetails? usage) : IChatClient
{
    public int Calls { get; private set; }
    public ChatOptions? CapturedOptions { get; private set; }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        Calls++;
        CapturedOptions = options;
        return Task.FromResult(
            new ChatResponse(new ChatMessage(ChatRole.Assistant, text))
            {
                ModelId = "claude-opus-4-7",
                Usage = usage,
            }
        );
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default
    ) => throw new NotSupportedException();

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}
