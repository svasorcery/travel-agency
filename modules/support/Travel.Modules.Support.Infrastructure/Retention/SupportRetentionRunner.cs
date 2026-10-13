using Microsoft.EntityFrameworkCore;
using Travel.Modules.Support.Core;
using Travel.Modules.Support.Infrastructure.Persistence;

namespace Travel.Modules.Support.Infrastructure.Retention;

public sealed record SupportPurgeCandidate(Guid CaseId, int Revision, DateTimeOffset PurgeAt);

public sealed class SupportRetentionRunner(SupportDbContext db, TimeProvider time)
{
    public const int BatchSize = 50;
    public const int PassLimit = 1000;

    public async Task<IReadOnlyList<SupportPurgeCandidate>> ReadBatchAsync(
        CancellationToken ct,
        int limit = BatchSize
    )
    {
        if (limit is < 1 or > BatchSize)
            throw new ArgumentOutOfRangeException(nameof(limit));
        var now = time.GetUtcNow();
        return await db
            .Cases.AsNoTracking()
            .Where(x => x.State == CaseState.Resolved && x.PurgeAt != null && x.PurgeAt <= now)
            .OrderBy(x => x.PurgeAt)
            .ThenBy(x => x.CaseId)
            .Take(limit)
            .Select(x => new SupportPurgeCandidate(x.CaseId, x.Revision, x.PurgeAt!.Value))
            .ToListAsync(ct);
    }

    public async Task<bool> DeleteAsync(SupportPurgeCandidate candidate, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        // PostgreSQL cascades owned messages and receipts in the same atomic delete statement.
        return await db
                .Cases.Where(x =>
                    x.CaseId == candidate.CaseId
                    && x.State == CaseState.Resolved
                    && x.Revision == candidate.Revision
                    && x.PurgeAt == candidate.PurgeAt
                    && x.PurgeAt <= now
                )
                .ExecuteDeleteAsync(ct) == 1;
    }

    public async Task<int> RunPassAsync(CancellationToken ct)
    {
        var deleted = 0;
        for (var observed = 0; observed < PassLimit; )
        {
            var batch = await ReadBatchAsync(ct, Math.Min(BatchSize, PassLimit - observed));
            if (batch.Count == 0)
                break;
            observed += batch.Count;
            foreach (var candidate in batch)
                if (await DeleteAsync(candidate, ct))
                    deleted++;
        }
        return deleted;
    }
}
