using System.Text.RegularExpressions;

namespace Travel.Modules.Flights.Infrastructure.Providers.Travelpayouts;

/// <summary>
/// Removes sensitive query parameters (such as API tokens) from URLs before they are
/// recorded in OpenTelemetry spans or log output.
/// </summary>
public static partial class UrlRedactor
{
    // Matches ?token=ANYTHING or &token=ANYTHING (value ends at & or end-of-string)
    [GeneratedRegex(@"(?<=[\?&])token=[^&]*", RegexOptions.IgnoreCase)]
    private static partial Regex TokenParamRegex();

    /// <summary>
    /// Returns a copy of <paramref name="url"/> where every <c>token=&lt;value&gt;</c>
    /// query parameter is replaced with <c>token=REDACTED</c>.
    /// </summary>
    public static string Redact(string url) => TokenParamRegex().Replace(url, "token=REDACTED");
}
