using Travel.Modules.Flights.Core.Aggregates;

namespace Travel.Modules.Flights.Application.Queries;

public sealed record GetBookingCreationQuery(Guid AggregateId, Guid UserId);

public sealed record BookingCreationStatusResult(
    BookingAggregate Booking,
    DateTimeOffset ObservedAt
);
