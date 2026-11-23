using System.Text.Json;
using System.Text.RegularExpressions;

namespace Travel.Modules.Flights.Application.NlSearch;

internal static partial class FlightSearchPreviewText
{
    internal const string RelativeDayWords =
        @"понедельник[а-яё]*|вторник[а-яё]*|сред[ауые]|четверг[а-яё]*|пятниц[а-яё]*|суббот[а-яё]*|воскресень[а-яё]*|сегодня|завтра|послезавтра|через";
    internal const string ReturnVerbWords = @"возвращ[а-яё]*|вернуться";

    private const RegexOptions IgnoreCase = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
    private static readonly IReadOnlyDictionary<string, FlightSearchPreviewLocation> Locations =
        LoadLocations();
    private static readonly Regex LocationRole = new(
        @"\b(?<role>из|от|в|до)\s+(?<negated>не\s+)?(?<name>"
            + string.Join(
                '|',
                Locations.Keys.OrderByDescending(alias => alias.Length).Select(Regex.Escape)
            )
            + @"|(?-i:[A-Z]{3}))(?![\p{L}\p{N}_-])",
        IgnoreCase
    );

    internal sealed record LocationEvidence(
        FlightSearchPreviewLocation Location,
        Group Name,
        bool HasAlternative
    );

    internal static string Normalize(string text) =>
        Whitespace()
            .Replace(
                text.Replace('‐', '-').Replace('‑', '-').Replace('–', '-').Replace('—', '-'),
                " "
            )
            .Trim();

    internal static (
        IReadOnlyList<LocationEvidence> Origins,
        IReadOnlyList<LocationEvidence> Destinations,
        bool HasOtherLeg
    ) ReadRoute(string text)
    {
        List<LocationEvidence> origins = [];
        List<LocationEvidence> destinations = [];
        List<LocationEvidence> returnOrigins = [];
        List<LocationEvidence> returnDestinations = [];

        var returnDirections = ReturnRoute()
            .Matches(text)
            .Where(direction =>
                direction.Groups["return"].Success && !IsNegated(text, direction.Index)
            )
            .ToArray();
        foreach (Match match in LocationRole.Matches(text))
        {
            if (match.Groups["negated"].Success || IsNegated(text, match.Index))
                continue;

            var name = match.Groups["name"];
            var code = name.Value.ToUpperInvariant();
            var location = Locations.GetValueOrDefault(name.Value) ?? new(code, code);
            var candidate = new LocationEvidence(
                location,
                name,
                AlternativePlace().IsMatch(text[(match.Index + match.Length)..])
            );
            var isOrigin =
                match.Groups["role"].Value.Equals("из", StringComparison.OrdinalIgnoreCase)
                || match.Groups["role"].Value.Equals("от", StringComparison.OrdinalIgnoreCase);
            var isReturn = returnDirections.Any(direction => direction.Index < match.Index);
            var list = isReturn
                ? (isOrigin ? returnOrigins : returnDestinations)
                : (isOrigin ? origins : destinations);
            list.Add(candidate);
        }

        var hasOtherLeg =
            DoesNotReverse(returnOrigins, destinations)
            || DoesNotReverse(returnDestinations, origins)
            || (
                returnOrigins.Count == 0
                && returnDirections.Any(direction =>
                    ExplicitReturnOrigin().IsMatch(text[(direction.Index + direction.Length)..])
                )
            )
            || (
                returnDestinations.Count == 0
                && returnDirections.Any(direction =>
                    ExplicitReturnDestination()
                        .IsMatch(text[(direction.Index + direction.Length)..])
                )
            );
        return (origins, destinations, hasOtherLeg);
    }

    private static bool DoesNotReverse(
        IReadOnlyList<LocationEvidence> inbound,
        IReadOnlyList<LocationEvidence> expected
    ) =>
        inbound.Any(evidence =>
            evidence.HasAlternative
            || expected.Count == 0
            || expected.Any(outbound => outbound.Location.Code != evidence.Location.Code)
        );

    internal static bool HasChild(string text) =>
        Child()
            .Matches(text)
            .Any(match =>
                !IsNegated(text, match.Index)
                && !NoChildrenFollowing().IsMatch(text[(match.Index + match.Length)..])
            );

    internal static bool HasUnsupportedRequest(string text, string[] restrictions) =>
        HasPositiveMatch(UnsupportedCondition(), text)
        || StandaloneBusinessClass().IsMatch(text)
        || AdditionalRestriction()
            .Matches(text)
            .Any(match =>
                !IsNegated(text, match.Index)
                && restrictions.Any(fragment => Covers(text, fragment, match))
            );

    internal static bool StartsWithAlternative(string text) => Alternative().IsMatch(text);

    internal static bool HasPositiveMatch(Regex pattern, string text) =>
        pattern.Matches(text).Any(match => !IsNegated(text, match.Index));

    internal static bool IsNegated(string text, int index) => Negation().IsMatch(text[..index]);

