using System.Globalization;
using Travel.Modules.Flights.Application.Contracts;
using Travel.Modules.Flights.Application.Notifications;
using Travel.Modules.Flights.Application.Queries;
using Wolverine.Attributes;

namespace Travel.Modules.Flights.Application.Handlers.Notifications;

public static class SendOrderCancellationEmailHandler
{
    [WolverineHandler]
    public static async Task Handle(
        OrderCancelledNotification evt,
        IOrderReadModelQueries orders,
        IEmailSender sender,
        IEmailRenderer renderer,
        IUserDirectory users,
        CancellationToken ct
    )
    {
        var order = await orders.GetAsync(evt.AggregateId, evt.UserId, ct);
        if (order is null)
            return;

        var user = await users.GetAsync(evt.UserId, ct);
        if (user is null)
            return;

        var locale = SendOrderConfirmationEmailHandler.GetLocale(user.Locale);
        var model = BuildModel(order, user);

        var rendered = await renderer.RenderAsync("OrderCancellation", model, locale, ct);
        await sender.SendAsync(
            user.Email,
            rendered.Subject,
            rendered.HtmlBody,
            rendered.TextBody,
            ct
        );
    }

    private static OrderEmailModel BuildModel(OrderView order, UserProfile user)
    {
        var bookingRef = order.AggregateId.ToString("N")[..6].ToUpperInvariant();
        var itinerary = SendOrderConfirmationEmailHandler.ParseItinerarySummary(
            order.ItineraryJson
        );
        var total = $"{order.TotalAmount:N2} {order.Currency}";
        var guestName = $"{user.GivenName} {user.FamilyName}".Trim();
        if (string.IsNullOrWhiteSpace(guestName))
            guestName = user.Email;

        return new OrderEmailModel(
            GuestName: guestName,
            OrderId: order.AggregateId.ToString("N"),
            ItinerarySummary: itinerary,
            TotalFormatted: total,
            BookingRef: bookingRef
        );
    }
}
