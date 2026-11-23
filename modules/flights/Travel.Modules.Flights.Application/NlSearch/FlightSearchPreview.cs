using System.Globalization;
using System.Text.RegularExpressions;
using Travel.IntegrationContracts.AI.NlSearch;

namespace Travel.Modules.Flights.Application.NlSearch;

public sealed record FlightSearchPreviewLocation(string Code, string Name);

public sealed record FlightSearchPreviewDate(DateOnly Value, string Source);

public sealed record FlightSearchPreviewIssue(string Field, string Reason);

public sealed record FlightSearchPreviewDraft(
    string? TripType,
    FlightSearchPreviewLocation? Origin,
    FlightSearchPreviewLocation? Destination,
    FlightSearchPreviewDate? DepartureDate,
    FlightSearchPreviewDate? ReturnDate,
    int? PassengerCount,
    IReadOnlyList<FlightSearchPreviewIssue> Issues
);

public static partial class FlightSearchPreview
{
    private const string DepartureField = "departureDate";
    private const string ReturnField = "returnDate";
    private const string PartyField = "passengerCount";
    private const string OneWay = "oneWay";
    private const string RoundTrip = "roundTrip";
    private const string Suggested = "suggested";
    private const string RoundTripWords = @"туда(?:\s*-\s*|\s+и\s+)обратно|в обе стороны";
    private const string ReturnWords =
        @"обратно|возврат[а-яё]*|обратн[а-яё]* рейс|" + FlightSearchPreviewText.ReturnVerbWords;
    private const string CountWords =
        @"[0-9]+|один|одна|двое|два|две|трое|три|четверо|четыре|пятеро|пять|шестеро|шесть|семеро|семь|восьмеро|восемь|девятеро|девять";
    private const string PartyUnits = @"взросл[а-яё]*|пассажир[а-яё]*|человек[а-яё]*";
    private static readonly CultureInfo Russian = CultureInfo.GetCultureInfo("ru-RU");

    public static FlightSearchPreviewDraft Normalize(
        string query,
        DateOnly referenceDate,
        FlightSearchPreviewFragments fragments
    )
    {
        var text = FlightSearchPreviewText.Normalize(query);
        var issues = new List<FlightSearchPreviewIssue>();
        var locations = FlightSearchPreviewText.ReadRoute(text);
        var origin = ResolveLocation(locations.Origins, text, fragments.Origin, "origin", issues);
        var destination = ResolveLocation(
            locations.Destinations,
            text,
            fragments.Destination,
            "destination",
            issues
        );
        if (origin is not null && origin.Code == destination?.Code)
        {
            destination = null;
            issues.Add(new("destination", "invalid"));
        }

        var hasChild = FlightSearchPreviewText.HasChild(text);
        var count = hasChild ? null : ResolvePassengerCount(text, fragments.Party, issues);
        if (
            hasChild
            || FlightSearchPreviewText.HasUnsupportedRequest(text, fragments.Restrictions)
            || locations.HasOtherLeg
        )
            issues.Add(new("request", "unsupported"));

        var dates = ReadDates(text);
        var departure = ResolveDate(
            dates.Departures,
            text,
            fragments.Departure,
            referenceDate,
            DepartureField,
            issues
        );
        var oneWay = FlightSearchPreviewText.HasPositiveMatch(OneWayWords(), text);
        var returnRequested =
            dates.Returns.Count > 0
            || FlightSearchPreviewText.HasPositiveMatch(ReturnIntent(), text);
        var tripType = ResolveTripType(
            text,
            fragments.TripType,
            oneWay,
            returnRequested,
            departure?.Source == Suggested,
            issues
        );
        var returning = ResolveReturnDate(
            dates.Returns,
            text,
            fragments.Return,
            referenceDate,
            departure,
            tripType,
            oneWay,
            issues
        );

        return new(tripType, origin, destination, departure, returning, count, issues);
    }

    private static FlightSearchPreviewLocation? ResolveLocation(
        IReadOnlyList<FlightSearchPreviewText.LocationEvidence> candidates,
        string text,
        string? fragment,
        string field,
        List<FlightSearchPreviewIssue> issues
    )
    {
        if (
            candidates.Any(candidate => candidate.HasAlternative)
            || candidates.Select(candidate => candidate.Location.Code).Distinct().Skip(1).Any()
        )
        {
            issues.Add(new(field, "ambiguous"));
            return null;
        }

        var grounded = candidates.FirstOrDefault(candidate =>
            FlightSearchPreviewText.Covers(text, fragment, candidate.Name)
        );
        if (grounded is null)
            issues.Add(new(field, fragment is null ? "missing" : "invalid"));
        return grounded?.Location;
    }