    internal static bool Covers(string text, string? fragment, Group evidence)
    {
        if (string.IsNullOrWhiteSpace(fragment))
            return false;

        var literal = Normalize(fragment);
        var start = text.IndexOf(literal, StringComparison.OrdinalIgnoreCase);
        while (start >= 0 && start <= evidence.Index)
        {
            var end = start + literal.Length;
            if (
                end >= evidence.Index + evidence.Length
                && (
                    start == 0
                    || !char.IsLetterOrDigit(text[start - 1])
                    || !char.IsLetterOrDigit(literal[0])
                )
                && (
                    end == text.Length
                    || !char.IsLetterOrDigit(text[end])
                    || !char.IsLetterOrDigit(literal[^1])
                )
            )
                return true;

            start = text.IndexOf(literal, start + 1, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static IReadOnlyDictionary<string, FlightSearchPreviewLocation> LoadLocations()
    {
        using var json =
            typeof(FlightSearchPreviewText).Assembly.GetManifestResourceStream(
                "flights-preview-locations.json"
            )
            ?? throw new InvalidOperationException("The flight preview location list is missing.");
        var entries =
            JsonSerializer.Deserialize<LocationEntry[]>(json, JsonSerializerOptions.Web)
            ?? throw new InvalidOperationException("The flight preview location list is null.");
        var locations = new Dictionary<string, FlightSearchPreviewLocation>(
            StringComparer.OrdinalIgnoreCase
        );
        foreach (var entry in entries)
        {
            var location = new FlightSearchPreviewLocation(entry.Code, entry.Name);
            locations.Add(entry.Code, location);
            foreach (var alias in entry.Aliases)
                locations.Add(alias, location);
        }

        return locations;
    }

    private sealed record LocationEntry(string Code, string Name, string[] Aliases);

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"\b(?:не|без|кроме)\s+$", IgnoreCase)]
    private static partial Regex Negation();

    [GeneratedRegex(@"^\s*,?\s*(?:или\b|либо\b|/)", IgnoreCase)]
    private static partial Regex Alternative();

    [GeneratedRegex(
        @"^\s*,?\s*(?:или\b|либо\b|/)\s*(?:(?:из|от|в|до)\s+)?(?!(?:не|обратно|туда|на)\b)\p{L}",
        IgnoreCase
    )]
    private static partial Regex AlternativePlace();

    [GeneratedRegex(
        @"\b(?:туда(?:\s*-\s*|\s+и\s+)обратно|(?<return>обратно|" + ReturnVerbWords + @"))\b",
        IgnoreCase
    )]
    private static partial Regex ReturnRoute();

    [GeneratedRegex(@"^\s+(?:летим\s+)?(?:из|от)\b", IgnoreCase)]
    private static partial Regex ExplicitReturnOrigin();

    [GeneratedRegex(
        @"^\s+(?:летим\s+)?(?:(?:из|от)\s+[^,;.!?]+?\s+)?(?:в|до)\s+(?!(?:"
            + RelativeDayWords
            + @"|полдень|полночь|утра|вечера)\b)\p{L}",
        IgnoreCase
    )]
    private static partial Regex ExplicitReturnDestination();

    [GeneratedRegex(
        @"\b(?:реб[её]н[а-яё]*|детьми|детей|дети|младен[а-яё]*|инфант[а-яё]*|подрост[а-яё]*|несовершеннолет[а-яё]*)\b",
        IgnoreCase
    )]
    private static partial Regex Child();

    [GeneratedRegex(@"^\s*(?:нет|не будет)\b", IgnoreCase)]
    private static partial Regex NoChildrenFollowing();

    [GeneratedRegex(
        @"\b(?:бизнес[- ]класс[а-яё]*|бизнесом|в бизнесе|перв[а-яё]*\s+класс[а-яё]*|премиум[- ]?(?:эконом|класс)[а-яё]*|business class|first class|premium economy|доллар[а-яё]*|евро|юан[а-яё]*|фунт[а-яё]*|USD|EUR|GBP|CNY|AED|KZT|составн[а-яё]*\s+маршрут[а-яё]*|сложн[а-яё]*\s+маршрут[а-яё]*|мульти[- ]?сити|(?:затем|потом|далее)\s+(?:летим\s+)?(?:из|в|до))\b",
        IgnoreCase
    )]
    private static partial Regex UnsupportedCondition();

    [GeneratedRegex(@"(?:^|[,;])\s*бизнес\s*(?=[,;.!?]|$)", IgnoreCase)]
    private static partial Regex StandaloneBusinessClass();

    [GeneratedRegex(
        @"\b(?:только\s+(?:прям[а-яё]*|утренн[а-яё]*|вечерн[а-яё]*)\s+рейс[а-яё]*|не дороже|без пересадок)\b",
        IgnoreCase
    )]
    private static partial Regex AdditionalRestriction();
}
