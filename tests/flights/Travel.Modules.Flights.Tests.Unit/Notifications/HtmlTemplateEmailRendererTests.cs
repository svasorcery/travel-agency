using System.Globalization;
using Shouldly;
using Travel.Modules.Flights.Application.Notifications;
using Travel.Modules.Flights.Infrastructure.Notifications.Email;
using Xunit;

namespace Travel.Modules.Flights.Tests.Unit.Notifications;

/// <summary>
/// Tests for <see cref="HtmlTemplateEmailRenderer"/> behaviour that does not require a live
/// database: locale fallback, token replacement, and XSS escaping.
/// Uses a temp directory with minimal templates to avoid any I/O coupling to the real template set.
/// </summary>
public sealed class HtmlTemplateEmailRendererTests : IDisposable
{
    private readonly string _templateDir = Path.Combine(
        Path.GetTempPath(),
        $"rendererTests_{Guid.NewGuid():N}"
    );

    public HtmlTemplateEmailRendererTests()
    {
        Directory.CreateDirectory(_templateDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_templateDir))
            Directory.Delete(_templateDir, true);
    }

    private OrderEmailModel SampleModel(string guestName = "Alice") =>
        new(
            GuestName: guestName,
            OrderId: Guid.NewGuid().ToString("N"),
            ItinerarySummary: "SVO → LED",
            TotalFormatted: "10 000 RUB",
            BookingRef: "ABC123"
        );

    private void WriteTemplate(string name, string content) =>
        File.WriteAllText(Path.Combine(_templateDir, name), content);

    // ─── locale fallback ────────────────────────────────────────────────────────

    [Fact]
    public async Task Unknown_locale_falls_back_to_ru_template()
    {
        WriteTemplate("Order.ru.html", "<p>Привет {{GuestName}}</p>");
        var renderer = new HtmlTemplateEmailRenderer(_templateDir);

        var result = await renderer.RenderAsync(
            "Order",
            SampleModel(),
            CultureInfo.GetCultureInfo("ja"), // Japanese — not configured
            CancellationToken.None
        );

        result.HtmlBody.ShouldContain("Привет Alice");
    }

    [Fact]
    public async Task Configured_locale_uses_exact_template()
    {
        WriteTemplate("Order.ru.html", "<p>RU {{GuestName}}</p>");
        WriteTemplate("Order.en.html", "<p>EN {{GuestName}}</p>");
        var renderer = new HtmlTemplateEmailRenderer(_templateDir);

        var ru = await renderer.RenderAsync(
            "Order",
            SampleModel("Иван"),
            CultureInfo.GetCultureInfo("ru"),
            CancellationToken.None
        );
        var en = await renderer.RenderAsync(
            "Order",
            SampleModel("John"),
            CultureInfo.GetCultureInfo("en"),
            CancellationToken.None
        );

        ru.HtmlBody.ShouldContain("RU Иван");
        en.HtmlBody.ShouldContain("EN John");
    }

    // ─── XSS escaping ───────────────────────────────────────────────────────────

    [Fact]
    public async Task GuestName_with_html_characters_is_escaped()
    {
        WriteTemplate("Order.ru.html", "<p>{{GuestName}}</p>");
        var renderer = new HtmlTemplateEmailRenderer(_templateDir);
        var model = SampleModel("<script>alert(1)</script>");

        var result = await renderer.RenderAsync(
            "Order",
            model,
            CultureInfo.GetCultureInfo("ru"),
            CancellationToken.None
        );

        result.HtmlBody.ShouldContain("&lt;script&gt;");
        result.HtmlBody.ShouldNotContain("<script>");
    }
}
