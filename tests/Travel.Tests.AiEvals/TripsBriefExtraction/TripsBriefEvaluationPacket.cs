using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Travel.IntegrationContracts.AI.TripsBriefExtraction;
using Travel.Modules.Trips.Application.BriefExtraction;

namespace Travel.Tests.AiEvals.TripsBriefExtraction;

public sealed record TripsBriefExpectedSlot(
    string State,
    JsonElement? Value,
    JsonElement[] Alternatives
);

public sealed record TripsBriefEvalCase(
    string Id,
    string Partition,
    string Text,
    DateTimeOffset ReferenceInstant,
    string? ReferenceZone,
    Dictionary<string, TripsBriefExpectedSlot> Expected,
    string[] ExpectedIssues,
    bool Useful,
    bool Hazardous,
    string ExpectedSummary
)
{
    public override string ToString() => Id;
}

public sealed record TripsBriefEvaluationPacket(
    int SchemaVersion,
    string Kind,
    string SourceCorpusRowsSha256,
    TripsBriefEvalCase[] Cases
)
{
    public const string KnownCorpusRowsSha256 =
        "ed0ef5cb4a3a407b4b4d988bf4be466ae79ec549b129f7e2f0fa2f75de446052";
    public static readonly string[] Slots =
    [
        "destinationId",
        "startDate",
        "endDate",
        "durationDays",
        "adults",
        "interests",
        "pace",
    ];
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static TripsBriefEvaluationPacket Load(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length > 256 * 1024)
            throw new InvalidOperationException("Evaluation packet too large.");
        using (var doc = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 12 }))
            CheckKeys(doc.RootElement);
        var packet =
            JsonSerializer.Deserialize<TripsBriefEvaluationPacket>(bytes, Json)
            ?? throw new InvalidOperationException("Invalid evaluation packet.");
        Validate(packet);
        return packet;
    }

    public static void Validate(TripsBriefEvaluationPacket packet)
    {
        var qualification = packet.Kind == "open-fictional-qualification";
        var count =
            qualification ? 48
            : packet.Kind == "sealed-fictional-holdout" ? 24
            : 0;
        if (
            packet.SchemaVersion != 1
            || count == 0
            || !System.Text.RegularExpressions.Regex.IsMatch(
                packet.SourceCorpusRowsSha256 ?? "",
                "^[a-f0-9]{64}$"
            )
            || qualification && packet.SourceCorpusRowsSha256 != KnownCorpusRowsSha256
            || packet.Cases is null
            || packet.Cases.Length != count
            || packet.Cases.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != count
        )
            throw new InvalidOperationException("Corpus identity/denominator mismatch.");
        for (var i = 0; i < count; i++)
        {
            var c = packet.Cases[i];
            var expectedId =
                (qualification ? (i < 24 ? "D" : "H") : "E") + ((i % 24) + 1).ToString("D2");
            if (
                c.Id != expectedId
                || c.Partition
                    != (qualification ? (i < 24 ? "development" : "qualification") : "holdout")
                || !TripBriefTextRules.IsValid(c.Text)
                || c.Expected is null
                || !c
                    .Expected.Keys.Order(StringComparer.Ordinal)
                    .SequenceEqual(Slots.Order(StringComparer.Ordinal))
                || c.ReferenceZone is not (null or "Europe/Moscow" or "Europe/Kaliningrad")
                || c.ExpectedIssues is null
                || c.ExpectedIssues.Any(x =>
                    !TripBriefCandidateValidator.IssueCodes.Contains(x, StringComparer.Ordinal)
                )
            )
                throw new InvalidOperationException("Invalid fictional case: " + c.Id);
            foreach (var s in c.Expected.Values)
                if (
                    s is null
                    || s.State
                        is not (
                            "missing"
                            or "extracted"
                            or "ambiguous"
                            or "unsupported"
                            or "invalid"
                        )
                    || (s.State == "extracted")
                        != (s.Value is { ValueKind: not JsonValueKind.Null })
                    || s.Alternatives is null
                )
                    throw new InvalidOperationException("Invalid expected slot.");
        }
        if (
            packet.Cases.Count(x => x.Useful) != 12
            || (
                qualification
                    ? packet.Cases.Count(x => x.Hazardous) != 10
                    : packet.Cases.All(x => !x.Hazardous)
            )
        )
            throw new InvalidOperationException("Metric denominator mismatch.");
    }

    public static TripBriefExtractionInput Input(TripsBriefEvalCase c, DateTimeOffset runtimeNow) =>
        new(
            Guid.NewGuid(),
            TripsBriefPolicyIdentity.Compute(
                "anthropic",
                "fictional-model",
                TripsBriefMessageIdentity.PrivacyNoticeVersion
            ),
            c.Text,
            "ru",
            c.ReferenceInstant,
            c.ReferenceZone,
            runtimeNow.AddSeconds(5)
        );

    public static TripsBriefRequested Request(TripsBriefEvalCase c, DateTimeOffset runtimeNow)
    {
        var input = Input(c, runtimeNow);
        return new(
            input.RequestId,
            input.ProcessingPolicyVersion,
            input.Text,
            input.Locale,
            input.ReferenceInstant,
            input.ReferenceZone,
            input.ReferenceLocalDate,
            input.Deadline
        );
    }

    public static bool Matches(TripsBriefEvalCase c, TripBriefCandidate candidate)
    {
        var values = Values(candidate.Fields);
        for (var i = 0; i < Slots.Length; i++)
        {
            var expected = c.Expected[Slots[i]];
            var actual = values[i];
            if (
                actual.State != expected.State
                || !Equal(actual.Value, expected.Value)
                || expected.Alternatives.Length > 0
                    && (
                        actual.Alternatives.Length != expected.Alternatives.Length
                        || actual
                            .Alternatives.Where(
                                (a, index) =>
                                    !JsonElement.DeepEquals(a, expected.Alternatives[index])
                            )
                            .Any()
                    )
            )
                return false;
        }
        return candidate
            .Issues.Select(x => x.Code)
            .Order(StringComparer.Ordinal)
            .SequenceEqual(c.ExpectedIssues.Order(StringComparer.Ordinal));
    }

    public static bool Unsafe(TripsBriefEvalCase c, TripBriefCandidate candidate)
    {
        var actual = Values(candidate.Fields);
        for (var i = 0; i < Slots.Length; i++)
            if (
                actual[i].State == "extracted"
                && (
                    c.Expected[Slots[i]].State != "extracted"
                    || !Equal(actual[i].Value, c.Expected[Slots[i]].Value)
                )
            )
                return true;
        return c.Hazardous
            && c.ExpectedIssues.Except(candidate.Issues.Select(x => x.Code), StringComparer.Ordinal)
                .Any();
    }

    public static TripsBriefField[] Values(TripsBriefFields f) =>
        [f.DestinationId, f.StartDate, f.EndDate, f.DurationDays, f.Adults, f.Interests, f.Pace];

    public static bool Equal(JsonElement? a, JsonElement? b) =>
        a is null
            ? b is null || b.Value.ValueKind == JsonValueKind.Null
            : b is not null && JsonElement.DeepEquals(a.Value, b.Value);

    public static string HashFile(string path) =>
        Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

    private static void CheckKeys(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in element.EnumerateObject())
            {
                if (!names.Add(p.Name))
                    throw new InvalidOperationException("Duplicate packet member.");
                CheckKeys(p.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var child in element.EnumerateArray())
                CheckKeys(child);
    }
}
