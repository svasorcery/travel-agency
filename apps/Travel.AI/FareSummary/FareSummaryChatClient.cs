using Anthropic;
using Anthropic.Core;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Travel.AI.Configuration;

namespace Travel.AI.FareSummary;

// Independent SDK client; creating the feature service never opens a connection.
// The key is resolved only when an admitted invocation reaches GetResponseAsync.
public sealed class FareSummaryChatClient(
    IOptions<AnthropicOptions> anthropic,
    FareSummaryOptions options
) : IChatClient
{
    private readonly Lazy<IChatClient> inner = new(() =>
        new AnthropicClient(CreateClientOptions(anthropic.Value.ApiKey)).AsIChatClient(
            options.Model
        )
    );

    public static ClientOptions CreateClientOptions(string apiKey, HttpClient? httpClient = null) =>
        new()
        {
            ApiKey = apiKey,
            MaxRetries = 0,
            HttpClient = httpClient ?? new HttpClient(),
        };

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default
    ) => inner.Value.GetResponseAsync(messages, options, cancellationToken);

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default
    ) => throw new NotSupportedException("Fare summary uses one bounded response.");

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
        if (inner.IsValueCreated)
            inner.Value.Dispose();
    }
}
