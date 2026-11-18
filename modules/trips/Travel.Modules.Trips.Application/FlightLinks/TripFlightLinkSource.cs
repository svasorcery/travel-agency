using ErrorOr;
using Travel.IntegrationContracts.Flights.Trips;
using Travel.Modules.Trips.Core;

namespace Travel.Modules.Trips.Application.FlightLinks;

public sealed class TripFlightLinkSource(IFlightsTripsOrderAccess orders)
{
    internal async Task<ErrorOr<TripFlightObservation>> ObserveAsync(
        OwnerIdentity owner,
        Guid orderId,
        TimeProvider clock,
        CancellationToken ct
    )
    {
        ct.ThrowIfCancellationRequested();
        var read = await orders.ReadAsync(owner.Subject, orderId, ct);
        ct.ThrowIfCancellationRequested();
        if (read.IsError)
            return read.FirstError.Type == ErrorType.NotFound
                ? TripErrors.NotFound
                : TripErrors.Unavailable;
        var value = read.Value;
        if (
            value is null
            || value.OrderId != orderId
            || value.OwnerId != owner.Subject
            || value.StreamVersion <= 0
            || value.ObservedAt == default
            || value.ObservedAt.Offset != TimeSpan.Zero
            || value.ObservedAt > clock.GetUtcNow()
            || !ValidSummary(value.BookingStatus, value.PassengerCount, value.Route)
        )
            return TripErrors.Unavailable;
        return new TripFlightObservation(
            value.BookingStatus.ToString(),
            value.PassengerCount,
            MapRoute(value.Route),
            value.ReviewRequired,
            value.ObservedAt,
            value.StreamVersion
        );
    }

    internal async Task<ErrorOr<TripFlightCandidatePage>> CandidatesAsync(
        OwnerIdentity owner,
        Guid tripId,
        int offset,
        CancellationToken ct
    )
    {
        ct.ThrowIfCancellationRequested();
        var read = await orders.ListCandidatesAsync(owner.Subject, offset, ct);
        ct.ThrowIfCancellationRequested();
        if (read.IsError || read.Value?.Items is not { Count: <= 20 } items)
            return TripErrors.Unavailable;
        var page = read.Value;
        if (
            page.NextOffset.HasValue
                && (items.Count != 20 || page.NextOffset != offset + 20 || page.NextOffset > 10000)
            || page.PageBoundReached
                && (items.Count != 20 || page.NextOffset.HasValue || offset + 20 <= 10000)
        )
            return TripErrors.Unavailable;
        var mapped = new List<TripFlightCandidate>(items.Count);
        var ids = new HashSet<Guid>();
        foreach (var candidate in items)
        {
            if (
                candidate is null
                || candidate.OrderId == Guid.Empty
                || !ids.Add(candidate.OrderId)
                || !ValidSummary(candidate.BookingStatus, candidate.PassengerCount, candidate.Route)
            )
                return TripErrors.Unavailable;
            mapped.Add(
                new(
                    candidate.OrderId,
                    candidate.BookingStatus.ToString(),
                    candidate.PassengerCount,
                    MapRoute(candidate.Route)
                )
            );
        }
        return new TripFlightCandidatePage(
            tripId,
            mapped.AsReadOnly(),
            page.NextOffset,
            page.PageBoundReached
        );
    }

    private static bool ValidSummary(
        FlightOrderBookingStatus status,
        int passengerCount,
        IReadOnlyList<FlightOrderLeg>? route
    )
    {
        if (!Enum.IsDefined(status) || passengerCount is < 1 or > 9)
            return false;
        if (route is null)
            return true;
        if (route.Count is < 1 or > 4)
            return false;
        DateTimeOffset? lastArrival = null;
        foreach (var leg in route)
        {
            if (
                leg is null
                || !Airport(leg.Origin)
                || !Airport(leg.Destination)
                || leg.DepartAt == default
                || leg.ArriveAt == default
                || leg.ArriveAt <= leg.DepartAt
                || leg.ArriveAt - leg.DepartAt >= TimeSpan.FromDays(2)
                || leg.DepartAt < lastArrival
            )
                return false;
            lastArrival = leg.ArriveAt;
        }
        return true;
    }

    private static bool Airport(string? value) =>
        value is { Length: 3 } && value.All(c => c is >= 'A' and <= 'Z');

    private static IReadOnlyList<TripFlightLeg>? MapRoute(IReadOnlyList<FlightOrderLeg>? route) =>
        route
            ?.Select(x => new TripFlightLeg(x.Origin, x.Destination, x.DepartAt, x.ArriveAt))
            .ToArray();
}
