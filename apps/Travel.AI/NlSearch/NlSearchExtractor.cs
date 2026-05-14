using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Travel.AI.NlSearch;

/// <summary>
/// Thin static helper that calls the LLM and deserialises its structured-JSON output
/// into a <see cref="ParsedSearchCriteriaDto" />.
/// Shared by <see cref="NlSearchAiHandler" /> (Wolverine path) and the AI-eval test suite.
/// </summary>
public static class NlSearchExtractor
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// Sends <paramref name="query" /> to the LLM and returns the parsed criteria.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the LLM returns null or unparseable JSON.
    /// </exception>
    public static async Task<ParsedSearchCriteriaDto> ExtractAsync(
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
        var json = response.Text;

        return JsonSerializer.Deserialize<ParsedSearchCriteriaDto>(json, JsonOptions)
            ?? throw new InvalidOperationException(
                $"LLM returned null or unparseable JSON for query: {query}"
            );
    }
}
