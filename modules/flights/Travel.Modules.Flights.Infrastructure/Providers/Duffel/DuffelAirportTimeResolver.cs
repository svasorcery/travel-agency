using System.Globalization;
using System.Text.RegularExpressions;
using ErrorOr;

namespace Travel.Modules.Flights.Infrastructure.Providers.Duffel;

public static partial class DuffelAirportTimeResolver
{
    private static readonly Error InvalidTime = Error.Validation(
        "DuffelOffer.InvalidAirportTime",
        "Supplier airport time is invalid or unresolved."
    );

    [GeneratedRegex(
        @"\A[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(?:\.[0-9]{1,7})?(?<offset>Z|[+-][0-9]{2}:[0-9]{2})?\z",
        RegexOptions.CultureInvariant
    )]
    private static partial Regex IsoTimestamp();

    public static ErrorOr<DateTimeOffset> Resolve(string raw, string? ianaTimeZone)
    {
        if (raw is null || string.IsNullOrWhiteSpace(ianaTimeZone))
            return InvalidTime;
        var match = IsoTimestamp().Match(raw);
        if (!match.Success)
            return InvalidTime;
        var offsetText = match.Groups["offset"].Value;
        var wallText = offsetText.Length == 0 ? raw : raw[..^offsetText.Length];
        if (
            !DateTime.TryParseExact(
                wallText,
                ["yyyy-MM-dd'T'HH:mm:ss", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF"],
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var wallTime
            )
        )
            return InvalidTime;
        // Preserve Unspecified wall time: neither parsing nor lookup may consult the host zone.
        wallTime = DateTime.SpecifyKind(wallTime, DateTimeKind.Unspecified);
        if (
            !TimeZoneInfo.TryFindSystemTimeZoneById(ianaTimeZone, out var zone)
            || !zone.HasIanaId
            || zone.IsInvalidTime(wallTime)
        )
            return InvalidTime;
        var ambiguous = zone.IsAmbiguousTime(wallTime);
        var validOffsets = ambiguous
            ? zone.GetAmbiguousTimeOffsets(wallTime)
            : [zone.GetUtcOffset(wallTime)];
        if (offsetText.Length == 0 && ambiguous)
            return InvalidTime;
        var offset = validOffsets[0];
        if (offsetText.Length > 0)
        {
            var explicitText = offsetText == "Z" ? raw[..^1] + "+00:00" : raw;
            if (
                !DateTimeOffset.TryParseExact(
                    explicitText,
                    ["yyyy-MM-dd'T'HH:mm:sszzz", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz"],
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var explicitTime
                ) || !validOffsets.Contains(explicitTime.Offset)
            )
                return InvalidTime;
            offset = explicitTime.Offset;
        }
        try
        {
            return new DateTimeOffset(wallTime, offset);
        }
        catch (ArgumentException)
        {
            return InvalidTime;
        }
    }
}
