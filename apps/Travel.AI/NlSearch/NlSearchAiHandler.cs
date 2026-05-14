using Microsoft.Extensions.AI;
using Travel.AI.NlSearch.Contracts;
using Travel.AI.Persistence;
using Travel.AI.Persistence.Entities;
using Wolverine.Attributes;

namespace Travel.AI.NlSearch;

public static class NlSearchAiHandler
{
    // Anthropic Opus 4.7 pricing (USD per 1M tokens) — see spec §13.4.
    private const decimal InputCostPer1M = 15m;
    private const decimal OutputCostPer1M = 75m;

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
        // Delegate extraction to the shared NlSearchExtractor so the eval suite
        // can call the same logic without Wolverine/DB dependencies. The extractor
        // returns the parsed DTO plus the token usage reported by the model.
        var extraction = await NlSearchExtractor.ExtractAsync(chat, req.Query, ct);
        var p = extraction.Result;

        var costUsd =
            (extraction.InputTokens * InputCostPer1M + extraction.OutputTokens * OutputCostPer1M)
            / 1_000_000m;

        db.CostLedger.Add(
            new CostLedgerEntry
            {
                Id = Guid.NewGuid(),
                Feature = "flights.nl_search",
                Model = extraction.ModelId,
                InputTokens = extraction.InputTokens,
                OutputTokens = extraction.OutputTokens,
                CostUsd = costUsd,
                CorrelationId = req.CorrelationId,
                OccurredAt = time.GetUtcNow(),
            }
        );
        await db.SaveChangesAsync(ct);

        log.LogDebug(
            "NlSearch parsed for correlation {CorrelationId}: origin={Origin} dest={Destination}",
            req.CorrelationId,
            p.Origin,
            p.Destination
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
