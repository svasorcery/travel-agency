using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using ErrorOr;
using Travel.IntegrationContracts.AI.TripsBriefExtraction;
using Travel.Modules.Trips.Core;

namespace Travel.Modules.Trips.Application.BriefExtraction;

public static class TripBriefCandidateValidator
{
    public const string Version = "trips-brief-validator-v1";
    public static readonly string[] SlotNames =
    [
        "destinationId",
        "startDate",
        "endDate",
        "durationDays",
        "adults",
        "interests",
        "pace",
    ];
    public static readonly string[] IssueCodes =
    [
        "partyComposition",
        "adultBounds",
        "adultConflict",
        "destinationChoice",
        "multipleDestinations",
        "cityUnresolved",
        "cityOutsideCatalogue",
        "paceConflict",
        "durationBounds",
        "yearMissing",
        "reversedDates",
        "dateDurationConflict",
        "invalidDate",
        "dateConflict",
        "dateBounds",
        "referenceZoneMissing",
        "dateFormatAmbiguous",
        "weekdayDateConflict",
        "unsupportedDateExpression",
        "unsupportedOrigin",
        "unsupportedMoney",
        "unsupportedChildren",
        "unsupportedAction",
        "unsupportedBooking",
    ];

    public static ErrorOr<TripBriefCandidate> Validate(
        TripBriefExtractionInput input,
        TripsBriefParsed reply
    )
    {
        if (!ValidInput(input))
            return TripBriefErrors.InvalidInput;
        if (
            reply is null
            || reply.CorrelationId != input.RequestId
            || reply.ProcessingPolicyVersion != input.ProcessingPolicyVersion
            || reply.Status != "Ready"
            || reply.Fields is null
            || reply.Issues is null
            || reply.Issues.Length > 16
        )
            return TripBriefErrors.InvalidOutput;
        try
        {
            var proposed = Fields(reply.Fields);
            if (proposed.Where((field, index) => !ValidField(input.Text, index, field)).Any())
                return TripBriefErrors.InvalidOutput;
            var expected = Analyze(input);
            var normalized = Fields(expected.Fields);
            if (
                expected.Issues.Length > 16
                || normalized.Where((field, index) => !ValidField(input.Text, index, field)).Any()
            )
                return TripBriefErrors.InvalidOutput;
            for (var i = 0; i < proposed.Length; i++)
            {
                var claim = proposed[i];
                var known = normalized[i];
                if (claim.State == "missing")
                {
                    normalized[i] = BriefFields.Missing();
                    continue;
                }
                if (known.State == "missing" || !Supports(input.Text, i, claim.Evidence, known))
                    return TripBriefErrors.InvalidOutput;
                if (known.State == "extracted")
                {
                    if (
                        claim.State != "extracted"
                        || !JsonEqual(claim.Value!.Value, known.Value!.Value)
                    )
                        return TripBriefErrors.InvalidOutput;
                }
                // A recognized explicit invalid value/conflict never becomes a clamped or chosen value.
                else if (claim.State != "extracted" && claim.State != known.State)
                    return TripBriefErrors.InvalidOutput;
                if (
                    claim.Alternatives.Any(alternative =>
                        !known.Alternatives.Any(allowed => JsonEqual(alternative, allowed))
                    )
                )
                    return TripBriefErrors.InvalidOutput;
            }
            for (var i = 1; i <= 3; i++)
                if (
                    normalized[i].State == "missing"
                    && Fields(expected.Fields)[i].State is "invalid" or "ambiguous" or "unsupported"
                    && proposed.Skip(1).Take(3).Any(field => field.State != "missing")
                )
                    normalized[i] = Fields(expected.Fields)[i];
            // Derived end is admissible only when both source relations were claimed; no form values enter here.
            if (
                proposed[2].State == "missing"
                && normalized[1].State == "extracted"
                && normalized[3].State == "extracted"
                && expected.Fields.EndDate.State == "extracted"
            )
                normalized[2] = expected.Fields.EndDate;
            foreach (var issue in reply.Issues)
                if (
                    issue is null
                    || !IssueCodes.Contains(issue.Code, StringComparer.Ordinal)
                    || issue.Slots is null
                    || issue.Slots.Length > 7
                    || issue.Slots.Distinct(StringComparer.Ordinal).Count() != issue.Slots.Length
                    || issue.Slots.Any(slot => !SlotNames.Contains(slot, StringComparer.Ordinal))
                    || issue.Evidence is null
                    || issue.Evidence.Length is < 1 or > 4
                    || issue.Evidence.Any(span => !TripBriefTextRules.IsSpan(input.Text, span))
                    || !expected.Issues.Any(known =>
                        known.Code == issue.Code && Covers(issue.Evidence, known.Evidence)
                    )
                )
                    return TripBriefErrors.InvalidOutput;
            return expected with
            {
                Fields = new(
                    normalized[0],
                    normalized[1],
                    normalized[2],
                    normalized[3],
                    normalized[4],
                    normalized[5],
                    normalized[6]
                ),
            };
        }
        catch (Exception ex)
            when (ex
                    is ArgumentException
                        or InvalidOperationException
                        or RegexMatchTimeoutException
                        or OverflowException
            )
        {
            return TripBriefErrors.InvalidOutput;
        }
    }

