using System.Globalization;

namespace Travel.Modules.Flights.Application.Notifications;

public sealed record OrderEmailModel(
    string GuestName,
    string OrderId,
    string ItinerarySummary,
    string TotalFormatted,
    string BookingRef
);

public sealed record RenderedEmail(string Subject, string HtmlBody, string TextBody);

public interface IEmailRenderer
{
    Task<RenderedEmail> RenderAsync(
        string templateName,
        OrderEmailModel model,
        CultureInfo locale,
        CancellationToken ct
    );
}
