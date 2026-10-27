using System.Globalization;
using System.Text.RegularExpressions;
using Travel.IntegrationContracts.AI.TripsBriefExtraction;
using Travel.Modules.Trips.Core;

namespace Travel.Modules.Trips.Application.BriefExtraction;

public sealed record TripBriefDateResult(
    TripsBriefField StartDate,
    TripsBriefField EndDate,
    TripsBriefField DurationDays,
    TripsBriefIssue[] Issues,
    bool HasRelativeWords
);

public static class TripBriefDateNormalizer
{
    public const string Version = "trips-brief-dates-v1";
    private static readonly string[] Months =
    [
        "января",
        "февраля",
        "марта",
        "апреля",
        "мая",
        "июня",
        "июля",
        "августа",
        "сентября",
        "октября",
        "ноября",
        "декабря",
    ];
    private static readonly string[] Weekdays =
    [
        "воскресенье|воскресенья",
        "понедельник|понедельника",
        "вторник|вторника",
        "среда|среду|среды",
        "четверг|четверга",
        "пятница|пятницу|пятницы",
        "суббота|субботу|субботы",
    ];

    public static DateOnly? LocalDate(DateTimeOffset instant, string? zone) =>
        zone is "Europe/Moscow" or "Europe/Kaliningrad"
            ? DateOnly.FromDateTime(
                TimeZoneInfo
                    .ConvertTime(instant, TimeZoneInfo.FindSystemTimeZoneById(zone))
                    .DateTime
            )
            : null;

    public static DateTimeOffset ExpiresAt(DateTimeOffset instant, string? zone, bool relative)
    {
        var expiry = instant.AddMinutes(10);
        if (!relative || LocalDate(instant, zone) is not { } local)
            return expiry;
        var midnight = new DateTimeOffset(
            TimeZoneInfo.ConvertTimeToUtc(
                local.AddDays(1).ToDateTime(TimeOnly.MinValue),
                TimeZoneInfo.FindSystemTimeZoneById(zone!)
            ),
            TimeSpan.Zero
        );
        return midnight < expiry ? midnight : expiry;
    }