    private static int? ResolvePassengerCount(
        string text,
        string? fragment,
        List<FlightSearchPreviewIssue> issues
    )
    {
        var counts = PartyCounts()
            .Matches(text)
            .Where(match => !FlightSearchPreviewText.IsNegated(text, match.Index))
            .Select(match => match.Groups["count"])
            .ToArray();
        if (
            AlternativeCounts().IsMatch(text)
            || counts.Any(count => ApproximateCount().IsMatch(text[..count.Index]))
            || counts.Select(ReadCount).Distinct().Skip(1).Any()
        )
        {
            issues.Add(new(PartyField, "ambiguous"));
            return null;
        }

        var grounded = counts.FirstOrDefault(count =>
            FlightSearchPreviewText.Covers(text, fragment, count)
        );
        if (grounded is null)
        {
            issues.Add(new(PartyField, fragment is null ? "missing" : "invalid"));
            return null;
        }

        var value = ReadCount(grounded);
        if (value is >= 1 and <= 9)
            return value;

        issues.Add(new(PartyField, "unsupported"));
        return null;
    }

    private static int ReadCount(Group count) =>
        count.Value.ToLowerInvariant() switch
        {
            "один" or "одна" => 1,
            "двое" or "два" or "две" => 2,
            "трое" or "три" => 3,
            "четверо" or "четыре" => 4,
            "пятеро" or "пять" => 5,
            "шестеро" or "шесть" => 6,
            "семеро" or "семь" => 7,
            "восьмеро" or "восемь" => 8,
            "девятеро" or "девять" => 9,
            var value => int.TryParse(
                value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var number
            )
                ? number
                : 0,
        };

    private static (List<Match> Departures, List<Match> Returns) ReadDates(string text)
    {
        List<Match> departures = [];
        List<Match> returns = [];
        var previousEnd = 0;
        var previousWasReturn = false;
        foreach (Match match in Dates().Matches(text))
        {
            var between = text[previousEnd..match.Index];
            var lastRole = DateRole().Matches(between).LastOrDefault();
            var returning =
                lastRole?.Groups["return"].Success
                ?? (previousWasReturn && FlightSearchPreviewText.StartsWithAlternative(between));
            if (
                !FlightSearchPreviewText.IsNegated(text, match.Index)
                && (
                    lastRole is null
                    || !FlightSearchPreviewText.IsNegated(text, previousEnd + lastRole.Index)
                )
            )
                (returning ? returns : departures).Add(match);
            previousEnd = match.Index + match.Length;
            previousWasReturn = returning;
        }

        return (departures, returns);
    }

    private static FlightSearchPreviewDate? ResolveDate(
        IReadOnlyList<Match> candidates,
        string text,
        string? fragment,
        DateOnly referenceDate,
        string field,
        List<FlightSearchPreviewIssue> issues
    )
    {
        if (
            candidates.Any(candidate => candidate.Groups["alternative"].Success)
            || candidates
                .Select(candidate => candidate.Value)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Skip(1)
                .Any()
        )
        {
            issues.Add(new(field, "ambiguous"));
            return null;
        }

        var grounded = candidates.FirstOrDefault(candidate =>
            FlightSearchPreviewText.Covers(text, fragment, candidate)
        );
        if (grounded is null)
        {
            issues.Add(new(field, fragment is null ? "missing" : "invalid"));
            return null;
        }

        if (grounded.Groups["weekend"].Success && field == DepartureField)
        {
            var days = (DayOfWeek.Saturday - referenceDate.DayOfWeek + 7) % 7;
            if (referenceDate.DayNumber <= DateOnly.MaxValue.DayNumber - days)
                return new(referenceDate.AddDays(days), Suggested);
        }
        else if (TryReadExplicitDate(grounded.Value, out var value) && value >= referenceDate)
        {
            return new(value, "explicit");
        }

        issues.Add(new(field, "invalid"));
        return null;
    }

