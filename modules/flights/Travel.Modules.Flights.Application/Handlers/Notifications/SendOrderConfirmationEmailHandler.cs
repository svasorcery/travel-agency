using System.Globalization;
using System.Text.Json;
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
        IBookingNotificationReadiness readiness,
        IEmailSender sender,
        IEmailRenderer renderer,
        IUserDirectory users,
        CancellationToken ct
    )
    {
        var order = await readiness.RequireAsync(
            evt.AggregateId,
            evt.UserId,
            evt.RequiredStreamVersion,
            ct
        );
        if (order.Status is not ("Confirmed" or "Ticketed"))
            return;

        var user = await users.GetAsync(evt.UserId, ct);
        if (user is null)
            return;

        var locale = GetLocale(user.Locale);
        var model = BuildModel(order, user, locale);

        var rendered = await renderer.RenderAsync("OrderConfirmation", model, locale, ct);
        await sender.SendAsync(
            user.Email,
            rendered.Subject,
            rendered.HtmlBody,
            rendered.TextBody,
            ct
        );
    }

    private static OrderEmailModel BuildModel(OrderView order, UserProfile user, CultureInfo locale)
    {
        var bookingRef = order.AggregateId.ToString("N")[..6].ToUpperInvariant();
        var itinerary = ParseItinerarySummary(order.ItineraryJson, locale);
        var total = $"{order.TotalAmount:N2} {order.Currency}";
        var guestName = $"{user.GivenName} {user.FamilyName}".Trim();
        if (string.IsNullOrWhiteSpace(guestName))
            guestName = user.Email;

        var isEn = locale.TwoLetterISOLanguageName.Equals("en", StringComparison.OrdinalIgnoreCase);
        var ticketFollowsText = isEn
            ? "Your e-ticket will be sent to this email shortly."
            : "Ваш электронный билет будет отправлен на этот адрес в ближайшее время.";

        return new OrderEmailModel(
            GuestName: guestName,
            OrderId: order.AggregateId.ToString("N"),
            ItinerarySummary: itinerary,
            TotalFormatted: total,
            BookingRef: bookingRef,
            TicketFollowsText: ticketFollowsText
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

    internal static string ParseItinerarySummary(string itineraryJson, CultureInfo locale)
    {
        var unavailable =
            locale.TwoLetterISOLanguageName == "en"
                ? "Itinerary unavailable"
                : "Маршрут недоступен";
        try
        {
            using var doc = JsonDocument.Parse(itineraryJson);
            var root = doc.RootElement;
            if (Property(root, "slices", out var slices))
            {
                if (
                    slices.ValueKind != JsonValueKind.Array
                    || slices.GetArrayLength() is < 1 or > 4
                )
                    return unavailable;
                var legs = new List<string>();
                foreach (var slice in slices.EnumerateArray())
                {
                    var leg = Route(slice);
                    if (leg is null)
                        return unavailable;
                    legs.Add(leg);
                }
                // Each leg remains separate: an open-jaw gap is not a flight segment.
                return string.Join("; ", legs);
            }
            return Route(root) ?? unavailable;
        }
        catch (JsonException)
        {
            return unavailable;
        }
    }

    private static string? Route(JsonElement element)
    {
        if (
            !Property(element, "origin", out var origin)
            || !Property(element, "destination", out var destination)
        )
            return null;
        var from = Airport(origin);
        var to = Airport(destination);
        return from is not null && to is not null ? $"{from} → {to}" : null;
    }

    private static string? Airport(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object && Property(element, "value", out var value))
            element = value;
        if (element.ValueKind != JsonValueKind.String)
            return null;
        var code = element.GetString()?.ToUpperInvariant();
        return code is { Length: 3 } && code.All(c => c is >= 'A' and <= 'Z') ? code : null;
    }

    private static bool Property(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
            foreach (var property in element.EnumerateObject())
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
        value = default;
        return false;
    }
}
