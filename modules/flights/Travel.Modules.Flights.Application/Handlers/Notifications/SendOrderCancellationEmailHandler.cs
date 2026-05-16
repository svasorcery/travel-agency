using System.Globalization;
using Travel.Modules.Flights.Application.Contracts;
using Travel.Modules.Flights.Application.Notifications;
using Travel.Modules.Flights.Application.Queries;
using Travel.Modules.Flights.Core.DomainEvents;
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
        var model = BuildModel(order, user, evt.Reason, locale);

        var rendered = await renderer.RenderAsync("OrderCancellation", model, locale, ct);
        await sender.SendAsync(
            user.Email,
            rendered.Subject,
            rendered.HtmlBody,
            rendered.TextBody,
            ct
        );
    }

    private static OrderEmailModel BuildModel(
        OrderView order,
        UserProfile user,
        CancelReason reason,
        CultureInfo locale
    )
    {
        var bookingRef = order.AggregateId.ToString("N")[..6].ToUpperInvariant();
        var itinerary = SendOrderConfirmationEmailHandler.ParseItinerarySummary(
            order.ItineraryJson
        );
        var total = $"{order.TotalAmount:N2} {order.Currency}";
        var guestName = $"{user.GivenName} {user.FamilyName}".Trim();
        if (string.IsNullOrWhiteSpace(guestName))
            guestName = user.Email;

        var (cancelReasonText, refundText) = ResolveLocalisedCancelTexts(reason, locale);

        return new OrderEmailModel(
            GuestName: guestName,
            OrderId: order.AggregateId.ToString("N"),
            ItinerarySummary: itinerary,
            TotalFormatted: total,
            BookingRef: bookingRef,
            CancelReasonText: cancelReasonText,
            RefundText: refundText
        );
    }

    /// <summary>
    /// Returns localised (cancelReasonText, refundText) for the given cancel reason and locale.
    /// Falls back to Russian strings for any locale that is not explicitly mapped.
    /// </summary>
    internal static (string CancelReasonText, string RefundText) ResolveLocalisedCancelTexts(
        CancelReason reason,
        CultureInfo locale
    )
    {
        var isEn = locale.TwoLetterISOLanguageName.Equals("en", StringComparison.OrdinalIgnoreCase);

        if (isEn)
        {
            var reasonEn = reason switch
            {
                CancelReason.User => "Cancelled by passenger",
                CancelReason.Airline => "Cancelled by airline",
                CancelReason.System => "Cancelled by system",
                _ => "Cancelled",
            };
            return (reasonEn, "Your refund will be processed within 5–10 business days.");
        }
        else
        {
            var reasonRu = reason switch
            {
                CancelReason.User => "Отменено пассажиром",
                CancelReason.Airline => "Отменено авиакомпанией",
                CancelReason.System => "Отменено системой",
                _ => "Отменено",
            };
            return (reasonRu, "Возврат средств будет произведён в течение 5–10 рабочих дней.");
        }
    }
}
