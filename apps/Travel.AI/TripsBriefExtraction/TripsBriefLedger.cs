using Microsoft.EntityFrameworkCore;
using Travel.AI.Persistence;
using Travel.AI.Persistence.Entities;
using Travel.IntegrationContracts.AI.TripsBriefExtraction;

namespace Travel.AI.TripsBriefExtraction;

public interface ITripsBriefLedger
{
    Task AdmitAsync(Guid correlationId, string model, CancellationToken cancellationToken);
    Task CompleteAsync(TripsBriefParsed metadata, CancellationToken cancellationToken);
}

// Each task owns its context until the database settles, including noncooperative cancellation.
// Neither a detached request scope nor user text is retained by accounting.
public sealed class TripsBriefLedger(IServiceScopeFactory scopes, TimeProvider time)
    : ITripsBriefLedger
{
    public async Task AdmitAsync(
        Guid correlationId,
        string model,
        CancellationToken cancellationToken
    )
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AiDbContext>();
        db.CostLedger.Add(
            new CostLedgerEntry
            {
                Id = Guid.NewGuid(),
                Feature = "trips.brief_extraction",
                Model = model,
                Outcome = "Admitted",
                MessageIdentity = TripsBriefMessageIdentity.Requested,
                CorrelationId = correlationId,
                OccurredAt = time.GetUtcNow(),
                InputTokens = null,
                OutputTokens = null,
                CostUsd = null,
                UserId = null,
            }
        );
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task CompleteAsync(TripsBriefParsed metadata, CancellationToken cancellationToken)
    {
        if (metadata.Fields is not null || metadata.Issues is not null)
            throw new InvalidOperationException("Trips brief accounting requires metadata only.");
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AiDbContext>();
        var row = await db.CostLedger.SingleAsync(
            r =>
                r.MessageIdentity == TripsBriefMessageIdentity.Requested
                && r.CorrelationId == metadata.CorrelationId,
            cancellationToken
        );
        row.InputTokens = metadata.InputTokens;
        row.OutputTokens = metadata.OutputTokens;
        row.CostUsd = metadata.CostUsd;
        row.Outcome = metadata.Status;
        // The row retains the configured public model, never arbitrary untrusted response.ModelId.
        await db.SaveChangesAsync(cancellationToken);
    }
}
