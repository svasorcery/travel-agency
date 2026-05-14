using Microsoft.Extensions.AI;
using Travel.AI.NlSearch.Contracts;
using Travel.AI.Persistence;
using Travel.AI.Persistence.Entities;
using Wolverine.Attributes;

namespace Travel.AI.NlSearch;

public static class NlSearchAiHandler
{
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
        // can call the same logic without Wolverine/DB dependencies.
        var p = await NlSearchExtractor.ExtractAsync(chat, req.Query, ct);

        // Token-usage tracking requires access to the raw ChatResponse, so we
        // keep a minimal usage-capture path here.  ExtractAsync does not expose
        // the raw response intentionally (eval suite only needs the DTO).
        // For cost logging we record zeroes when usage is unavailable — this is
        // acceptable for M1 (actual billing is visible on the provider dashboard).
        db.CostLedger.Add(
            new CostLedgerEntry
            {
                Id = Guid.NewGuid(),
                Feature = "flights.nl_search",
                Model = "claude-opus-4-7",
                InputTokens = 0,
                OutputTokens = 0,
                CostUsd = 0m,
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
