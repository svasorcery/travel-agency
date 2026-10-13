using ErrorOr;
using Marten;
using Travel.IntegrationContracts.Flights;
using Travel.Modules.Flights.Core.Aggregates;
using Travel.Modules.Flights.Core.ValueObjects;

namespace Travel.Modules.Flights.Application.Support;

/// <summary>One authoritative owner-bound source read; no provider, projection, decryption or dispatch.</summary>
public sealed class FlightsSupportTriageContext(IDocumentSession session, TimeProvider time)
    : IFlightsSupportTriageContext
{
    public async Task<ErrorOr<FlightSupportTriageFacts>> ReadAsync(
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
                return Unavailable();
            var now = time.GetUtcNow();
            var observed = new DateTimeOffset(now.UtcTicks / 10 * 10, TimeSpan.Zero);
            FlightSupportTriageFacts Facts(
                FlightSupportJourneyState state,
                DateTimeOffset? departure = null
            ) => new(orderId, ownerId, version, observed, state, departure?.ToUniversalTime());
            if (
                booking.HasSupplierCancellationReportBarrier
                || booking.HasUnresolvedCreation
                || booking.HasConfirmationBarrier
                || booking.CurrentCancellation is { IsTerminal: false }
            )
                return Facts(FlightSupportJourneyState.Unknown);
            if (
                booking.Status is BookingStatus.Cancelled or BookingStatus.Refunded
                || booking.ObservedSupplierCancellation is not null
            )
                return Facts(FlightSupportJourneyState.Closed);
            if (
                booking.Status == BookingStatus.Held
                && (booking.ExpiresAt is null || booking.ExpiresAt <= now)
            )
                return Facts(FlightSupportJourneyState.Unknown);
            if (booking.Itinerary is null || Itinerary.Create(booking.Itinerary.Slices).IsError)
                return Facts(FlightSupportJourneyState.Unknown);
            var next = booking
                .Itinerary.Slices.SelectMany(slice => slice.Segments)
                .Select(segment => (DateTimeOffset?)segment.DepartAt)
                .Where(at => at > now)
                .Min();
            return next is null
                ? Facts(FlightSupportJourneyState.Unknown)
                : Facts(FlightSupportJourneyState.Active, next);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return Unavailable();
        }
    }

    private static Error NotFound() =>
        Error.NotFound("Flights.OrderAccessNotFound", "Order was not found.");

    private static Error Unavailable() =>
        Error.Failure("Flights.OrderAccessUnavailable", "Order context is unavailable.");
}
