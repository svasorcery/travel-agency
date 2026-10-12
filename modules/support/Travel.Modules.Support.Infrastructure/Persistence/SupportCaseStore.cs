using System.Data.Common;
using ErrorOr;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Travel.Modules.Support.Application;
using Travel.Modules.Support.Core;

namespace Travel.Modules.Support.Infrastructure.Persistence;

public sealed class SupportCaseStore(SupportDbContext db, TimeProvider time) : ISupportCaseStore
{
    public Task<ErrorOr<SupportCase?>> LoadCaseAsync(Guid caseId, CancellationToken ct) =>
        Guard<SupportCase?>(async () =>
            (
                await db.Cases.AsNoTracking().SingleOrDefaultAsync(x => x.CaseId == caseId, ct)
            )?.ToRecord()
        );

    public Task<ErrorOr<SupportReceipt?>> LoadReceiptAsync(
        Guid caseId,
        Guid actionId,
        CancellationToken ct
    ) =>
        Guard<SupportReceipt?>(async () =>
            (
                await db
                    .Receipts.AsNoTracking()
                    .SingleOrDefaultAsync(x => x.CaseId == caseId && x.ActionId == actionId, ct)
            )?.ToRecord()
        );

    public Task<ErrorOr<SupportMessage?>> LoadMessageAsync(
        Guid caseId,
        Guid messageId,
        CancellationToken ct
    ) =>
        Guard<SupportMessage?>(async () =>
            (
                await db
                    .Messages.AsNoTracking()
                    .SingleOrDefaultAsync(x => x.CaseId == caseId && x.MessageId == messageId, ct)
            )?.ToRecord()
        );

    public Task<ErrorOr<IReadOnlyList<SupportCase>>> ListCasesAsync(
        SupportCaseQuery query,
        CancellationToken ct
    ) =>
        Guard<IReadOnlyList<SupportCase>>(async () =>
        {
            if (query.Limit is < 1 or > 21)
                return SupportErrors.InvalidInput;
            var rows = db
                .Cases.AsNoTracking()
                .Where(x => x.PurgeAt == null || x.PurgeAt > query.Now);
            if (query.OwnerId is { } owner)
                rows = rows.Where(x => x.OwnerId == owner);
            if (query.Filters.State is { } state)
                rows = rows.Where(x => x.State == state);
            if (query.Filters.Category is { } category)
                rows = rows.Where(x => x.Category == category);
            if (query.Filters.Priority is { } priority)
                rows = rows.Where(x => x.Priority == priority);
            if (query.After is { } after)
                rows = rows.Where(x =>
                    x.CreatedAt > after.CreatedAt
                    || (x.CreatedAt == after.CreatedAt && x.CaseId.CompareTo(after.CaseId) > 0)
                );
            var items = await rows.OrderBy(x => x.CreatedAt)
                .ThenBy(x => x.CaseId)
                .Take(query.Limit)
                .ToListAsync(ct);
            return ErrorOrFactory.From<IReadOnlyList<SupportCase>>(
                items.Select(x => x.ToRecord()).ToArray()
            );
        });

    public Task<ErrorOr<IReadOnlyList<SupportMessage>>> ListMessagesAsync(
        SupportMessageQuery query,
        CancellationToken ct
    ) =>
        Guard<IReadOnlyList<SupportMessage>>(async () =>
        {
            if (query.Limit is < 1 or > 21)
                return SupportErrors.InvalidInput;
            var rows = db.Messages.AsNoTracking().Where(x => x.CaseId == query.CaseId);
            if (query.After is { } after)
                rows = rows.Where(x =>
                    x.WrittenAtRevision > after.WrittenAtRevision
                    || (
                        x.WrittenAtRevision == after.WrittenAtRevision
                        && x.MessageId.CompareTo(after.MessageId) > 0
                    )
                );
            var items = await rows.OrderBy(x => x.WrittenAtRevision)
                .ThenBy(x => x.MessageId)
                .Take(query.Limit)
                .ToListAsync(ct);
            return ErrorOrFactory.From<IReadOnlyList<SupportMessage>>(
                items.Select(x => x.ToRecord()).ToArray()
            );
        });

    public Task<ErrorOr<Success>> TryCreateAsync(
        SupportCase supportCase,
        SupportMessage message,
        SupportReceipt receipt,
        CancellationToken ct
    ) =>
        Write(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            db.Cases.Add(SupportCaseEntity.From(supportCase));
            db.Messages.Add(SupportMessageEntity.From(message));
            db.Receipts.Add(SupportReceiptEntity.From(receipt));
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            db.ChangeTracker.Clear();
            return Result.Success;
        });

    public Task<ErrorOr<Success>> TryUpdateAsync(
        int expectedRevision,
        SupportCase supportCase,
        SupportMessage? message,
        SupportReceipt receipt,
        CancellationToken ct
    ) =>
        Write(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            // Evaluate the live predecessor at commit admission, including reopen races with expiry.
            var now = time.GetUtcNow();
            var affected = await db
                .Cases.Where(x =>
                    x.CaseId == supportCase.CaseId
                    && x.OwnerId == supportCase.OwnerId
                    && x.Revision == expectedRevision
                    && (x.PurgeAt == null || x.PurgeAt > now)
                )
                .ExecuteUpdateAsync(
                    setters =>
                        setters
                            .SetProperty(x => x.State, supportCase.State)
                            .SetProperty(x => x.Revision, supportCase.Revision)
                            .SetProperty(x => x.Category, supportCase.Category)
                            .SetProperty(x => x.Priority, supportCase.Priority)
                            .SetProperty(x => x.TriageSource, supportCase.TriageSource)
                            .SetProperty(x => x.UpdatedAt, supportCase.UpdatedAt)
                            .SetProperty(x => x.ResolvedAt, supportCase.ResolvedAt)
                            .SetProperty(x => x.PurgeAt, supportCase.PurgeAt),
                    ct
                );
            if (affected != 1)
                return SupportErrors.Conflict;
            if (message is not null)
                db.Messages.Add(SupportMessageEntity.From(message));
            db.Receipts.Add(SupportReceiptEntity.From(receipt));
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            db.ChangeTracker.Clear();
            return Result.Success;
        });

    private async Task<ErrorOr<Success>> Write(Func<Task<ErrorOr<Success>>> operation)
    {
        try
        {
            return await Guard(operation);
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    private static async Task<ErrorOr<T>> Guard<T>(Func<Task<ErrorOr<T>>> operation)
    {
        try
        {
            return await operation();
        }
        catch (DbUpdateConcurrencyException)
        {
            return SupportErrors.Conflict;
        }
        catch (DbUpdateException exception)
            when (exception.InnerException
                    is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation }
            )
        {
            return SupportErrors.Conflict;
        }
        catch (DbUpdateException)
        {
            return SupportErrors.Unavailable;
        }
        catch (DbException)
        {
            return SupportErrors.Unavailable;
        }
        catch (TimeoutException)
        {
            return SupportErrors.Unavailable;
        }
        catch (InvalidOperationException exception)
            when (exception.InnerException is { } inner
                && ExecutionStrategy.CallOnWrappedException(
                    inner,
                    static cause =>
                        cause is TimeoutException or NpgsqlException { IsTransient: true }
                )
            )
        {
            return SupportErrors.Unavailable;
        }
    }
}
