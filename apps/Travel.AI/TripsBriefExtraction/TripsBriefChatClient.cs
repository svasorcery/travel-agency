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
        CreateProjectedClient(anthropic.Value.ApiKey)
    );

    public static ClientOptions CreateClientOptions(string apiKey, HttpClient? httpClient = null) =>
        new()
        {
            ApiKey = apiKey,
            MaxRetries = 0,
            BaseUrl = "https://api.anthropic.com",
            HttpClient = httpClient ?? new HttpClient(),
        };

    public static IChatClient CreateProjectedClient(string apiKey, HttpClient? httpClient = null) =>
        new ProjectedClient(
            new AnthropicClient(CreateClientOptions(apiKey, httpClient)).AsIChatClient()
        );

    private sealed class ProjectedClient(IChatClient client) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default
        )
        {
            if (
                options?.ResponseFormat is not ChatResponseFormatJson json
                || json.Schema is null
                || !System.Text.Json.JsonElement.DeepEquals(
                    json.Schema.Value,
                    TripsBriefExtractor.LocalSchema()
                )
                || options.RawRepresentationFactory is not null
                || options.Tools is { Count: > 0 }
                || options.Temperature != 0
                || options.MaxOutputTokens != TripsBriefExtractor.MaxOutputTokens
            )
                throw new InvalidOperationException("TripsWireOptionsRejected");
            var projected = options.Clone();
            projected.ResponseFormat = null;
            projected.RawRepresentationFactory =
                _ => new Anthropic.Models.Messages.MessageCreateParams
                {
                    Model = options.ModelId!,
                    MaxTokens = TripsBriefExtractor.MaxOutputTokens,
                    Messages = [],
                    OutputConfig = new Anthropic.Models.Messages.OutputConfig
                    {
                        Format = new Anthropic.Models.Messages.JsonOutputFormat
                        {
                            Schema = System.Text.Json.JsonSerializer.Deserialize<
                                Dictionary<string, System.Text.Json.JsonElement>
                            >(TripsBriefWireProjection.Schema().GetRawText())!,
                        },
                    },
                };
            return client.GetResponseAsync(messages, projected, cancellationToken);
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException("SingleResponseOnly");

        public object? GetService(Type type, object? serviceKey = null) => null;

        public void Dispose() => client.Dispose();
    }

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
