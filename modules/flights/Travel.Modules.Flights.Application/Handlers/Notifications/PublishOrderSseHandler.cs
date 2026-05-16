using System.Text.Json;
using Travel.Modules.Flights.Application.Contracts;
using Travel.Modules.Flights.Application.Notifications;
using Wolverine.Attributes;

namespace Travel.Modules.Flights.Application.Handlers.Notifications;

public static class PublishOrderSseHandler
{
    [WolverineHandler]
    public static void Handle(
        OrderConfirmedNotification evt,
        IOrderSseRegistry registry,
        TimeProvider time
    )
    {
        var payload = JsonSerializer.SerializeToElement(new { status = "Confirmed" });
        registry.Publish(
            evt.AggregateId,
            new SseEvent("OrderConfirmed", evt.AggregateId, payload, time.GetUtcNow())
        );
    }

    [WolverineHandler]
    public static void Handle(
        OrderTicketedNotification evt,
        IOrderSseRegistry registry,
        TimeProvider time
    )
    {
        var payload = JsonSerializer.SerializeToElement(new { status = "Ticketed" });
        registry.Publish(
            evt.AggregateId,
            new SseEvent("OrderTicketed", evt.AggregateId, payload, time.GetUtcNow())
        );
    }

    [WolverineHandler]
    public static void Handle(
        OrderCancelledNotification evt,
        IOrderSseRegistry registry,
        TimeProvider time
    )
    {
        var payload = JsonSerializer.SerializeToElement(new { status = "Cancelled" });
        registry.Publish(
            evt.AggregateId,
            new SseEvent("OrderCancelled", evt.AggregateId, payload, time.GetUtcNow())
        );
    }
}
