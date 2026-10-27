using Anthropic;
using Anthropic.Core;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Travel.AI.Configuration;

namespace Travel.AI.TripsBriefExtraction;

// Feature-specific lazy adapter. No provider key is resolved before an authorized dispatch.
public sealed class TripsBriefChatClient(
    IOptions<AnthropicOptions> anthropic,
    TripsBriefOptions configuration
) : IChatClient
{
    private readonly Lazy<IChatClient> inner = new(() =>
        new AnthropicClient(CreateClientOptions(anthropic.Value.ApiKey)).AsIChatClient(
            configuration.Model
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
    )
    {
        if (
            !configuration.Enabled
            || !configuration.IsConfigured
            || configuration.ProcessorId != "anthropic"
            || options?.ModelId != configuration.Model
        )
            throw new InvalidOperationException("Trips brief model unavailable.");
        return inner.Value.GetResponseAsync(messages, options, cancellationToken);
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default
    ) => throw new NotSupportedException("Trips brief uses one bounded response.");

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
        if (inner.IsValueCreated)
            inner.Value.Dispose();
    }
}
