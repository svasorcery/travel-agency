using ErrorOr;
using JasperFx.Events;
using Marten;
using Travel.Modules.Flights.Core.Errors;

namespace Travel.Modules.Flights.Application.Handlers.Booking;

/// <summary>
/// Extension methods for <see cref="IDocumentSession"/> shared across booking handlers.
/// </summary>
internal static class DocumentSessionExtensions
{
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