    public static TripBriefDateResult Normalize(
        string text,
        DateTimeOffset referenceInstant,
        string? referenceZone
    )
    {
        var start = BriefFields.Missing();
        var end = BriefFields.Missing();
        var duration = BriefFields.Missing();
        var issues = new List<TripsBriefIssue>();
        var dates = new List<(TripsBriefSpan Span, DateOnly? Date, string? Problem, bool End)>();
        var relative = false;
        var datePattern =
            @"(?<![\p{L}\p{N}])(?:[0-9]{4}-[0-9]{2}-[0-9]{2}|[0-9]{2}\.[0-9]{2}\.[0-9]{4}|[0-9]{1,2}\s+(?:"
            + string.Join('|', Months)
            + @")(?:\s+[0-9]{4}(?:\s+года)?)?|[0-9]{1,2}/[0-9]{1,2}/[0-9]{4}|послезавтра|завтра|сегодня)(?![\p{L}\p{N}])";
        foreach (Match match in TripBriefLexicon.Regex(datePattern).Matches(text))
        {
            if (TripBriefLexicon.IsNegative(text, match.Index))
                continue;
            var value = match.Value.ToLowerInvariant();
            var span = new TripsBriefSpan(match.Index, match.Index + match.Length);
            var isEnd = TripBriefLexicon
                .Regex(
                    @"(?:^|\s)(?:по|до)\s+(?:(?:(?:в|во)\s+)?(?:"
                        + string.Join("|", Weekdays)
                        + @")\s+)?$"
                )
                .IsMatch(text[..match.Index]);
            DateOnly? date = null;
            string? problem = null;
            if (value is "сегодня" or "завтра" or "послезавтра")
            {
                relative = true;
                var local = LocalDate(referenceInstant, referenceZone);
                if (local is null)
                    problem = "referenceZoneMissing";
                else
                {
                    try
                    {
                        date = local.Value.AddDays(
                            value == "сегодня" ? 0
                            : value == "завтра" ? 1
                            : 2
                        );
                    }
                    catch (ArgumentOutOfRangeException)
                    {
                        problem = "invalidDate";
                    }
                }
            }
            else if (value.Contains('/'))
                problem = "dateFormatAmbiguous";
            else if (TripBriefLexicon.Regex(@"^[0-9]{1,2}\s+\p{L}+").IsMatch(value))
            {
                var parts = value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 3)
                    problem = "yearMissing";
                else
                {
                    try
                    {
                        date = new DateOnly(
                            int.Parse(parts[2], CultureInfo.InvariantCulture),
                            Array.IndexOf(Months, parts[1]) + 1,
                            int.Parse(parts[0], CultureInfo.InvariantCulture)
                        );
                    }
                    catch (ArgumentOutOfRangeException)
                    {
                        problem = "invalidDate";
                    }
                }
            }
            else if (
                DateOnly.TryParseExact(
                    value,
                    ["yyyy-MM-dd", "dd.MM.yyyy"],
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var parsed
                )
            )
                date = parsed;
            else
                problem = "invalidDate";
            dates.Add((span, date, problem, isEnd));
        }
        var durations = TripBriefLexicon
            .Regex(
                @"(?<!\p{L})на\s+(?<n>"
                    + TripBriefLexicon.NumberPattern
                    + @")\s+(?:день|дня|дней)(?!\p{L})"
            )
            .Matches(text)
            .Cast<Match>()
            .Where(match => !TripBriefLexicon.IsNegative(text, match.Index))
            .ToArray();
        if (durations.Length > 0)
        {
            var counts = durations
                .Select(x => TripBriefLexicon.Number(x.Groups["n"].Value))
                .Distinct()
                .ToArray();
            var evidence = durations
                .Select(x => new TripsBriefSpan(x.Index, x.Index + x.Length))
                .ToArray();
            if (counts.Any(x => x is < 1 or > 14))
            {
                duration = BriefFields.State("invalid", evidence);
                issues.Add(BriefFields.Issue("durationBounds", ["durationDays"], evidence));
            }
            else if (counts.Length > 1)
            {
                duration = BriefFields.State("ambiguous", evidence);
                issues.Add(BriefFields.Issue("dateDurationConflict", ["durationDays"], evidence));
            }
            else
                duration = BriefFields.Extract(counts[0], evidence);
        }
        foreach (var item in dates)
        {
            var field = item.Date is { } date
                ? BriefFields.Extract(
                    date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    item.Span
                )
                : BriefFields.State(
                    item.Problem == "invalidDate" ? "invalid" : "ambiguous",
                    [item.Span]
                );
            if (item.End && end.State == "missing")
                end = field;
            else if (!item.End && start.State == "missing")
                start = field;
            else
            {
                start = BriefFields.State("ambiguous", dates.Select(x => x.Span));
                end = start;
                issues.Add(
                    BriefFields.Issue(
                        "dateConflict",
                        ["startDate", "endDate"],
                        dates.Select(x => x.Span).ToArray()
                    )
                );
            }
            if (item.Problem is { } problem)
                issues.Add(
                    BriefFields.Issue(problem, [item.End ? "endDate" : "startDate"], item.Span)
                );
        }
        var group = dates
            .Select(x => x.Span)
            .Concat(duration.Evidence)
            .Distinct()
            .Take(4)
            .ToArray();
        if (
            dates.Any(x =>
                x.Problem is "yearMissing" or "dateFormatAmbiguous" or "referenceZoneMissing"
            )
        )
        {
            start = BriefFields.State("ambiguous", group);
            end = BriefFields.State("ambiguous", group);
        }
        var weekdayConflicts = new List<TripsBriefSpan>();
        foreach (var dated in dates.Where(date => date.Date is not null))
        {
            foreach (var (aliases, index) in Weekdays.Select((aliases, index) => (aliases, index)))
            {
                if (index == (int)dated.Date!.Value.DayOfWeek)
                    continue;
                var before = TripBriefLexicon
                    .Regex(
                        @"(?<![\p{L}\p{M}\p{N}_-])(?<weekday>"
                            + aliases
                            + @")(?![\p{L}\p{M}\p{N}_-])\s*\)?\s*,?\s*$"
                    )
                    .Match(text[..dated.Span.Start]);
                var after = TripBriefLexicon
                    .Regex(
                        @"^\s*(?:,\s*)?(?:\(\s*)?(?:(?:в|во)\s+)?(?<weekday>"
                            + aliases
                            + @")(?![\p{L}\p{M}\p{N}_-])"
                    )
                    .Match(text[dated.Span.End..]);
                if (before.Success)
                {
                    var weekday = before.Groups["weekday"];
                    weekdayConflicts.Add(new(weekday.Index, weekday.Index + weekday.Length));
                }
                if (after.Success)
                {
                    var weekday = after.Groups["weekday"];
                    weekdayConflicts.Add(
                        new(
                            dated.Span.End + weekday.Index,
                            dated.Span.End + weekday.Index + weekday.Length
                        )
                    );
                }
            }
        }
        if (weekdayConflicts.Count > 0)
        {
            var evidence = weekdayConflicts.Concat(group).Distinct().Take(4).ToArray();
            start = BriefFields.State("ambiguous", evidence);
            end = BriefFields.State("ambiguous", evidence);
            issues.Add(
                BriefFields.Issue("weekdayDateConflict", ["startDate", "endDate"], evidence)
            );
        }
        if (start.State == "extracted" && end.State == "extracted")
        {
            var s = DateOnly.ParseExact(
                start.Value!.Value.GetString()!,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture
            );
            var e = DateOnly.ParseExact(
                end.Value!.Value.GetString()!,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture
            );
            if (e < s)
            {
                start = BriefFields.State("invalid", group);
                end = BriefFields.State("invalid", group);
                issues.Add(BriefFields.Issue("reversedDates", ["startDate", "endDate"], group));
            }
            else if (
                duration.State == "extracted"
                && duration.Value!.Value.GetInt32() != e.DayNumber - s.DayNumber + 1
            )
            {
                start = BriefFields.State("ambiguous", group);
                end = BriefFields.State("ambiguous", group);
                duration = BriefFields.State("ambiguous", group);
                issues.Add(
                    BriefFields.Issue(
                        "dateDurationConflict",
                        ["startDate", "endDate", "durationDays"],
                        group
                    )
                );
            }
            else if (TripDateRange.Create(s, e).IsError)
            {
                start = BriefFields.State("invalid", group);
                end = BriefFields.State("invalid", group);
                issues.Add(BriefFields.Issue("dateBounds", ["startDate", "endDate"], group));
            }
        }
        else if (
            start.State == "extracted"
            && end.State == "missing"
            && duration.State == "extracted"
        )
        {
            try
            {
                var s = DateOnly.ParseExact(
                    start.Value!.Value.GetString()!,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture
                );
                end = BriefFields.Extract(
                    s.AddDays(duration.Value!.Value.GetInt32() - 1)
                        .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    group
                );
            }
            catch (ArgumentOutOfRangeException)
            {
                start = BriefFields.State("invalid", group);
                end = BriefFields.State("invalid", group);
                issues.Add(BriefFields.Issue("invalidDate", ["startDate", "endDate"], group));
            }
        }
        var unsupported = TripBriefLexicon
            .Regex(
                @"следующие\s+выходные|выходные|следующ(?:ую|ая|ий)\s+\p{L}+|праздники|летом|зимой|весной|осенью|на\s+(?:"
                    + TripBriefLexicon.NumberPattern
                    + @")\s+(?:ночи|ночей|ночь|рабочих\s+дней)"
            )
            .Match(text);
        if (unsupported.Success)
        {
            var span = new TripsBriefSpan(
                unsupported.Index,
                unsupported.Index + unsupported.Length
            );
            if (unsupported.Value.Contains("ноч", StringComparison.OrdinalIgnoreCase))
                duration = BriefFields.State("unsupported", [span]);
            else
            {
                start = BriefFields.State("unsupported", [span]);
                end = start;
            }
            issues.Add(
                BriefFields.Issue(
                    "unsupportedDateExpression",
                    unsupported.Value.Contains("ноч", StringComparison.OrdinalIgnoreCase)
                        ? ["durationDays"]
                        : ["startDate", "endDate"],
                    span
                )
            );
        }
        return new(start, end, duration, issues.DistinctBy(x => x.Code).ToArray(), relative);
    }
}