    public static bool ValidInput(TripBriefExtractionInput input) =>
        input.RequestId != Guid.Empty
        && !string.IsNullOrWhiteSpace(input.ProcessingPolicyVersion)
        && input.ProcessingPolicyVersion.Length <= 128
        && input.ProcessingPolicyVersion.All(c => c is >= '!' and <= '~')
        && input.Locale == "ru"
        && input.ReferenceZone is null or "Europe/Moscow" or "Europe/Kaliningrad"
        && TripBriefTextRules.IsValid(input.Text);

    // Pure grammar qualification helper: production only delivers fields admitted by Validate's evidence checks.
    public static TripBriefCandidate Analyze(TripBriefExtractionInput input)
    {
        var text = input.Text;
        var issues = new List<TripsBriefIssue>();
        var cityMatches = Find(text, TripBriefLexicon.Cities)
            .Where(x => !TripBriefLexicon.IsNegative(text, x.Span.Start))
            .ToArray();
        var origins = cityMatches
            .Where(x => TripBriefLexicon.IsOrigin(text, x.Span.Start))
            .ToArray();
        foreach (var origin in origins)
            issues.Add(BriefFields.Issue("unsupportedOrigin", [], origin.Span));
        foreach (
            Match clause in TripBriefLexicon
                .Regex(@"(?<!\p{L})из\s+(?<city>[\p{L}\p{M}-]+)")
                .Matches(text)
        )
        {
            var source = clause.Groups["city"];
            if (!origins.Any(origin => origin.Span.Start == source.Index))
                issues.Add(
                    BriefFields.Issue(
                        "unsupportedOrigin",
                        [],
                        new TripsBriefSpan(source.Index, source.Index + source.Length)
                    )
                );
        }
        var cities = cityMatches.Except(origins).ToArray();
        var destinations = CityCatalogue
            .Cities.Select(x => x.DestinationId)
            .Where(id => cities.Any(x => x.Value == id))
            .ToArray();
        TripsBriefField destination;
        if (destinations.Length == 1)
            destination = BriefFields.Extract(
                destinations[0],
                cities.Select(x => x.Span).ToArray()
            );
        else if (destinations.Length > 1)
        {
            var multiple = text.Contains('→');
            destination = BriefFields.State(
                multiple ? "unsupported" : "ambiguous",
                cities.Select(x => x.Span),
                multiple
                    ? []
                    : destinations.Select(value => JsonSerializer.SerializeToElement(value))
            );
            issues.Add(
                BriefFields.Issue(
                    multiple ? "multipleDestinations" : "destinationChoice",
                    ["destinationId"],
                    destination.Evidence
                )
            );
        }
        else
        {
            destination = BriefFields.Missing();
            var unknown = TripBriefLexicon.Regex(@"(?:^|\s)[вВ]\s+(?<city>[\p{L}-]+)").Match(text);
            if (
                unknown.Success
                && !new[]
                {
                    "пятницу",
                    "понедельник",
                    "среду",
                    "субботу",
                    "воскресенье",
                    "четверг",
                    "вторник",
                    "день",
                }.Contains(unknown.Groups["city"].Value.ToLowerInvariant(), StringComparer.Ordinal)
            )
            {
                var city = unknown.Groups["city"];
                var unresolved =
                    city.Value.Equals("Мосвку", StringComparison.OrdinalIgnoreCase)
                    || city.Value.Any(c => c is >= 'A' and <= 'z')
                        && city.Value.Any(c => c is >= 'А' and <= 'я');
                destination = BriefFields.State(
                    unresolved ? "ambiguous" : "unsupported",
                    [new(city.Index, city.Index + city.Length)]
                );
                issues.Add(
                    BriefFields.Issue(
                        unresolved ? "cityUnresolved" : "cityOutsideCatalogue",
                        ["destinationId"],
                        destination.Evidence
                    )
                );
            }
        }
        var adults = BriefFields.Missing();
        var counts = TripBriefLexicon
            .Regex(
                @"(?<![\p{L}\p{N}])(?<n>"
                    + TripBriefLexicon.NumberPattern
                    + @")\s+(?:взрослых|взрослый|взрослого)(?!\p{L})"
            )
            .Matches(text)
            .Cast<Match>()
            .Where(match => !TripBriefLexicon.IsNegative(text, match.Index))
            .ToArray();
        if (counts.Length > 0)
        {
            var evidence = counts.Select(Span).ToArray();
            var distinct = counts
                .Select(x => TripBriefLexicon.Number(x.Groups["n"].Value))
                .Distinct()
                .ToArray();
            if (distinct.Length > 1)
            {
                adults = BriefFields.State(
                    "ambiguous",
                    evidence,
                    distinct
                        .Where(n => !PartySize.Create(n).IsError)
                        .Select(value => JsonSerializer.SerializeToElement(value))
                );
                issues.Add(BriefFields.Issue("adultConflict", ["adults"], evidence));
            }
            else if (PartySize.Create(distinct[0]).IsError)
            {
                adults = BriefFields.State("invalid", evidence);
                issues.Add(BriefFields.Issue("adultBounds", ["adults"], evidence));
            }
            else
                adults = BriefFields.Extract(distinct[0], evidence);
        }
        else
        {
            var composition = TripBriefLexicon
                .Regex(
                    @"нас\s+(?:"
                        + TripBriefLexicon.NumberPattern
                        + @")|(?:"
                        + TripBriefLexicon.NumberPattern
                        + @")\s+человека|семья"
                )
                .Match(text);
            if (composition.Success)
            {
                adults = BriefFields.State("ambiguous", [Span(composition)]);
                issues.Add(BriefFields.Issue("partyComposition", ["adults"], adults.Evidence));
            }
        }
        var interests = BriefFields.Missing();
        var interestMatches = Find(text, TripBriefLexicon.Interests)
            .Where(x => !TripBriefLexicon.IsNegative(text, x.Span.Start))
            .ToArray();
        var none = TripBriefLexicon.Regex(@"(?<!\p{L})без\s+интересов(?!\p{L})").Match(text);
        if (interestMatches.Length > 0)
            interests = BriefFields.Extract(
                TripBriefLexicon
                    .InterestOrder.Where(id => interestMatches.Any(x => x.Value == id))
                    .ToArray(),
                interestMatches.Select(x => x.Span).ToArray()
            );
        else if (none.Success)
            interests = BriefFields.Extract(Array.Empty<string>(), Span(none));
        var paceMatches = Find(text, TripBriefLexicon.Paces)
            .Where(x => !TripBriefLexicon.IsNegative(text, x.Span.Start))
            .ToArray();
        var paces = paceMatches.Select(x => x.Value).Distinct().ToArray();
        var pace =
            paces.Length == 0 ? BriefFields.Missing()
            : paces.Length == 1
                ? BriefFields.Extract(paces[0], paceMatches.Select(x => x.Span).ToArray())
            : BriefFields.State(
                "ambiguous",
                paceMatches.Select(x => x.Span),
                paces.Select(value => JsonSerializer.SerializeToElement(value))
            );
        if (paces.Length > 1)
            issues.Add(BriefFields.Issue("paceConflict", ["pace"], pace.Evidence));
        foreach (
            var (code, pattern) in new[]
            {
                (
                    "unsupportedMoney",
                    @"бюджет|рублей|рубля|рубль|долларов|евро|(?<!\p{L})(?:RUB|EUR|USD)(?!\p{L})|[$€₽]"
                ),
                (
                    "unsupportedChildren",
                    @"(?<!\p{L})(?:ребёнок|ребенок|ребёнка|ребенка|дети|детей)(?!\p{L})"
                ),
                ("unsupportedAction", @"игнорируй|сохрани|перепиши|отправь|<script>"),
                ("unsupportedBooking", @"забронируй|бронирование|купи\s+билет"),
            }
        )
        {
            var matches = TripBriefLexicon.Regex(pattern).Matches(text).Cast<Match>().ToArray();
            if (matches.Length > 0)
                issues.Add(BriefFields.Issue(code, [], matches.Select(Span).ToArray()));
        }
        var dates = TripBriefDateNormalizer.Normalize(
            text,
            input.ReferenceInstant,
            input.ReferenceZone
        );
        issues.AddRange(dates.Issues);
        return new(
            1,
            input.RequestId,
            input.ProcessingPolicyVersion,
            input.ReferenceInstant,
            input.ReferenceZone,
            input.ReferenceLocalDate,
            TripBriefDateNormalizer.ExpiresAt(
                input.ReferenceInstant,
                input.ReferenceZone,
                dates.HasRelativeWords
            ),
            new(
                destination,
                dates.StartDate,
                dates.EndDate,
                dates.DurationDays,
                adults,
                interests,
                pace
            ),
            issues.DistinctBy(x => x.Code).ToArray()
        );
    }

