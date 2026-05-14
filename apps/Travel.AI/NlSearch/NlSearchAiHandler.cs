using System.Text.Json;
using Microsoft.Extensions.AI;
using Travel.AI.NlSearch.Contracts;
using Travel.AI.Persistence;
using Travel.AI.Persistence.Entities;
using Wolverine.Attributes;

namespace Travel.AI.NlSearch;

public static class NlSearchAiHandler
{
    // Shared JsonSerializerOptions for deserializing the LLM JSON response.
    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    [WolverineHandler]
    public static async Task<NlSearchParsed> Handle(
        NlSearchRequested req,
        IChatClient chat,
        AiDbContext db,
        TimeProvider time,
        ILogger<NlSearchRequested> log,
        CancellationToken ct
    )
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, NlSearchPrompts.NlSearchSystem),
            new(ChatRole.User, req.Query),
        };

        // Use ChatResponseFormat.ForJsonSchema<T> to request structured JSON output.
        // The IChatClient implementation (Anthropic) will use the schema to constrain output.
        var options = new ChatOptions
        {
            Temperature = 0.1f,
            ResponseFormat = ChatResponseFormat.ForJsonSchema<ParsedSearchCriteriaDto>(),
        };

        var response = await chat.GetResponseAsync(messages, options, ct);

        var usage = response.Usage;
        var inTok = (int)(usage?.InputTokenCount ?? 0);
        var outTok = (int)(usage?.OutputTokenCount ?? 0);

        db.CostLedger.Add(
            new CostLedgerEntry
            {
                Id = Guid.NewGuid(),
                Feature = "flights.nl_search",
                Model = response.ModelId ?? "claude-opus-4-7",
                InputTokens = inTok,
                OutputTokens = outTok,
                CostUsd =
                    (
                        inTok * NlSearchPrompts.InputCostPer1M
                        + outTok * NlSearchPrompts.OutputCostPer1M
                    ) / 1_000_000m,
                CorrelationId = req.CorrelationId,
                OccurredAt = time.GetUtcNow(),
            }
        );
        await db.SaveChangesAsync(ct);

        var json = response.Text;
        log.LogDebug(
            "NlSearch LLM response for correlation {CorrelationId}: {Json}",
            req.CorrelationId,
            json
        );

        var p =
            JsonSerializer.Deserialize<ParsedSearchCriteriaDto>(json, _jsonOptions)
            ?? throw new InvalidOperationException(
                $"LLM returned null or unparseable JSON for correlation {req.CorrelationId}."
            );

        return new NlSearchParsed(
            req.CorrelationId,
            p.Origin,
            p.Destination,
            p.DepartureDate,
            p.ReturnDate,
            p.PassengerCount,
            p.CabinClass,
            p.Currency
        );
    }
}
