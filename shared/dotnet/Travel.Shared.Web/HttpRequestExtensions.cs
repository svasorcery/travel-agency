using Microsoft.AspNetCore.Http;

namespace Travel.Shared.Web;

/// <summary>
/// Extension methods for <see cref="HttpRequest"/>.
/// </summary>
public static class HttpRequestExtensions
{
    /// <summary>
    /// Resolves the best-match locale from the <c>Accept-Language</c> header.
    /// Iterates quality-ordered language tags and returns the first primary subtag that
    /// appears in <paramref name="supportedLocales"/>.
    /// Falls back to <paramref name="defaultLocale"/> when no header is present or no
    /// supported locale matches.
    /// </summary>
    /// <param name="request">The incoming HTTP request.</param>
    /// <param name="supportedLocales">
    /// The locales the caller accepts, e.g. <c>["ru", "en"]</c>.
    /// </param>
    /// <param name="defaultLocale">Locale to return when nothing matches. Defaults to <c>"ru"</c>.</param>
    /// <returns>A two-letter IETF primary language subtag, lower-cased.</returns>
    public static string ResolveLocale(
        this HttpRequest request,
        IEnumerable<string> supportedLocales,
        string defaultLocale = "ru"
    )
    {
        var supported =
            supportedLocales as IReadOnlySet<string>
            ?? new HashSet<string>(supportedLocales, StringComparer.OrdinalIgnoreCase);

        foreach (var value in request.Headers.AcceptLanguage.ToString().Split(','))
        {
            var tag = value.Trim().Split(';')[0].Trim().ToLowerInvariant();
            var primary = tag.Split('-')[0];
            if (supported.Contains(primary))
                return primary;
        }

        return defaultLocale;
    }
}
