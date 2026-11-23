using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Travel.AI.NlSearch;

public sealed record NlSearchExtraction(
    ParsedSearchCriteriaDto Result,
    int InputTokens,
    int OutputTokens,
    string ModelId
);

public sealed record NlSearchUsage(int? InputTokens, int? OutputTokens, string ModelId);

internal sealed record NlSearchExtraction<T>(
    T Result,
    int InputTokens,
    int OutputTokens,
    string ModelId
);

internal sealed record NlSearchPrompt<T>(
    Func<string> SystemText,
    ChatResponseFormatJson Format,
    Func<string, T?> Parse
)
    where T : class;

// Accounting metadata only: never retain input, provider payload or parser exceptions.
public sealed class NlSearchExtractionException : InvalidOperationException
{
    public NlSearchExtractionException() { }

    public NlSearchExtractionException(string message)
        : base(message) { }

    public NlSearchExtractionException(string message, Exception innerException)
        : base(message, innerException) { }

    public NlSearchExtractionException(string outcome, NlSearchUsage usage)
        : base($"NL-search extraction failed ({outcome}).")
    {
        Outcome = outcome;
        Usage = usage;
    }

    public string Outcome { get; } = "InferenceFailed";
    public NlSearchUsage Usage { get; } = new(null, null, "unknown");
}

public static class NlSearchExtractor
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };
    internal static readonly NlSearchPrompt<ParsedSearchCriteriaDto> LegacyPrompt = new(
        () => NlSearchPrompts.NlSearchSystem,
        ChatResponseFormat.ForJsonSchema<ParsedSearchCriteriaDto>(),
        text => JsonSerializer.Deserialize<ParsedSearchCriteriaDto>(text, JsonOptions)
    );

    private static string UserMessage(string query, DateOnly today) =>
        $"{query}\n\n[today: {today:yyyy-MM-dd}]";

    // One UTF-8 byte per possible token is deliberately conservative. Include every
    // supplied prompt/schema field plus a framing reserve rather than query-only counting.
    public static int ValidateInput(string? query, DateOnly today, NlSearchOptions bounds) =>
        ValidateInput(query, today, bounds, LegacyPrompt);

    internal static int ValidateInput<T>(
        string? query,
        DateOnly today,
        NlSearchOptions bounds,
        NlSearchPrompt<T> prompt
    )
        where T : class
    {
        bounds.Validate();
        if (string.IsNullOrWhiteSpace(query) || query.Length > bounds.MaxQueryCharacters)
            throw Failure("InvalidInput");
        var envelope = JsonSerializer.Serialize(
            new
            {
                system = prompt.SystemText(),
                user = UserMessage(query, today),
                schema = prompt.Format.Schema,
                prompt.Format.SchemaName,
                prompt.Format.SchemaDescription,
                model = NlSearchOptions.ModelId,
                temperature = 0.1f,
                max_tokens = bounds.MaxOutputTokens,
            },
            new JsonSerializerOptions
            {
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            }
        );
        var conservativeTokens = Encoding.UTF8.GetByteCount(envelope) + 128;
        if (conservativeTokens > bounds.MaxInputTokens)
            throw Failure("InputBudgetExceeded");
        return conservativeTokens;
    }

    public static Task<NlSearchExtraction> ExtractAsync(
        IChatClient chat,
        string query,
        DateOnly today,
        CancellationToken ct = default
    ) => ExtractAsync(chat, query, today, new NlSearchOptions(), TimeProvider.System, null, ct);

    public static async Task<NlSearchExtraction> ExtractAsync(
        IChatClient chat,
        string query,
        DateOnly today,
        NlSearchOptions bounds,
        TimeProvider time,
        Action<Task>? retainActiveUntilSettled,
        CancellationToken ct
    )
    {
        var extraction = await ExtractAsync(
            chat,
            query,
            today,
            bounds,
            LegacyPrompt,
            time,
            retainActiveUntilSettled,
            ct
        );
        return new(
            extraction.Result,
            extraction.InputTokens,
            extraction.OutputTokens,
            extraction.ModelId
        );
    }

    internal static async Task<NlSearchExtraction<T>> ExtractAsync<T>(
        IChatClient chat,
        string query,
        DateOnly today,
        NlSearchOptions bounds,
        NlSearchPrompt<T> prompt,
        TimeProvider time,
        Action<Task>? retainActiveUntilSettled,
        CancellationToken ct
    )
        where T : class
    {
        ValidateInput(query, today, bounds, prompt);
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, prompt.SystemText()),
            new(ChatRole.User, UserMessage(query, today)),
        };
        var options = new ChatOptions
        {
            ModelId = NlSearchOptions.ModelId,
            Temperature = 0.1f,
            ResponseFormat = prompt.Format,
            MaxOutputTokens = bounds.MaxOutputTokens,
        };
        using var deadline = new CancellationTokenSource(
            TimeSpan.FromMilliseconds(bounds.InferenceTimeoutMilliseconds),
            time
        );
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, deadline.Token);
        Task<ChatResponse>? pending = null;
        ChatResponse response;
        try
        {
            linked.Token.ThrowIfCancellationRequested();
            pending = chat.GetResponseAsync(messages, options, linked.Token);
            response = await pending.WaitAsync(linked.Token);
        }
        catch (Exception)
        {
            if (pending is not null)
            {
                retainActiveUntilSettled?.Invoke(pending);
                _ = pending.ContinueWith(
                    done =>
                    {
                        _ = done.Exception;
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default
                );
            }
            throw Failure(linked.IsCancellationRequested ? "DeadlineExceeded" : "InferenceFailed");
        }
        var usage = new NlSearchUsage(
            TokenCount(response.Usage?.InputTokenCount),
            TokenCount(response.Usage?.OutputTokenCount),
            response.ModelId ?? NlSearchOptions.ModelId
        );
        if (
            response.Text.Length > bounds.MaxResponseBytes
            || Encoding.UTF8.GetByteCount(response.Text) > bounds.MaxResponseBytes
            || usage.InputTokens > bounds.MaxInputTokens
            || usage.OutputTokens > bounds.MaxOutputTokens
        )
            throw new NlSearchExtractionException("OutputBudgetExceeded", usage);
        T? dto;
        try
        {
            dto = prompt.Parse(response.Text);
        }
        catch (JsonException)
        {
            throw new NlSearchExtractionException("InvalidOutput", usage);
        }
        if (dto is null)
            throw new NlSearchExtractionException("InvalidOutput", usage);
        if (usage.InputTokens is null || usage.OutputTokens is null)
            throw new NlSearchExtractionException("UsageUnknown", usage);
        return new(dto, usage.InputTokens.Value, usage.OutputTokens.Value, usage.ModelId);
    }

    private static NlSearchExtractionException Failure(string outcome) =>
        new(outcome, new NlSearchUsage(null, null, NlSearchOptions.ModelId));

    private static int? TokenCount(long? count) =>
        count is >= 0 and <= int.MaxValue ? (int)count.Value : null;
}
