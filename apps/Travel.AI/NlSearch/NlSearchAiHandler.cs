using System.Diagnostics;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Travel.AI.Observability;
using Travel.AI.Persistence;
using Travel.AI.Persistence.Entities;
using Travel.IntegrationContracts.AI.NlSearch;
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
        AiMetrics metrics,
        ILogger<NlSearchRequested> log,
        CancellationToken ct
    )
    {
        // Delegate extraction to the shared NlSearchExtractor so the eval suite
        // can call the same logic without Wolverine/DB dependencies. The extractor
        // returns the parsed DTO plus the token usage reported by the model.
        var sw = Stopwatch.StartNew();
        NlSearchExtraction extraction;
        // Wrap the Anthropic call in a gen_ai.completion span (OTel semantic conventions §GenAI).
        using (
            var genAiSpan = AiActivitySource.Source.StartActivity(
                "gen_ai.completion",
                ActivityKind.Client
            )
        )
        {
            // Model ID is not known until after the call; set a provisional value and update below.
            genAiSpan?.SetTag("gen_ai.system", "anthropic");
            genAiSpan?.SetTag(
                "correlation_id",
                Activity.Current?.TraceId.ToString() ?? string.Empty
            );
            extraction = await NlSearchExtractor.ExtractAsync(
                chat,
                req.Query,
                today: DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime),
                ct: ct
            );
            genAiSpan?.SetTag("gen_ai.request.model", extraction.ModelId);
            genAiSpan?.SetTag("gen_ai.usage.input_tokens", extraction.InputTokens);
            genAiSpan?.SetTag("gen_ai.usage.output_tokens", extraction.OutputTokens);
        }

        sw.Stop();
        var p = extraction.Result;

        var costUsd =
            (extraction.InputTokens * InputCostPer1M + extraction.OutputTokens * OutputCostPer1M)
            / 1_000_000m;

        // OTel gen_ai.* instruments for this model call.
        metrics.RecordTokenUsage(
            extraction.ModelId,
            "chat",
            extraction.InputTokens,
            extraction.OutputTokens
        );
        metrics.RecordOperationDuration(extraction.ModelId, "chat", sw.Elapsed.TotalSeconds);

        db.CostLedger.Add(
            new CostLedgerEntry
            {
                Id = Guid.NewGuid(),
                Feature = "flights.nl_search",
                Model = extraction.ModelId,
                InputTokens = extraction.InputTokens,
                OutputTokens = extraction.OutputTokens,
                CostUsd = costUsd,
                // UserId is intentionally null for M1 — NL-search is available to anonymous users.
                // Wire in the authenticated user's id when AI features require authentication (M2).
                UserId = null,
                MessageIdentity = NlSearchMessageIdentity.Requested,
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
            p.Currency,
            InputTokens: extraction.InputTokens,
            OutputTokens: extraction.OutputTokens,
            CostUsd: costUsd,
            ModelId: extraction.ModelId
        );
    }
}