    private static bool TryReadExplicitDate(string raw, out DateOnly value) =>
        DateOnly.TryParseExact(
            raw.ToLowerInvariant(),
            ["yyyy-MM-dd", "d.M.yyyy", "d MMMM yyyy", "dd MMMM yyyy"],
            Russian,
            DateTimeStyles.None,
            out value
        );

    private static string? ResolveTripType(
        string text,
        string? fragment,
        bool oneWay,
        bool returnRequested,
        bool weekend,
        List<FlightSearchPreviewIssue> issues
    )
    {
        if (oneWay && returnRequested)
        {
            issues.Add(new("tripType", "ambiguous"));
            return null;
        }

        if (
            fragment is not null
            && !OneWayWords()
                .Matches(text)
                .Concat(ReturnIntent().Matches(text))
                .Any(match =>
                    !FlightSearchPreviewText.IsNegated(text, match.Index)
                    && FlightSearchPreviewText.Covers(text, fragment, match)
                )
        )
            issues.Add(new("tripType", "invalid"));

        return oneWay ? OneWay
            : returnRequested || weekend ? RoundTrip
            : null;
    }

    private static FlightSearchPreviewDate? ResolveReturnDate(
        IReadOnlyList<Match> candidates,
        string text,
        string? fragment,
        DateOnly referenceDate,
        FlightSearchPreviewDate? departure,
        string? tripType,
        bool oneWay,
        List<FlightSearchPreviewIssue> issues
    )
    {
        if (oneWay)
            return null;

        if (candidates.Count > 0 || fragment is not null)
        {
            var returning = ResolveDate(
                candidates,
                text,
                fragment,
                referenceDate,
                ReturnField,
                issues
            );
            if (returning is not null && departure is not null && returning.Value < departure.Value)
            {
                issues.Add(new(ReturnField, "invalid"));
                return null;
            }

            return returning;
        }

        if (tripType != RoundTrip)
            return null;

        if (
            departure?.Source == Suggested
            && departure.Value < DateOnly.MaxValue
            && !FlightSearchPreviewText.HasPositiveMatch(RelativeReturnDay(), text)
        )
            return new(departure.Value.AddDays(1), Suggested);

        issues.Add(new(ReturnField, "missing"));
        return null;
    }

    [GeneratedRegex(
        @"\b(?:(?:нас|пассажиров)\s+(?<count>"
            + CountWords
            + @")|(?<count>"
            + CountWords
            + @")\s+(?:"
            + PartyUnits
            + @"))\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    )]
    private static partial Regex PartyCounts();

    [GeneratedRegex(
        @"\b(?:(?:нас|пассажиров)\s+(?:"
            + CountWords
            + @")\s*(?:или|либо|[-/])\s*(?:"
            + CountWords
            + @")|(?:"
            + CountWords
            + @")\s*(?:или|либо|[-/])\s*(?:"
            + CountWords
            + @")\s+(?:"
            + PartyUnits
            + @"))\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    )]
    private static partial Regex AlternativeCounts();

    [GeneratedRegex(
        @"\b(?:около|примерно|порядка|до|от)\s+$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    )]
    private static partial Regex ApproximateCount();

    [GeneratedRegex(
        @"\b(?:(?<weekend>на выходные)|[0-9]{4}-[0-9]{2}-[0-9]{2}|[0-9]{1,2}\.[0-9]{1,2}\.[0-9]{4}|(?<alternative>[0-9]{1,2}\s*(?:или|либо|[-/])\s*)?[0-9]{1,2}\s+(?:января|февраля|марта|апреля|мая|июня|июля|августа|сентября|октября|ноября|декабря)(?:\s+[0-9]{4})?)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    )]
    private static partial Regex Dates();

    [GeneratedRegex(
        @"\b(?:" + RoundTripWords + @"|(?<return>" + ReturnWords + @")|вылет[а-яё]*|туда)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    )]
    private static partial Regex DateRole();

    [GeneratedRegex(
        @"\b(?:" + RoundTripWords + "|" + ReturnWords + @")\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    )]
    private static partial Regex ReturnIntent();

    [GeneratedRegex(
        @"\b(?:"
            + ReturnWords
            + @")\b[^,;.!?]*\b(?:"
            + FlightSearchPreviewText.RelativeDayWords
            + @")\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    )]
    private static partial Regex RelativeReturnDay();

    [GeneratedRegex(
        @"\b(?:только туда|без обратн[а-яё]* рейс[а-яё]*|в одну сторону)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    )]
    private static partial Regex OneWayWords();
}
