using ErrorOr;
using Marten;
using Travel.IntegrationContracts.Flights;
using Travel.Modules.Flights.Core.Aggregates;

namespace Travel.Modules.Flights.Application.Support;

/// <summary>Published source ownership read; no projection, passenger decryption or business dispatch.</summary>
public sealed class FlightsSupportOrderAccess(IDocumentSession session, TimeProvider time)
    : IFlightsSupportOrderAccess
{
    public async Task<ErrorOr<FlightOrderOwnershipProof>> VerifyOwnershipAsync(
        Guid ownerId,
        Guid orderId,
        CancellationToken cancellationToken = default
    )
    {
        if (ownerId == Guid.Empty || orderId == Guid.Empty)
            return NotFound();
        try
        {
            var stream = await session.Events.FetchForWriting<BookingAggregate>(
                orderId,
                cancellationToken
            );
            var booking = stream.Aggregate;
            if (
                booking is null
                || booking.Id != orderId
                || booking.OwnerUserId != ownerId
                || !booking.HasConsistentMutationOwner
                || booking.Status
                    is not (
                        BookingStatus.Held
                        or BookingStatus.Confirmed
                        or BookingStatus.Ticketed
                        or BookingStatus.Cancelled
                        or BookingStatus.Refunded
                    )
            )
                return NotFound();
            if (stream.CurrentVersion is not { } version || version <= 0)
                return Error.Failure(
                    "Flights.OrderAccessUnavailable",
                    "Order ownership is unavailable."
                );
            var now = time.GetUtcNow();
            var observed = new DateTimeOffset(now.UtcTicks - now.UtcTicks % 10, TimeSpan.Zero);
            return new FlightOrderOwnershipProof(orderId, ownerId, version, observed);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return Error.Failure(
                "Flights.OrderAccessUnavailable",
                "Order ownership is unavailable."
            );
        }
    }

    private static Error NotFound() =>
        Error.NotFound("Flights.OrderAccessNotFound", "Order was not found.");
}
