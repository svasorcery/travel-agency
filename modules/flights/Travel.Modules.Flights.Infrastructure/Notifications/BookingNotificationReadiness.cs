using Marten;
using Microsoft.EntityFrameworkCore;
using Travel.Modules.Flights.Application.Notifications;
using Travel.Modules.Flights.Application.Queries;
using Travel.Modules.Flights.Application.ReadModels;
using Travel.Modules.Flights.Infrastructure.Persistence;

namespace Travel.Modules.Flights.Infrastructure.Notifications;

public sealed class BookingNotificationReadiness(
    IDocumentStore store,
    DbContextOptions<FlightsDbContext> options
) : IBookingNotificationReadiness
{
    public async Task<OrderView> RequireAsync(
        Guid aggregateId,
        Guid userId,
        long? requiredStreamVersion,
        CancellationToken ct
    )
    {
        if (requiredStreamVersion is <= 0)
            throw new BookingProjectionTerminalException("InvalidNotificationStreamVersion");
        var required = requiredStreamVersion;
        if (required is null)
        {
            await using var source = store.QuerySession();
            var state = await source.Events.FetchStreamStateAsync(aggregateId, ct);
            if (
                state is null
                || state.AggregateType
                    != typeof(Travel.Modules.Flights.Core.Aggregates.BookingAggregate)
            )
                throw new BookingProjectionTerminalException("NotificationSourceMissingOrInvalid");
            required = state.Version;
        }
        // A dedicated context prevents a prior tracked row from bypassing readiness.
        await using var db = new FlightsDbContext(options);
        var order = await new OrderReadModelQueries(db).GetAsync(aggregateId, userId, ct);
        if (order is null || order.ProjectedStreamVersion < required)
            throw new BookingReadModelNotReadyException("NotificationProjectionBehind");
        return order;
    }
}
