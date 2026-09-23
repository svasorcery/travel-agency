using System.Text.Json;
using Travel.Modules.Flights.Application.Contracts;
using Travel.Modules.Flights.Application.Notifications;
using Travel.Modules.Flights.Application.Queries;
using Wolverine.Attributes;

namespace Travel.Modules.Flights.Application.Handlers.Notifications;

public static class PublishOrderSseHandler
{
    [WolverineHandler]
    public static async Task Handle(
        OrderConfirmedNotification evt,
        IBookingNotificationReadiness readiness,
        IOrderSseRegistry registry,
        TimeProvider time,
        CancellationToken ct
    )
    {
        var order = await readiness.RequireAsync(
            evt.AggregateId,
            evt.UserId,
            evt.RequiredStreamVersion,
            ct
        );
        if (order.Status is "Confirmed" or "Ticketed")
            Publish(order, registry, time);
    }

    [WolverineHandler]
    public static async Task Handle(
        OrderTicketedNotification evt,
        IBookingNotificationReadiness readiness,
        IOrderSseRegistry registry,
        TimeProvider time,
        CancellationToken ct
    )
    {
        var order = await readiness.RequireAsync(
            evt.AggregateId,
            evt.UserId,
            evt.RequiredStreamVersion,
            ct
        );
        if (order.Status == "Ticketed")
            Publish(order, registry, time);
    }

    [WolverineHandler]
    public static async Task Handle(
        OrderCancelledNotification evt,
        IBookingNotificationReadiness readiness,
        IOrderSseRegistry registry,
        TimeProvider time,
        CancellationToken ct
    )
    {
        var order = await readiness.RequireAsync(
            evt.AggregateId,
            evt.UserId,
            evt.RequiredStreamVersion,
            ct
        );
        if (order.Status == "Cancelled")
            Publish(order, registry, time);
    }

    private static void Publish(OrderView order, IOrderSseRegistry registry, TimeProvider time) =>
        registry.Publish(
            order.AggregateId,
            new SseEvent(
                "Order" + order.Status,
                order.AggregateId,
                JsonSerializer.SerializeToElement(new { status = order.Status }),
                time.GetUtcNow(),
                order.ProjectedStreamVersion
            )
        );
}
