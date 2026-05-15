using System.Globalization;
using System.Text.RegularExpressions;
using Travel.Modules.Flights.Application.Notifications;

namespace Travel.Modules.Flights.Infrastructure.Notifications.Email;

/// <summary>
/// Renders email templates using simple token replacement on file-system HTML templates.
/// Templates live in <c>Notifications/Email/Templates/</c> relative to the Infrastructure
/// assembly's directory and are deployed with CopyToOutputDirectory=PreserveNewest.
/// Template names follow <c>{TemplateName}.{two-letter-locale}.html</c> convention
/// (e.g. OrderConfirmation.ru.html). Falls back to .en.html.
/// Tokens: {{GuestName}}, {{BookingRef}}, {{ItinerarySummary}}, {{TotalFormatted}}, {{OrderId}}.
/// Subject is resolved from a lookup table; plain text is produced by stripping HTML tags.
/// Token replacement was chosen over a Razor engine because RazorLight 2.3.1 does not work
/// on .NET 10 (dynamic-dispatch incompatibility); this renderer needs zero runtime
/// dependencies and the templates are static HTML with no logic.
/// </summary>
public sealed partial class HtmlTemplateEmailRenderer : IEmailRenderer
{
    private readonly string _templateRoot;

    private static readonly Dictionary<string, string> Subjects = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        ["OrderConfirmation.ru"] = "Бронирование подтверждено",
        ["OrderConfirmation.en"] = "Booking confirmed",
        ["OrderCancellation.ru"] = "Бронирование отменено",
        ["OrderCancellation.en"] = "Booking cancelled",
    };

    public HtmlTemplateEmailRenderer()
        : this(
            Path.Combine(
                Path.GetDirectoryName(typeof(HtmlTemplateEmailRenderer).Assembly.Location)!,
                "Notifications",
                "Email",
                "Templates"
            )
        ) { }

    internal HtmlTemplateEmailRenderer(string templateRoot)
    {
        _templateRoot = templateRoot;
    }

    public async Task<RenderedEmail> RenderAsync(
        string templateName,
        OrderEmailModel model,
        CultureInfo locale,
        CancellationToken ct
    )
    {
        var twoLetter = locale.TwoLetterISOLanguageName.ToLowerInvariant();

        var templateFile =
            ResolveTemplateFile(templateName, twoLetter)
            ?? throw new InvalidOperationException(
                $"Email template '{templateName}' not found for locale '{twoLetter}' or fallback 'en'. "
                    + $"Searched in: {_templateRoot}"
            );

        var rawHtml = await File.ReadAllTextAsync(
            Path.Combine(_templateRoot, templateFile),
            System.Text.Encoding.UTF8,
            ct
        );

        var html = ApplyTokens(rawHtml, model);

        var subjectKey = $"{templateName}.{twoLetter}";
        if (!Subjects.TryGetValue(subjectKey, out var subject))
            subject = Subjects.GetValueOrDefault($"{templateName}.en", templateName);

        var text = StripHtml(html);
        return new RenderedEmail(subject, html, text);
    }

    private static string ApplyTokens(string template, OrderEmailModel model) =>
        template
            .Replace("{{GuestName}}", System.Net.WebUtility.HtmlEncode(model.GuestName))
            .Replace("{{BookingRef}}", System.Net.WebUtility.HtmlEncode(model.BookingRef))
            .Replace(
                "{{ItinerarySummary}}",
                System.Net.WebUtility.HtmlEncode(model.ItinerarySummary)
            )
            .Replace("{{TotalFormatted}}", System.Net.WebUtility.HtmlEncode(model.TotalFormatted))
            .Replace("{{OrderId}}", System.Net.WebUtility.HtmlEncode(model.OrderId))
            .Replace(
                "{{CancelReasonText}}",
                System.Net.WebUtility.HtmlEncode(model.CancelReasonText)
            )
            .Replace("{{RefundText}}", System.Net.WebUtility.HtmlEncode(model.RefundText))
            .Replace(
                "{{TicketFollowsText}}",
                System.Net.WebUtility.HtmlEncode(model.TicketFollowsText)
            );

    private string? ResolveTemplateFile(string templateName, string locale)
    {
        var specific = $"{templateName}.{locale}.html";
        if (File.Exists(Path.Combine(_templateRoot, specific)))
            return specific;

        var fallback = $"{templateName}.en.html";
        if (File.Exists(Path.Combine(_templateRoot, fallback)))
            return fallback;

        return null;
    }

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex HtmlTagRegex();

    private static string StripHtml(string html)
    {
        var text = HtmlTagRegex().Replace(html, " ");
        text = Regex.Replace(text, @"\s{2,}", " ").Trim();
        return text;
    }
}
