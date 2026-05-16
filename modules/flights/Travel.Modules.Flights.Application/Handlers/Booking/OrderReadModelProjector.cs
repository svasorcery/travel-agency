using Travel.Modules.Flights.Core.Aggregates;

namespace Travel.Modules.Flights.Application.Handlers.Booking;

/// <summary>
/// Projects a BookingAggregate into the OrderReadModel EF table.
/// Concrete implementation is in Infrastructure to avoid a circular project reference
/// (Infrastructure already references Application). Register via DI in the composition root.
/// </summary>
public interface IOrderReadModelProjector
{
    Task Project(BookingAggregate agg, Guid userId, CancellationToken ct);
}
