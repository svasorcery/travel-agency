using Travel.AI.Persistence;
using Travel.AI.Persistence.Entities;
using Travel.IntegrationContracts.AI.FareSummary;

namespace Travel.AI.FareSummary;

public sealed class FareSummaryLedger(AiDbContext db, TimeProvider time) : IFareSummaryLedger
{
    public async Task RecordAsync(FareSummaryPlanned result, CancellationToken cancellationToken)
    {
        // Never invent usage/model/cost for the non-nullable existing ledger schema.
        if (
            result.InputTokens is null
            || result.OutputTokens is null
            || result.CostUsd is null
            || string.IsNullOrWhiteSpace(result.ModelId)
        )
            throw new InvalidOperationException("Fare summary accounting metadata unavailable.");
        db.CostLedger.Add(
            new CostLedgerEntry
            {
                Id = Guid.NewGuid(),
                Feature = "flights.fare_summary",
                Model = result.ModelId,
                InputTokens = result.InputTokens.Value,
                OutputTokens = result.OutputTokens.Value,
                CostUsd = result.CostUsd.Value,
                UserId = null,
                MessageIdentity = FareSummaryMessageIdentity.Requested,
                CorrelationId = result.CorrelationId,
                OccurredAt = time.GetUtcNow(),
            }
        );
        await db.SaveChangesAsync(cancellationToken);
    }
}
