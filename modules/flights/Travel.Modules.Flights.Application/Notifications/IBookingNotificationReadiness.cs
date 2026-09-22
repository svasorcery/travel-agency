using Travel.Modules.Flights.Application.Queries;

namespace Travel.Modules.Flights.Application.Notifications;

public interface IBookingNotificationReadiness
{
    Task<OrderView> RequireAsync(
        Guid aggregateId,
        Guid userId,
        long? requiredStreamVersion,
        CancellationToken ct
    );
}
