using ErrorOr;
using JasperFx.Events;
using Marten;
using Travel.Modules.Flights.Application.Booking;
using Travel.Modules.Flights.Application.ReadModels;
using Travel.Modules.Flights.Core.Errors;
using Wolverine;
using Wolverine.Marten;

namespace Travel.Modules.Flights.Application.Persistence;

/// <summary>
/// Extension methods for <see cref="IDocumentSession"/> shared across booking handlers.
/// </summary>
public static class DocumentSessionExtensions
{
    public static async Task SaveBookingWithWorkAsync(
        this IDocumentSession session,
        IMartenOutbox outbox,
        Guid aggregateId,
        IReadOnlyList<BookingWork> work,
        IReadOnlyList<object> notifications,
        CancellationToken ct
    )
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(outbox);
        ArgumentNullException.ThrowIfNull(work);
        ArgumentNullException.ThrowIfNull(notifications);
        outbox.Enroll(session);
        foreach (var item in work)
        {
            ArgumentNullException.ThrowIfNull(item);
            ArgumentNullException.ThrowIfNull(item.Message);
            await outbox.PublishAsync(
                item.Message,
                new DeliveryOptions { ScheduledTime = item.DueAt }
            );
        }
        foreach (var notification in notifications)
        {
            ArgumentNullException.ThrowIfNull(notification);
            await outbox.PublishAsync(notification);
        }
        await outbox.PublishAsync(new ReconcileOrderReadModel(aggregateId));
        try
        {
            await session.SaveChangesAsync(ct);
        }
        catch (EventStreamUnexpectedMaxEventIdException exception)
        {
            throw new BookingWriteConflictException(aggregateId, exception);
        }
    }

    /// <summary>
    /// Commits booking events together with their durable read-model reconciliation request and
    /// any sibling notifications through one enrolled Marten outbox transaction.
    /// </summary>
    public static Task SaveBookingWithReconcileAsync(
        this IDocumentSession session,
        IMartenOutbox outbox,
        Guid aggregateId,
        IReadOnlyList<object> notifications,
        CancellationToken ct
    ) => session.SaveBookingWithWorkAsync(outbox, aggregateId, [], notifications, ct);

    /// <summary>
    /// Saves changes and maps an optimistic-concurrency violation to
    /// <see cref="FlightsErrors.ConcurrencyConflict"/>, eliminating the boilerplate
    /// try/catch in every booking handler.
    /// </summary>
    internal static async Task<ErrorOr<Success>> SaveOrConcurrencyConflictAsync(
        this IDocumentSession session,
        IMartenOutbox outbox,
        Guid aggregateId,
        IReadOnlyList<object> notifications,
        CancellationToken ct
    )
    {
        try
        {
            await session.SaveBookingWithReconcileAsync(outbox, aggregateId, notifications, ct);
            return Result.Success;
        }
        catch (BookingWriteConflictException)
        {
            return FlightsErrors.ConcurrencyConflict;
        }
    }
}

public sealed record BookingWork(object Message, DateTimeOffset? DueAt);
