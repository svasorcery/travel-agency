using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using JasperFx.Events;
using Microsoft.EntityFrameworkCore;
using Travel.Modules.Flights.Application.Observability;
using Travel.Modules.Flights.Application.ReadModels;
using Travel.Modules.Flights.Core.Aggregates;
using Travel.Modules.Flights.Core.DomainEvents;
using Travel.Modules.Flights.Infrastructure.Persistence.Entities;
using IDocumentStore = Marten.IDocumentStore;

namespace Travel.Modules.Flights.Infrastructure.Persistence;

public sealed class OrderReadModelReconciler(
    IDocumentStore store,
    DbContextOptions<FlightsDbContext> options,
    IBookingProjectionMaintenanceContext maintenance,
    IBookingProjectionMetrics? metrics = null,
    TimeProvider? timeProvider = null
) : IOrderReadModelReconciler
{
    public async Task<ReconcileResult> ReconcileAsync(
        Guid aggregateId,
        OrderReadModelReconcileMode mode,
        CancellationToken ct
    )
    {
        var clock = timeProvider ?? TimeProvider.System;
        var started = clock.GetTimestamp();
        try
        {
            var result = await ReconcileCoreAsync(aggregateId, mode, ct);
            metrics?.RecordReconcile(
                result.AppliedEvents == 0 ? "unchanged" : "applied",
                result.AppliedEvents,
                clock.GetElapsedTime(started).TotalMilliseconds
            );
            metrics?.RecordSourceCheckpointLag(result.SourceVersion, result.PreviousVersion);
            return result;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error)
        {
            metrics?.RecordReconcile("failed", 0, clock.GetElapsedTime(started).TotalMilliseconds);
            metrics?.RecordProjectionFailure(FailureCategory(error));
            throw;
        }
    }

    private static string FailureCategory(Exception error) =>
        error switch
        {
            BookingProjectionTransientException => "storage",
            BookingProjectionTerminalException
            {
                Message: "CheckpointAhead" or "ProjectionBootstrapRequired"
            } => "checkpoint",
            BookingProjectionTerminalException
            {
                Message: "SourceOwnerMissing" or "SourceOwnerConflict" or "ProjectionOwnerMismatch"
            } => "ownership",
            BookingProjectionTerminalException => "source",
            _ => "unknown",
        };

    private async Task<ReconcileResult> ReconcileCoreAsync(
        Guid aggregateId,
        OrderReadModelReconcileMode mode,
        CancellationToken ct
    )
    {
        if (
            mode
            is not (OrderReadModelReconcileMode.Incremental or OrderReadModelReconcileMode.Reset)
        )
            throw new ArgumentOutOfRangeException(nameof(mode));
        if (mode == OrderReadModelReconcileMode.Reset)
            maintenance.RequireExclusiveReset();
        try
        {
            await using var db = new FlightsDbContext(options);
            var existing = await db
                .Orders.AsNoTracking()
                .SingleOrDefaultAsync(x => x.AggregateId == aggregateId, ct);
            long? previous = existing?.ProjectedStreamVersion;
            if (mode == OrderReadModelReconcileMode.Incremental && previous < 0)
                throw new BookingProjectionTerminalException("ProjectionBootstrapRequired");
            var start = mode == OrderReadModelReconcileMode.Reset ? 0 : previous ?? 0;
            var (target, events) = await ReadSourceAsync(aggregateId, start, ct);
            if (mode == OrderReadModelReconcileMode.Incremental && existing is not null)
                await VerifyPersistedOwnerAsync(existing, ct);
            if (mode == OrderReadModelReconcileMode.Incremental && previous == target)
                return new(aggregateId, previous, target, previous, 0, existing is not null);

            var row =
                mode == OrderReadModelReconcileMode.Incremental && existing is not null
                    ? existing
                    : EmptyRow(aggregateId, existing?.Id);
            foreach (var envelope in events)
                OrderReadModelEventApplier.Apply(row, envelope);
            var materialized = OrderReadModelEventApplier.ShouldMaterialize(row);
            if (!materialized)
            {
                if (existing is not null)
                {
                    if (mode != OrderReadModelReconcileMode.Reset)
                        throw new BookingProjectionTerminalException("ProjectionUnexpected");
                    db.Orders.Remove(existing);
                    await db.SaveChangesAsync(ct);
                }
                return new(aggregateId, previous, target, null, events.Count, false);
            }

            if (existing is null)
                db.Orders.Add(row);
            else
            {
                db.Attach(row);
                db.Entry(row).State = EntityState.Modified;
                db.Entry(row).Property(x => x.ProjectedStreamVersion).OriginalValue =
                    previous!.Value;
            }
            await db.SaveChangesAsync(ct);
            return new(aggregateId, previous, target, target, events.Count, true);
        }
        catch (Exception error)
        {
            ExceptionDispatchInfo
                .Capture(BookingStorageFailureClassifier.Classify(error, ct))
                .Throw();
            throw;
        }
    }

    public async Task<ProjectionValidation> ValidateAsync(Guid aggregateId, CancellationToken ct)
    {
        try
        {
            await using var db = new FlightsDbContext(options);
            var existing = await db
                .Orders.AsNoTracking()
                .SingleOrDefaultAsync(x => x.AggregateId == aggregateId, ct);
            long? target = null;
            var expected = EmptyRow(aggregateId, existing?.Id);
            bool materialized;
            try
            {
                var source = await ReadSourceAsync(aggregateId, 0, ct);
                target = source.Target;
                foreach (var envelope in source.Events)
                    OrderReadModelEventApplier.Apply(expected, envelope);
                materialized = OrderReadModelEventApplier.ShouldMaterialize(expected);
            }
            catch (BookingProjectionTerminalException error)
            {
                return new(
                    aggregateId,
                    target,
                    existing?.ProjectedStreamVersion,
                    false,
                    [new(error.Message, false)]
                );
            }

            var issues = new List<ProjectionIssue>();
            if (!materialized && existing is not null)
                issues.Add(new("ProjectionUnexpected", true));
            if (materialized && existing is null)
                issues.Add(new("ProjectionMissing", true));
            if (existing is not null)
            {
                if (existing.ProjectedStreamVersion < 0)
                    issues.Add(new("ProjectionBootstrapRequired", true));
                else if (existing.ProjectedStreamVersion > target)
                    issues.Add(new("CheckpointAhead", true));
                else if (existing.ProjectedStreamVersion < target)
                    issues.Add(new("CheckpointBehind", true));
                if (materialized && !FieldsMatch(existing, expected))
                    issues.Add(new("DerivedFieldsMismatch", true));
            }
            return new(aggregateId, target, existing?.ProjectedStreamVersion, materialized, issues);
        }
        catch (Exception error)
        {
            ExceptionDispatchInfo
                .Capture(BookingStorageFailureClassifier.Classify(error, ct))
                .Throw();
            throw;
        }
    }

    private async Task<(long Target, IReadOnlyList<IEvent> Events)> ReadSourceAsync(
        Guid id,
        long after,
        CancellationToken ct
    )
    {
        await using var session = store.QuerySession();
        var state = await session.Events.FetchStreamStateAsync(id, ct);
        if (state is null)
            throw new BookingProjectionTerminalException("SourceMissing");
        if (state.AggregateType != typeof(BookingAggregate))
            throw new BookingProjectionTerminalException("SourceStreamTypeInvalid");
        var target = state.Version;
        if (after > target)
            throw new BookingProjectionTerminalException("CheckpointAhead");
        if (after == target)
            return (target, Array.Empty<IEvent>());
        IReadOnlyList<IEvent> events;
        try
        {
            events = await session.Events.FetchStreamAsync(
                id,
                version: target,
                fromVersion: after + 1,
                token: ct
            );
        }
        catch (JsonException error)
        {
            throw new BookingProjectionTerminalException("SourcePayloadUnreadable", error);
        }
        var expected = after + 1;
        foreach (var envelope in events)
        {
            if (envelope.StreamId != id || envelope.Version != expected++)
                throw new BookingProjectionTerminalException("SourceVersionGap");
        }
        if (expected != target + 1)
            throw new BookingProjectionTerminalException("SourceVersionGap");
        return (target, events);
    }

    private async Task VerifyPersistedOwnerAsync(
        OrderReadModelEntity existing,
        CancellationToken ct
    )
    {
        // The suffix may contain no ownership event. Check its immutable source fact before
        // trusting EF ownership, including on equal-version delivery. Do not re-project the prefix.
        if (existing.ProjectedStreamVersion == 0)
            throw new BookingProjectionTerminalException("ProjectionBootstrapRequired");
        await using var session = store.QuerySession();
        var prefix = await session.Events.FetchStreamAsync(
            existing.AggregateId,
            version: existing.ProjectedStreamVersion,
            token: ct
        );
        long version = 1;
        Guid? owner = null;
        var sawHold = false;
        foreach (var envelope in prefix)
        {
            if (envelope.StreamId != existing.AggregateId || envelope.Version != version++)
                throw new BookingProjectionTerminalException("SourceVersionGap");
            if (envelope.Data is not OfferHeld held)
                continue;
            sawHold = true;
            if (held.OwnerUserId is not { } sourceOwner || sourceOwner == Guid.Empty)
                throw new BookingProjectionTerminalException("SourceOwnerMissing");
            if (owner is { } previousOwner && previousOwner != sourceOwner)
                throw new BookingProjectionTerminalException("SourceOwnerConflict");
            owner = sourceOwner;
        }
        if (version != existing.ProjectedStreamVersion + 1)
            throw new BookingProjectionTerminalException("SourceVersionGap");
        if (!sawHold)
            throw new BookingProjectionTerminalException("ProjectionUnexpected");
        if (owner != existing.UserId)
            throw new BookingProjectionTerminalException("ProjectionOwnerMismatch");
    }

    private static OrderReadModelEntity EmptyRow(Guid id, Guid? rowId) =>
        new()
        {
            Id = rowId ?? Guid.NewGuid(),
            AggregateId = id,
            ProjectedStreamVersion = 0,
        };

    private static bool FieldsMatch(OrderReadModelEntity actual, OrderReadModelEntity expected) =>
        actual.UserId == expected.UserId
        && actual.ProviderOrderId == expected.ProviderOrderId
        && actual.Status == expected.Status
        && actual.TotalAmount == expected.TotalAmount
        && actual.Currency == expected.Currency
        && TimestampMatches(actual.BookedAt, expected.BookedAt)
        && TimestampMatches(actual.TicketedAt, expected.TicketedAt)
        && TimestampMatches(actual.CancelledAt, expected.CancelledAt)
        && TimestampMatches(actual.RefundedAt, expected.RefundedAt)
        && actual.TicketNumbers.SequenceEqual(expected.TicketNumbers)
        && JsonMatches(actual.ItineraryJson, expected.ItineraryJson)
        && JsonMatches(actual.PassengerInfoJson, expected.PassengerInfoJson);

    private static bool TimestampMatches(DateTimeOffset? actual, DateTimeOffset? expected)
    {
        if (actual is null || expected is null)
            return actual == expected;
        // Npgsql stores integer microseconds from PostgreSQL's 2000-01-01 epoch.
        // Event JSON retains 100ns ticks; compare the persisted representation.
        const long postgresEpochTicks = 630822816000000000;
        return (actual.Value.UtcTicks - postgresEpochTicks) / 10
            == (expected.Value.UtcTicks - postgresEpochTicks) / 10;
    }

    private static bool JsonMatches(string actual, string expected)
    {
        try
        {
            return JsonNode.DeepEquals(JsonNode.Parse(actual), JsonNode.Parse(expected));
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
