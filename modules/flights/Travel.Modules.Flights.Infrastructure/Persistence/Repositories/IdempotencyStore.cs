using ErrorOr;
using Microsoft.EntityFrameworkCore;
using Travel.Modules.Flights.Application.Idempotency;
using Travel.Modules.Flights.Core.Errors;
using Travel.Modules.Flights.Infrastructure.Persistence.Entities;

namespace Travel.Modules.Flights.Infrastructure.Persistence.Repositories;

public sealed class IdempotencyStore(FlightsDbContext db, TimeProvider time) : IIdempotencyStore
{
    public async Task<IdempotencyRecord?> TryGetAsync(
        IdempotencyKey key,
        Guid userId,
        string route,
        CancellationToken ct
    )
    {
        var now = time.GetUtcNow();
        var row = await db
            .IdempotencyKeys.Where(x =>
                x.Key == key.Value && x.UserId == userId && x.Route == route && x.ExpiresAt > now
            )
            .FirstOrDefaultAsync(ct);
        return row?.ResponseHash is null
            ? null
            : new IdempotencyRecord(row.ResponseHash, row.ResponseStatus, row.ResponseBody!);
    }

    public async Task<ErrorOr<Success>> CheckOrConflictAsync(
        IdempotencyKey key,
        Guid userId,
        string route,
        string bodyHash,
        CancellationToken ct
    )
    {
        var existing = await db
            .IdempotencyKeys.Where(x =>
                x.Key == key.Value && x.UserId == userId && x.Route == route
            )
            .FirstOrDefaultAsync(ct);
        if (existing is null)
            return Result.Success;
        return existing.BodyHash == bodyHash ? Result.Success : FlightsErrors.IdempotencyConflict;
    }

    public async Task<ErrorOr<Success>> SaveAsync(
        IdempotencyKey key,
        Guid userId,
        string route,
        string bodyHash,
        string responseHash,
        int responseStatus,
        string responseBody,
        CancellationToken ct
    )
    {
        db.IdempotencyKeys.Add(
            new IdempotencyKeyEntity
            {
                Key = key.Value,
                UserId = userId,
                Route = route,
                BodyHash = bodyHash,
                ResponseHash = responseHash,
                ResponseStatus = responseStatus,
                ResponseBody = responseBody,
                CreatedAt = time.GetUtcNow(),
                ExpiresAt = time.GetUtcNow().AddHours(24),
            }
        );
        await db.SaveChangesAsync(ct);
        return Result.Success;
    }

    public async Task PurgeExpiredAsync(CancellationToken ct)
    {
        var now = time.GetUtcNow();
        await db.IdempotencyKeys.Where(x => x.ExpiresAt <= now).ExecuteDeleteAsync(ct);
    }
}