    private static IEnumerable<(string Value, TripsBriefSpan Span)> Find(
        string text,
        (string Value, string Aliases)[] aliases
    ) => TripBriefLexicon.FindAliases(text, aliases);

    private static TripsBriefSpan Span(Match match) => new(match.Index, match.Index + match.Length);

    private static TripsBriefField[] Fields(TripsBriefFields fields) =>
        [
            fields.DestinationId,
            fields.StartDate,
            fields.EndDate,
            fields.DurationDays,
            fields.Adults,
            fields.Interests,
            fields.Pace,
        ];

    private static bool Covers(TripsBriefSpan[] offered, TripsBriefSpan[] required) =>
        required.Any(wanted =>
            offered.Any(span => span.Start <= wanted.Start && span.End >= wanted.End)
        );

    private static bool Supports(
        string text,
        int slot,
        TripsBriefSpan[] offered,
        TripsBriefField known
    )
    {
        if (slot == 5 && known.State == "extracted" && known.Value!.Value.GetArrayLength() > 0)
            return known
                .Value.Value.EnumerateArray()
                .All(value =>
                    Find(text, TripBriefLexicon.Interests)
                        .Where(match =>
                            match.Value == value.GetString()
                            && !TripBriefLexicon.IsNegative(text, match.Span.Start)
                        )
                        .Any(match => Covers(offered, [match.Span]))
                );
        if (slot == 2 && known.State == "extracted" && known.Evidence.Length > 1)
            return known.Evidence.All(span => Covers(offered, [span]));
        return Covers(offered, known.Evidence);
    }

