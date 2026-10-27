using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Travel.IntegrationContracts.AI.TripsBriefExtraction;

namespace Travel.Modules.Trips.Application.BriefExtraction;

public static class TripBriefLexicon
{
    public const string Version = "trips-brief-lexicon-v1";
    public static readonly string[] InterestOrder =
    [
        "architecture",
        "culture",
        "food",
        "nature",
        "history",
    ];
    internal static readonly (string Value, string Aliases)[] Cities =
    [
        ("moscow", "moscow|москва|москвы|москву|москве"),
        (
            "saint-petersburg",
            "saint-petersburg|санкт-петербург|санкт-петербурга|санкт-петербурге|петербург|петербурга|петербурге|питер|питера|питере|спб"
        ),
        ("kaliningrad", "kaliningrad|калининград|калининграда|калининграде"),
    ];
    internal static readonly (string Value, string Aliases)[] Interests =
    [
        ("architecture", "архитектура|архитектуру|архитектуры|архитектурой"),
        ("culture", "культура|культуру|культуры|культурой"),
        ("food", "еда|еду|еды|едой"),
        ("nature", "природа|природу|природы|природой"),
        ("history", "история|историю|истории|историей"),
    ];
    internal static readonly (string Value, string Aliases)[] Paces =
    [
        ("relaxed", "спокойный|спокойном|спокойно|неторопливый"),
        ("balanced", "умеренный|умеренном|сбалансированный"),
        ("active", "активный|активном|активно"),
    ];
    internal const string NumberPattern =
        "[0-9]+|один|одна|одного|два|две|двое|три|трое|четыре|четверо|пять|пятеро|шесть|шестеро|семь|семеро|восемь|восьмеро|девять|девятеро|десять|одиннадцать|двенадцать|тринадцать|четырнадцать|пятнадцать";

    internal static int Number(string text) =>
        text.ToLowerInvariant() switch
        {
            "один" or "одна" or "одного" => 1,
            "два" or "две" or "двое" => 2,
            "три" or "трое" => 3,
            "четыре" or "четверо" => 4,
            "пять" or "пятеро" => 5,
            "шесть" or "шестеро" => 6,
            "семь" or "семеро" => 7,
            "восемь" or "восьмеро" => 8,
            "девять" or "девятеро" => 9,
            "десять" => 10,
            "одиннадцать" => 11,
            "двенадцать" => 12,
            "тринадцать" => 13,
            "четырнадцать" => 14,
            "пятнадцать" => 15,
            _ => int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var count)
                ? count
                : int.MaxValue,
        };

    internal static Regex Regex(string pattern) =>
        new(
            pattern,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(100)
        );

    internal static Regex Words(string aliases) =>
        Regex(@"(?<![\p{L}\p{M}\p{N}_-])(?:" + aliases + @")(?![\p{L}\p{M}\p{N}_-])");

    internal static bool IsNegative(string text, int at)
    {
        if (InterestListNegation(text, at) is { } negative)
            return negative;
        var prefix = text[..at];
        var boundary = prefix.LastIndexOfAny([',', '.', ';', '\n']);
        return Regex(
                @"(?:^|\s)(?:не\s+(?:интересует|интересуют|хочу|нужна|нужны)?|без)\s*(?:\p{L}+\s+){0,2}$"
            )
            .IsMatch(prefix[(boundary + 1)..]);
    }

    private static bool? InterestListNegation(string text, int at)
    {
        var current = Regex(@"^[\p{L}\p{M}-]+").Match(text[at..]);
        if (!current.Success || !IsInterestAlias(current.Value))
            return null;
        var prefix = text[..at];
        // Commas/conjunctions continue a negative list. An unrecognised list item
        // cannot restore positive extraction: keep it blocked until a finite,
        // explicit positive predicate or a hard clause boundary resets the scope.
        var boundary = prefix.LastIndexOfAny(['.', ';', '!', '?', '\n']);
        prefix = prefix[(boundary + 1)..];
        var polarity = Regex(
                @"(?<![\p{L}\p{M}])(?:(?<negative>не(?:\s+(?:интересует|интересуют|хочу|нужна|нужны))?\s+|без\s+)|(?<positive>(?:интересует|интересуют|хочу|нужна|нужны)\s+))"
            )
            .Matches(prefix)
            .Cast<Match>()
            .LastOrDefault();
        return polarity is null ? null : polarity.Groups["negative"].Success;
    }

    private static bool IsInterestAlias(string text)
    {
        var normalized = NormalizeAlias(text);
        return Interests.Any(interest =>
            interest.Aliases.Split('|').Contains(normalized, StringComparer.OrdinalIgnoreCase)
        );
    }

    internal static bool IsOrigin(string text, int at) =>
        Regex(@"(?:^|\s)из\s*$").IsMatch(text[..at]);

    public static string NormalizeAlias(string text) =>
        Regex(@"\s+")
            .Replace(text.Normalize(NormalizationForm.FormC).Trim(), " ")
            .ToLowerInvariant();

    internal static IEnumerable<(string Value, TripsBriefSpan Span)> FindAliases(
        string text,
        (string Value, string Aliases)[] aliases
    )
    {
        foreach (Match token in Regex(@"[\p{L}\p{M}\p{N}_-]+").Matches(text))
        {
            var normalized = NormalizeAlias(token.Value);
            foreach (var item in aliases)
                if (
                    item.Aliases.Split('|').Contains(normalized, StringComparer.OrdinalIgnoreCase)
                    && !(normalized.All(c => c <= 127) && token.Value.Any(c => c > 127))
                )
                    yield return (item.Value, new(token.Index, token.Index + token.Length));
        }
    }
}
