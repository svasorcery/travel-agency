using ErrorOr;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Travel.Modules.Flights.Application.Idempotency;
using Travel.Modules.Flights.Core.Errors;
using Travel.Modules.Flights.Infrastructure.Persistence.Entities;

namespace Travel.Modules.Flights.Infrastructure.Persistence.Repositories;

public sealed class IdempotencyStore(FlightsDbContext db, TimeProvider time) : IIdempotencyStore
{
    private const string PostgresUniqueViolation = "23505";

    public async Task<BeginResult> TryBeginAsync(
        IdempotencyKey key,
        Guid userId,
        string route,
        string bodyHash,
        CancellationToken ct
    )
    {
        // Look up an existing row first. If it's completed (ResponseHash set) and
        // the body matches, the caller wants a replay. If completed with different
        // body, it's a semantic conflict. If still in-flight, the caller raced
        // another request and must back off.
        var existing = await db
            .IdempotencyKeys.AsNoTracking()
            .Where(x => x.Key == key.Value && x.UserId == userId && x.Route == route)
            .FirstOrDefaultAsync(ct);
        if (existing is not null)
        {
            if (existing.BodyHash != bodyHash)
                return new BeginResult(BeginOutcome.BodyConflict, null);
            if (existing.ResponseHash is null)
                return new BeginResult(BeginOutcome.InFlight, null);
            return new BeginResult(
                BeginOutcome.Replay,
                new IdempotencyRecord(
                    existing.ResponseHash,
                    existing.ResponseStatus,
                    existing.ResponseBody!
                )
            );
        }

        // No row yet — race to insert an in-flight placeholder. The Key column is
        // the primary key; a concurrent insert from another request will lose with
        // SQLSTATE 23505 (unique_violation), which we translate to InFlight.
        var now = time.GetUtcNow();
        db.IdempotencyKeys.Add(
            new IdempotencyKeyEntity
            {
                Key = key.Value,
                UserId = userId,
                Route = route,
                BodyHash = bodyHash,
                ResponseHash = null,
                ResponseStatus = 0,
                ResponseBody = null,
                CreatedAt = now,
                ExpiresAt = now.AddHours(24),
            }
        );
        try
        {
            await db.SaveChangesAsync(ct);
            return new BeginResult(BeginOutcome.Started, null);
        }
        catch (DbUpdateException ex)
            when (ex.InnerException is PostgresException pg
                && pg.SqlState == PostgresUniqueViolation
            )
        {
            // Lost the race. Discard our tracked entity so subsequent saves on this
            // DbContext don't keep failing, then re-read to determine whether the
            // winner has already completed (replay) or is still in flight.
            foreach (var entry in db.ChangeTracker.Entries<IdempotencyKeyEntity>().ToList())
                entry.State = EntityState.Detached;

            var winner = await db
                .IdempotencyKeys.AsNoTracking()
                .Where(x => x.Key == key.Value && x.UserId == userId && x.Route == route)
                .FirstOrDefaultAsync(ct);
            if (winner is null)
                return new BeginResult(BeginOutcome.InFlight, null);
            if (winner.BodyHash != bodyHash)
                return new BeginResult(BeginOutcome.BodyConflict, null);
            if (winner.ResponseHash is null)
                return new BeginResult(BeginOutcome.InFlight, null);
            return new BeginResult(
                BeginOutcome.Replay,
                new IdempotencyRecord(
                    winner.ResponseHash,
                    winner.ResponseStatus,
                    winner.ResponseBody!
                )
            );
        }
    }

    public async Task CompleteAsync(
        IdempotencyKey key,
        Guid userId,
        string route,
        string responseHash,
        int responseStatus,
        string responseBody,
        CancellationToken ct
    )
    {
        var row = await db
            .IdempotencyKeys.Where(x =>
                x.Key == key.Value && x.UserId == userId && x.Route == route
            )
            .FirstOrDefaultAsync(ct);
        if (row is null)
            return;
        row.ResponseHash = responseHash;
        row.ResponseStatus = responseStatus;
        row.ResponseBody = responseBody;
        await db.SaveChangesAsync(ct);
    }

    public async Task AbandonAsync(
        IdempotencyKey key,
        Guid userId,
        string route,
        CancellationToken ct
    )
    {
        await db
            .IdempotencyKeys.Where(x =>
                x.Key == key.Value
                && x.UserId == userId
                && x.Route == route
                && x.ResponseHash == null
            )
            .ExecuteDeleteAsync(ct);
    }

    public async Task<IdempotencyRecord?> TryGetAsync(
        IdempotencyKey key,
        Guid userId,
        string route,
        string bodyHash,
        CancellationToken ct
    )
    {
        var now = time.GetUtcNow();
        var row = await db
            .IdempotencyKeys.Where(x =>
                x.Key == key.Value
                && x.UserId == userId
                && x.Route == route
                && x.BodyHash == bodyHash
                && x.ExpiresAt > now
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
