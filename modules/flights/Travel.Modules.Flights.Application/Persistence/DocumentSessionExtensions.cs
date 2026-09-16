using ErrorOr;
using JasperFx.Events;
using Marten;
using Travel.Modules.Flights.Application.Booking;
using Travel.Modules.Flights.Application.ReadModels;
using Travel.Modules.Flights.Core.Errors;
using Wolverine.Marten;

namespace Travel.Modules.Flights.Application.Persistence;

/// <summary>
/// Extension methods for <see cref="IDocumentSession"/> shared across booking handlers.
/// </summary>
public static class DocumentSessionExtensions
{
    /// <summary>
    /// Commits booking events together with their durable read-model reconciliation request and
    /// any sibling notifications through one enrolled Marten outbox transaction.
    /// </summary>
    public static async Task SaveBookingWithReconcileAsync(
        this IDocumentSession session,
        IMartenOutbox outbox,
        Guid aggregateId,
        IReadOnlyList<object> notifications,
        CancellationToken ct
    )
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(outbox);
        ArgumentNullException.ThrowIfNull(notifications);

        outbox.Enroll(session);
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
    /// Saves changes and maps an optimistic-concurrency violation to
    /// <see cref="FlightsErrors.ConcurrencyConflict"/>, eliminating the boilerplate
    /// try/catch in every booking handler.
    /// </summary>
    internal static async Task<ErrorOr<Success>> SaveOrConcurrencyConflictAsync(
        this IDocumentSession session,
        CancellationToken ct
    )
    {
        try
        {
            await session.SaveChangesAsync(ct);
            return Result.Success;
        }
        catch (EventStreamUnexpectedMaxEventIdException)
        {
            return FlightsErrors.ConcurrencyConflict;
        }
    }
}