    private static bool JsonEqual(JsonElement left, JsonElement right) =>
        JsonElement.DeepEquals(left, right);

    private static bool ValidField(string text, int slot, TripsBriefField? field)
    {
        if (
            field is null
            || field.State
                is not ("extracted" or "missing" or "ambiguous" or "unsupported" or "invalid")
            || field.Evidence is null
            || field.Alternatives is null
            || field.Evidence.Length > 4
            || field.Alternatives.Length > 4
            || field.Evidence.Any(span => !TripBriefTextRules.IsSpan(text, span))
        )
            return false;
        var value = field.Value is { ValueKind: not JsonValueKind.Null };
        if (field.State == "missing")
            return !value && field.Evidence.Length == 0 && field.Alternatives.Length == 0;
        if (
            field.Evidence.Length == 0
            || field.State == "extracted" != value
            || field.State != "ambiguous" && field.Alternatives.Length != 0
        )
            return false;
        return (!value || ValidValue(slot, field.Value!.Value))
            && field.Alternatives.All(item => ValidValue(slot, item));
    }

    private static bool ValidValue(int slot, JsonElement value) =>
        slot switch
        {
            0 => value.ValueKind == JsonValueKind.String
                && !CityCatalogue.Find(value.GetString()).IsError,
            1 or 2 => value.ValueKind == JsonValueKind.String
                && value.GetString()?.Length == 10
                && DateOnly.TryParseExact(
                    value.GetString(),
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out _
                ),
            3 or 4 => value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out _),
            5 => value.ValueKind == JsonValueKind.Array
                && value.GetArrayLength() <= 5
                && value
                    .EnumerateArray()
                    .All(item =>
                        item.ValueKind == JsonValueKind.String
                        && TripBriefLexicon.InterestOrder.Contains(
                            item.GetString(),
                            StringComparer.Ordinal
                        )
                    )
                && value
                    .EnumerateArray()
                    .Select(item => item.GetString())
                    .Distinct(StringComparer.Ordinal)
                    .Count() == value.GetArrayLength(),
            6 => value.ValueKind == JsonValueKind.String
                && value.GetString() is "relaxed" or "balanced" or "active",
            _ => false,
        };
}
