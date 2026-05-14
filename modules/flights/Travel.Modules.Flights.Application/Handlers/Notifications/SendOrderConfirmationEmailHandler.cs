using System.Globalization;
using Travel.Modules.Flights.Application.Contracts;
using Travel.Modules.Flights.Application.Notifications;
using Travel.Modules.Flights.Application.Queries;
using Wolverine.Attributes;

namespace Travel.Modules.Flights.Application.Handlers.Notifications;

public static class SendOrderConfirmationEmailHandler
{
    [WolverineHandler]
    public static async Task Handle(
        OrderConfirmedNotification evt,
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

        var locale = GetLocale(user.Locale);
        var model = BuildModel(order, user);

        var rendered = await renderer.RenderAsync("OrderConfirmation", model, locale, ct);
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
        var itinerary = ParseItinerarySummary(order.ItineraryJson);
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

    internal static CultureInfo GetLocale(string? locale)
    {
        if (string.IsNullOrWhiteSpace(locale))
            return CultureInfo.GetCultureInfo("ru");
        try
        {
            return CultureInfo.GetCultureInfo(locale);
        }
        catch
        {
            return CultureInfo.GetCultureInfo("ru");
        }
    }

    internal static string ParseItinerarySummary(string itineraryJson)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(itineraryJson);
            var root = doc.RootElement;

            // Try to extract origin/destination from common shapes
            if (
                root.TryGetProperty("origin", out var orig)
                && root.TryGetProperty("destination", out var dest)
            )
                return $"{orig.GetString()} → {dest.GetString()}";

            if (
                root.TryGetProperty("slices", out var slices)
                && slices.ValueKind == System.Text.Json.JsonValueKind.Array
            )
            {
                var firstSlice = slices.EnumerateArray().FirstOrDefault();
                if (firstSlice.ValueKind != System.Text.Json.JsonValueKind.Undefined)
                {
                    var o = firstSlice.TryGetProperty("origin", out var so) ? so.GetString() : null;
                    var d = firstSlice.TryGetProperty("destination", out var sd)
                        ? sd.GetString()
                        : null;
                    if (o is not null && d is not null)
                        return $"{o} → {d}";
                }
            }
        }
        catch
        {
            // If parsing fails, fall through to raw JSON
        }

        return itineraryJson.Length > 80 ? itineraryJson[..80] + "…" : itineraryJson;
    }
}
