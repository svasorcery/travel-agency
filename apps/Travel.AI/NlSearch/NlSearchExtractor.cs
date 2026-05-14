using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Travel.AI.NlSearch;

/// <summary>
/// Result of one NL-search extraction: the parsed criteria plus the token usage
/// reported by the model, so callers can write an accurate cost-ledger entry.
/// </summary>
public sealed record NlSearchExtraction(
    ParsedSearchCriteriaDto Result,
    int InputTokens,
    int OutputTokens,
    string ModelId
);

/// <summary>
/// Thin static helper that calls the LLM and deserialises its structured-JSON output
/// into a <see cref="ParsedSearchCriteriaDto" />, returning token usage alongside it.
/// Shared by <see cref="NlSearchAiHandler" /> (Wolverine path) and the AI-eval test suite.
/// </summary>
public static class NlSearchExtractor
{
    private const string DefaultModelId = "claude-opus-4-7";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// Sends <paramref name="query" /> to the LLM and returns the parsed criteria
    /// together with the token usage reported on the response.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the LLM returns null or unparseable JSON.
    /// </exception>
    public static async Task<NlSearchExtraction> ExtractAsync(
        IChatClient chat,
        string query,
        CancellationToken ct = default
    )
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, NlSearchPrompts.NlSearchSystem),
            new(ChatRole.User, query),
        };

        var options = new ChatOptions
        {
            Temperature = 0.1f,
            ResponseFormat = ChatResponseFormat.ForJsonSchema<ParsedSearchCriteriaDto>(),
        };

        var response = await chat.GetResponseAsync(messages, options, ct);

        var dto =
            JsonSerializer.Deserialize<ParsedSearchCriteriaDto>(response.Text, JsonOptions)
            ?? throw new InvalidOperationException(
                $"LLM returned null or unparseable JSON for query: {query}"
            );

        var usage = response.Usage;
        return new NlSearchExtraction(
            dto,
            InputTokens: (int)(usage?.InputTokenCount ?? 0),
            OutputTokens: (int)(usage?.OutputTokenCount ?? 0),
            ModelId: response.ModelId ?? DefaultModelId
        );
    }
}
