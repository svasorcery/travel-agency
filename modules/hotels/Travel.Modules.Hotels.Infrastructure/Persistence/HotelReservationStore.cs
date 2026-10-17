using System.Data.Common;
using System.Text.Json;
using ErrorOr;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Travel.Modules.Hotels.Application;
using Travel.Modules.Hotels.Core;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.Runtime;

namespace Travel.Modules.Hotels.Infrastructure.Persistence;

public sealed class HotelReservationStore(
    HotelsDbContext db,
    IDbContextOutbox<HotelsDbContext> outbox
) : IHotelReservationStore
{
    public Task<ErrorOr<HotelReservation?>> LoadAsync(Guid id, Guid? owner, CancellationToken ct) =>
        Guard<HotelReservation?>(async () =>
        {
            var row = await db
                .Reservations.AsNoTracking()
                .SingleOrDefaultAsync(r => r.Id == id && (owner == null || r.OwnerId == owner), ct);
            return row is null ? null : From(row);
        });

    public Task<ErrorOr<HotelReservation[]>> ListAsync(Guid owner, CancellationToken ct) =>
        Guard<HotelReservation[]>(async () =>
            (
                await db
                    .Reservations.AsNoTracking()
                    .Where(r => r.OwnerId == owner)
                    .OrderByDescending(r => r.AttemptId != null && r.PrivatePurgeAt == null)
                    .ThenByDescending(r => r.AttemptId != null)
                    .ThenByDescending(r => r.CreatedAt)
                    .ThenBy(r => r.Id)
                    .Take(50)
                    .ToArrayAsync(ct)
            )
                .Select(From)
                .ToArray()
        );

    public async Task<ErrorOr<bool>> SaveAsync(
        HotelReservation reservation,
        int expectedVersion,
        HotelWork[] work,
        CancellationToken ct
    )
    {
        try
        {
            var previous =
                expectedVersion == 0
                    ? null
                    : await db
                        .Reservations.AsNoTracking()
                        .SingleOrDefaultAsync(r => r.Id == reservation.Id, ct);
            if (expectedVersion > 0 && (previous is null || previous.Version != expectedVersion))
                return false;
            if (
                previous?.AttemptId is not null
                && (
                    previous.AttemptId != reservation.AttemptId
                    || previous.ActionId != reservation.ActionId
                    || previous.OwnerId != reservation.OwnerId
                    || previous.QuoteRevision != reservation.QuoteRevision
                    || previous.Provider != reservation.Provider
                    || previous.ProviderAccount != reservation.ProviderAccount
                    || previous.SourcePreparationReference != reservation.SourcePreparationReference
                    || (
                        previous.TermsJson is null
                            ? null
                            : JsonSerializer.Serialize(
                                JsonSerializer.Deserialize<StayTerms>(previous.TermsJson)
                            )
                    )
                        != (
                            reservation.Terms is null
                                ? null
                                : JsonSerializer.Serialize(reservation.Terms)
                        )
                )
            )
                return HotelErrors.Conflict;
            reservation.Version = checked(expectedVersion + 1);
            var row = To(reservation);
            if (expectedVersion == 0)
                db.Reservations.Add(row);
            else
            {
                db.Reservations.Attach(row);
                db.Entry(row).State = EntityState.Modified;
                db.Entry(row).Property(r => r.Version).OriginalValue = expectedVersion;
            }
            foreach (var item in work)
                await outbox.PublishAsync(item, new DeliveryOptions { ScheduledTime = item.DueAt });
            await outbox.SaveChangesAndFlushMessagesAsync(ct);
            db.Entry(row).State = EntityState.Detached;
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            await DiscardFailedAttemptAsync(ct);
            return false;
        }
        catch (DbUpdateException ex)
            when (ex.InnerException
                    is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation }
            )
        {
            await DiscardFailedAttemptAsync(ct);
            return false;
        }
        catch (Exception ex)
            when (ex is DbException or DbUpdateException or InvalidOperationException)
        {
            await DiscardFailedAttemptAsync(ct);
            return HotelErrors.Unavailable;
        }
    }

    private async Task DiscardFailedAttemptAsync(CancellationToken ct)
    {
        // Publish writes durable rows before EF performs its CAS. Roll back that whole failed save,
        // and discard only this scoped Hotels outbox's outstanding envelopes before a retry.
        if (outbox is not MessageContext messages)
            throw new InvalidOperationException(
                "Hotels outbox transaction binding is unavailable."
            );
        var binding = messages.Transaction;
        if (db.Database.CurrentTransaction is { } transaction)
        {
            await transaction.RollbackAsync(ct);
            await transaction.DisposeAsync();
        }
        await messages.ClearAllAsync();
        if (binding is null)
            throw new InvalidOperationException(
                "Hotels outbox transaction binding is unavailable."
            );
        messages.EnlistInOutbox(binding);
        db.ChangeTracker.Clear();
    }

    public async Task<int> PurgePrivateContentAsync(DateTimeOffset now, CancellationToken ct)
    {
        var rows = await db
            .Reservations.AsNoTracking()
            .Where(r =>
                r.PrivatePurgeAt != null
                && r.PrivatePurgeAt <= now
                && (r.TermsJson != null || r.ProtectedContext != null || r.ProtectedParty != null)
            )
            .OrderBy(r => r.PrivatePurgeAt)
            .Take(100)
            .ToArrayAsync(ct);
        // The deadline/claim predicate is repeated by the atomic update: an admitted unresolved purchase cannot be purged by a stale cleanup selection.
        var removed = 0;
        foreach (var row in rows)
        {
            var state = JsonSerializer.Deserialize<ReservationState>(row.StateJson)!;
            state.Facts = state.BoundBookingId is null
                ? null
                : new(state.BoundBookingId, null, null);
            state.FirstMismatch = null;
            var minimal = JsonSerializer.Serialize(state);
            removed += await db
                .Reservations.Where(r =>
                    r.Id == row.Id
                    && r.Version == row.Version
                    && r.PrivatePurgeAt != null
                    && r.PrivatePurgeAt <= now
                )
                .ExecuteUpdateAsync(
                    s =>
                        s.SetProperty(r => r.ProtectedParty, (string?)null)
                            .SetProperty(r => r.ProtectedContext, (string?)null)
                            .SetProperty(r => r.TermsJson, (string?)null)
                            .SetProperty(r => r.StateJson, minimal)
                            .SetProperty(r => r.Version, r => r.Version + 1),
                    ct
                );
        }
        return removed;
    }

    internal static HotelReservation From(HotelReservationEntity row) =>
        new()
        {
            Id = row.Id,
            OwnerId = row.OwnerId,
            Version = row.Version,
            QuoteRevision = row.QuoteRevision,
            Provider = row.Provider,
            Environment = row.Environment,
            ProviderAccount = row.ProviderAccount,
            SourcePreparationReference = row.SourcePreparationReference,
            ProviderRequestReference = row.ProviderRequestReference,
            OutcomeDiagnostic = row.OutcomeDiagnostic,
            Terms = row.TermsJson is null
                ? null
                : JsonSerializer.Deserialize<StayTerms>(row.TermsJson),
            State = JsonSerializer.Deserialize<ReservationState>(row.StateJson)!,
            ValidUntil = row.ValidUntil,
            CreatedAt = row.CreatedAt,
            UpdatedAt = row.UpdatedAt,
            AttemptId = row.AttemptId,
            ActionId = row.ActionId,
            AdmittedAt = row.AdmittedAt,
            SenderFinished = row.SenderFinished,
            ProtectedContext = row.ProtectedContext,
            ProtectedParty = row.ProtectedParty,
            PrivatePurgeAt = row.PrivatePurgeAt,
        };

    private static HotelReservationEntity To(HotelReservation row) =>
        new()
        {
            Id = row.Id,
            OwnerId = row.OwnerId,
            Version = row.Version,
            QuoteRevision = row.QuoteRevision,
            Provider = row.Provider,
            Environment = row.Environment,
            ProviderAccount = row.ProviderAccount,
            BookingReference = row.State.BoundBookingId,
            SourcePreparationReference = row.SourcePreparationReference,
            ProviderRequestReference = row.ProviderRequestReference,
            OutcomeDiagnostic = row.OutcomeDiagnostic,
            TermsJson = row.Terms is null ? null : JsonSerializer.Serialize(row.Terms),
            StateJson = JsonSerializer.Serialize(row.State),
            ValidUntil = row.ValidUntil,
            CreatedAt = row.CreatedAt,
            UpdatedAt = row.UpdatedAt,
            AttemptId = row.AttemptId,
            ActionId = row.ActionId,
            AdmittedAt = row.AdmittedAt,
            SenderFinished = row.SenderFinished,
            ProtectedContext = row.ProtectedContext,
            ProtectedParty = row.ProtectedParty,
            PrivatePurgeAt = row.PrivatePurgeAt,
        };

    private static async Task<ErrorOr<T>> Guard<T>(Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (Exception ex)
            when (ex
                    is DbException
                        or DbUpdateException
                        or InvalidOperationException
                        or JsonException
            )
        {
            return HotelErrors.Unavailable;
        }
    }
}
