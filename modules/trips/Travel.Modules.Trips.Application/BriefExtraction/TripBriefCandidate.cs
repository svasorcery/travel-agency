using System.Buffers;
using System.Text;
using System.Text.Json;
using ErrorOr;
using Travel.IntegrationContracts.AI.TripsBriefExtraction;

namespace Travel.Modules.Trips.Application.BriefExtraction;

public sealed record TripBriefExtractionInput(
    Guid RequestId,
    string ProcessingPolicyVersion,
    string Text,
    string Locale,
    DateTimeOffset ReferenceInstant,
    string? ReferenceZone,
    DateTimeOffset Deadline
)
{
    public DateOnly? ReferenceLocalDate =>
        TripBriefDateNormalizer.LocalDate(ReferenceInstant, ReferenceZone);

    public override string ToString() => nameof(TripBriefExtractionInput);
}

public sealed record TripBriefCandidate(
    int CandidateVersion,
    Guid RequestId,
    string ProcessingPolicyVersion,
    DateTimeOffset ReferenceInstant,
    string? ReferenceZone,
    DateOnly? ReferenceLocalDate,
    DateTimeOffset ExpiresAt,
    TripsBriefFields Fields,
    TripsBriefIssue[] Issues
)
{
    public override string ToString() => nameof(TripBriefCandidate);
}

public static class TripBriefErrors
{
    public static Error InvalidInput =>
        Error.Validation("Trips.BriefInvalidInput", "Invalid extraction request.");
    public static Error InvalidOutput =>
        Error.Custom(502, "Trips.InvalidModelOutput", "Invalid extraction result.");
    public static Error Unavailable =>
        Error.Custom(503, "Trips.BriefUnavailable", "Extraction is unavailable.");
    public static Error PolicyChanged =>
        Error.Custom(503, "Trips.ProcessingPolicyChanged", "Processing policy changed.");
    public static Error Busy => Error.Custom(429, "Trips.BriefBusy", "Extraction is busy.");
    public static Error Deadline =>
        Error.Custom(504, "Trips.BriefDeadline", "Extraction deadline exceeded.");
}

public static class TripBriefTextRules
{
    public static bool IsValid(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 2000)
            return false;
        var span = text.AsSpan();
        var scalars = 0;
        while (!span.IsEmpty)
        {
            if (
                Rune.DecodeFromUtf16(span, out var rune, out var count) != OperationStatus.Done
                || ++scalars > 1000
                || Rune.IsControl(rune) && rune.Value != 10
            )
                return false;
            span = span[count..];
        }
        return Encoding.UTF8.GetByteCount(text) <= 4096;
    }

    public static bool IsSpan(string text, TripsBriefSpan? span) =>
        span is not null
        && span.Start >= 0
        && span.End > span.Start
        && span.End <= text.Length
        && !(span.Start > 0 && char.IsLowSurrogate(text[span.Start]))
        && !(span.End < text.Length && char.IsLowSurrogate(text[span.End]));
}

internal static class BriefFields
{
    public static TripsBriefField Missing() => new("missing", null, [], []);

    public static TripsBriefField State(
        string state,
        IEnumerable<TripsBriefSpan> spans,
        IEnumerable<JsonElement>? alternatives = null
    ) => new(state, null, spans.Distinct().Take(4).ToArray(), alternatives?.ToArray() ?? []);

    public static TripsBriefField Extract(object value, params TripsBriefSpan[] spans) =>
        new(
            "extracted",
            JsonSerializer.SerializeToElement(value),
            spans.Distinct().Take(4).ToArray(),
            []
        );

    public static TripsBriefIssue Issue(
        string code,
        string[] slots,
        params TripsBriefSpan[] spans
    ) => new(code, slots, spans.Distinct().Take(4).ToArray());
}
