using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Travel.IntegrationContracts.AI.TripsBriefExtraction;

namespace Travel.AI.TripsBriefExtraction;

public sealed class TripsBriefExtractor(
    IChatClient chat,
    TripsBriefOptions options,
    TripsBriefAdmission admission,
    TimeProvider time
)
{
    public const int MaxInputTokens = 4096;
    public const int MaxOutputTokens = 1024;
    public const int MaxResponseBytes = 16 * 1024;
    public const int HandlerMilliseconds = 4500;
    public const int InferenceMilliseconds = 3500;
    private const int CompletionReserveMilliseconds = 250;
    private static readonly string[] Slots =
    [
        "destinationId",
        "startDate",
        "endDate",
        "durationDays",
        "adults",
        "interests",
        "pace",
    ];
    private static readonly string[] Interests =
    [
        "architecture",
        "culture",
        "food",
        "nature",
        "history",
    ];
    private static readonly HashSet<string> IssueCodes =
    [
        "partyComposition",
        "yearMissing",
        "reversedDates",
        "dateDurationConflict",
        "invalidDate",
        "adultBounds",
        "destinationChoice",
        "multipleDestinations",
        "unsupportedOrigin",
        "unsupportedMoney",
        "paceConflict",
        "durationBounds",
        "unsupportedDateExpression",
        "unsupportedChildren",
        "unsupportedAction",
        "referenceZoneMissing",
        "cityUnresolved",
        "adultConflict",
        "dateFormatAmbiguous",
        "cityOutsideCatalogue",
        "unsupportedBooking",
        "weekdayDateConflict",
        "dateConflict",
        "dateBounds",
    ];
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
    private static readonly Lazy<string> Prompt = new(() =>
    {
        using var stream =
            typeof(TripsBriefExtractor).Assembly.GetManifestResourceStream(
                "prompts.v1.trips.brief-extraction.system.md"
            ) ?? throw new InvalidOperationException("Trips brief prompt unavailable.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    });

    // Runtime validation below further constrains each slot's type, values and evidence relationship.
    private static readonly string Schema = BuildSchema();

    internal static JsonElement LocalSchema()
    {
        using var document = JsonDocument.Parse(Schema);
        return document.RootElement.Clone();
    }

    public static string SchemaSha256()
    {
        using var document = JsonDocument.Parse(Schema);
        return Convert.ToHexStringLower(
            System.Security.Cryptography.SHA256.HashData(
                JsonSerializer.SerializeToUtf8Bytes(document.RootElement)
            )
        );
    }

    private static string BuildSchema()
    {
        const string span =
            "{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"start\",\"end\"],\"properties\":{\"start\":{\"type\":\"integer\"},\"end\":{\"type\":\"integer\"}}}";
        var field =
            "{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"state\",\"value\",\"evidence\",\"alternatives\"],\"properties\":{\"state\":{\"enum\":[\"extracted\",\"missing\",\"ambiguous\",\"unsupported\",\"invalid\"]},\"value\":{},\"evidence\":{\"type\":\"array\",\"maxItems\":4,\"items\":{\"$ref\":\"#/$defs/span\"}},\"alternatives\":{\"type\":\"array\",\"maxItems\":4,\"items\":{}}}}";
        return "{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"fields\",\"issues\"],\"properties\":{\"fields\":{\"type\":\"object\",\"additionalProperties\":false,\"required\":"
            + JsonSerializer.Serialize(Slots)
            + ",\"properties\":{"
            + string.Join(",", Slots.Select(s => $"\"{s}\":{{\"$ref\":\"#/$defs/field\"}}"))
            + "}},\"issues\":{\"type\":\"array\",\"maxItems\":16,\"items\":{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"code\",\"slots\",\"evidence\"],\"properties\":{\"code\":{\"type\":\"string\"},\"slots\":{\"type\":\"array\",\"items\":{\"type\":\"string\"}},\"evidence\":{\"type\":\"array\",\"maxItems\":4,\"items\":{\"$ref\":\"#/$defs/span\"}}}}}},\"$defs\":{\"span\":"
            + span
            + ",\"field\":"
            + field
            + "}}";
    }

    public sealed record InputEstimate(int EstimatedInputTokens, bool WithinBounds);

    private static string Payload(TripsBriefRequested request) =>
        JsonSerializer.Serialize(
            new
            {
                text = request.Text,
                locale = request.Locale,
                referenceInstant = request.ReferenceInstant,
                referenceZone = request.ReferenceZone,
                localDate = request.ReferenceLocalDate,
                promptVersion = TripsBriefMessageIdentity.PromptVersion,
            },
            Json
        );

    public static InputEstimate EstimateInput(TripsBriefRequested request, string model = "")
    {
        if (!ValidInput(request))
            return new(0, false);
        try
        {
            var envelope = JsonSerializer.Serialize(
                new
                {
                    system = Prompt.Value,
                    messages = new[] { new { role = "user", content = Payload(request) } },
                    output_config = new
                    {
                        format = new
                        {
                            type = "json_schema",
                            schema = TripsBriefWireProjection.Schema(),
                        },
                    },
                    model,
                    temperature = 0,
                    max_tokens = MaxOutputTokens,
                },
                new JsonSerializerOptions(Json) { Encoder = JavaScriptEncoder.Default }
            );
            var count = Encoding.UTF8.GetByteCount(envelope) + 128;
            return new(count, count <= MaxInputTokens);
        }
        catch (Exception)
        {
            return new(0, false);
        }
    }

    public async Task<TripsBriefParsed> ExtractAsync(
        TripsBriefRequested request,
        ITripsBriefLedger ledger,
        CancellationToken cancellationToken = default
    )
    {
        var started = time.GetTimestamp();
        var reply = Reply(request, "Disabled");
        if (!options.Enabled)
            return reply;
        if (!options.IsConfigured)
            return reply with { Status = "ModelUnavailable" };
        if (request.ProcessingPolicyVersion != options.ProcessingPolicyVersion)
            return reply with { Status = "ProcessingPolicyChanged" };
        if (
            cancellationToken.IsCancellationRequested
            || request.Deadline - time.GetUtcNow() < TimeSpan.FromMilliseconds(500)
        )
            return reply with { Status = "DeadlineExceeded" };
        var estimate = EstimateInput(request, options.Model);
        if (!estimate.WithinBounds)
            return reply with { Status = "InputUnavailable" };
        decimal upper;
        try
        {
            upper = Price(estimate.EstimatedInputTokens, MaxOutputTokens);
        }
        catch (OverflowException)
        {
            return reply with { Status = "BudgetExceeded" };
        }
        if (upper > options.MaxRequestCostUsd)
            return reply with { Status = "BudgetExceeded" };
        using var lease = admission.TryEnter(upper);
        if (lease is null)
            return reply with { Status = "Busy" };
        var remaining = request.Deadline - time.GetUtcNow();
        var handlerBudget =
            TimeSpan.FromMilliseconds(HandlerMilliseconds) - time.GetElapsedTime(started);
        var wholeBudget = remaining < handlerBudget ? remaining : handlerBudget;
        if (wholeBudget <= TimeSpan.Zero)
            return reply with { Status = "DeadlineExceeded" };
        using var timeout = new CancellationTokenSource(wholeBudget, time);
        using var whole = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeout.Token
        );
        try
        {
            if (
                !await AccountAsync(
                    () => ledger.AdmitAsync(request.CorrelationId, options.Model, whole.Token),
                    lease,
                    whole.Token
                )
            )
                return reply with { Status = "AccountingUnavailable" };
            // Transport and the committed predispatch row consume the same absolute deadline.
            remaining = request.Deadline - time.GetUtcNow();
            var handlerRemaining =
                TimeSpan.FromMilliseconds(HandlerMilliseconds) - time.GetElapsedTime(started);
            if (handlerRemaining < remaining)
                remaining = handlerRemaining;
            if (whole.IsCancellationRequested || remaining < TimeSpan.FromMilliseconds(500))
                return reply with { Status = "DeadlineExceeded" };
            if (!options.IsConfigured)
                return reply with { Status = "ModelUnavailable" };
            if (request.ProcessingPolicyVersion != options.ProcessingPolicyVersion)
                return reply with { Status = "ProcessingPolicyChanged" };
            var inferenceBudget =
                remaining - TimeSpan.FromMilliseconds(CompletionReserveMilliseconds);
            if (inferenceBudget > TimeSpan.FromMilliseconds(InferenceMilliseconds))
                inferenceBudget = TimeSpan.FromMilliseconds(InferenceMilliseconds);
            using var inferenceTimeout = new CancellationTokenSource(inferenceBudget, time);
            using var inferenceToken = CancellationTokenSource.CreateLinkedTokenSource(
                whole.Token,
                inferenceTimeout.Token
            );
            Task<ChatResponse>? pending = null;
            try
            {
                using var schema = JsonDocument.Parse(Schema);
                pending = chat.GetResponseAsync(
                    [new(ChatRole.System, Prompt.Value), new(ChatRole.User, Payload(request))],
                    new ChatOptions
                    {
                        ModelId = options.Model,
                        Temperature = 0,
                        MaxOutputTokens = MaxOutputTokens,
                        ResponseFormat = ChatResponseFormat.ForJsonSchema(
                            schema.RootElement.Clone()
                        ),
                    },
                    inferenceToken.Token
                );
                var response = await pending.WaitAsync(inferenceToken.Token);
                reply = ReadResponse(request, response);
                if (inferenceToken.IsCancellationRequested || time.GetUtcNow() >= request.Deadline)
                    reply = reply with
                    {
                        Status = "DeadlineExceeded",
                        Fields = null,
                        Issues = null,
                    };
            }
            catch (Exception)
            {
                if (pending is not null)
                    lease.DeferUntil(pending);
                reply = reply with
                {
                    Status = inferenceToken.IsCancellationRequested
                        ? "DeadlineExceeded"
                        : "InferenceFailed",
                };
            }
            var metadata = reply with { Fields = null, Issues = null };
            if (
                !await AccountAsync(
                    () => ledger.CompleteAsync(metadata, whole.Token),
                    lease,
                    whole.Token
                )
            )
                return metadata with { Status = "AccountingUnavailable" };
            lease.Settle(reply.CostUsd);
            if (whole.IsCancellationRequested || time.GetUtcNow() >= request.Deadline)
                return metadata with { Status = "DeadlineExceeded" };
            return reply;
        }
        catch (Exception)
        {
            return reply with
            {
                Status = whole.IsCancellationRequested ? "DeadlineExceeded" : "InferenceFailed",
                Fields = null,
                Issues = null,
            };
        }
    }

    private async Task<bool> AccountAsync(
        Func<Task> action,
        TripsBriefAdmission.Lease lease,
        CancellationToken ct
    )
    {
        Task? pending = null;
        try
        {
            ct.ThrowIfCancellationRequested();
            pending = action();
            await pending.WaitAsync(ct);
            return true;
        }
        catch (Exception)
        {
            if (pending is not null)
                lease.DeferUntil(pending);
            return false;
        }
    }

    private decimal Price(int input, int output) =>
        (input * options.InputUsdPerMillion + output * options.OutputUsdPerMillion) / 1_000_000m;

    private TripsBriefParsed ReadResponse(TripsBriefRequested request, ChatResponse response)
    {
        var reply = Reply(request, "ModelUnavailable") with
        {
            InputTokens = Count(response.Usage?.InputTokenCount),
            OutputTokens = Count(response.Usage?.OutputTokenCount),
        };
        if (response.ModelId != options.Model)
            return reply;
        reply = reply with { ModelId = options.Model, Status = "AccountingUnavailable" };
        if (reply.InputTokens is not { } input || reply.OutputTokens is not { } output)
            return reply;
        try
        {
            reply = reply with { CostUsd = Price(input, output) };
        }
        catch (OverflowException)
        {
            return reply;
        }
        if (
            input > MaxInputTokens
            || output > MaxOutputTokens
            || reply.CostUsd > options.MaxRequestCostUsd
        )
            return reply with { Status = "BudgetExceeded" };
        if (response.FinishReason == ChatFinishReason.ContentFilter)
            return reply with { Status = "Refused" };
        if (
            response.FinishReason == ChatFinishReason.Length
            || response.FinishReason == ChatFinishReason.ToolCalls
            || response.Messages.SelectMany(m => m.Contents).Any(c => c is not TextContent)
        )
            return reply with { Status = "InvalidOutput" };
        var parsed = Parse(response.Text, request.Text);
        return parsed is null
            ? reply with
            {
                Status = "InvalidOutput",
            }
            : reply with
            {
                Status = "Ready",
                Fields = parsed.Value.Fields,
                Issues = parsed.Value.Issues,
            };
    }

    private static int? Count(long? value) => value is >= 0 and <= int.MaxValue ? (int)value : null;

    private static TripsBriefParsed Reply(TripsBriefRequested request, string status) =>
        new(request.CorrelationId, request.ProcessingPolicyVersion, status);

    private static bool ValidInput(TripsBriefRequested request)
    {
        if (
            request.CorrelationId == Guid.Empty
            || request.Locale != "ru"
            || string.IsNullOrWhiteSpace(request.Text)
            || request.Text.Length > 2000
            || !ValidUnicode(request.Text, true)
            || Encoding.UTF8.GetByteCount(request.Text) > 4096
            || request.Text.EnumerateRunes().Count() > 1000
        )
            return false;
        if (request.ReferenceZone is null)
            return request.ReferenceLocalDate is null;
        if (request.ReferenceZone is not ("Europe/Moscow" or "Europe/Kaliningrad"))
            return false;
        try
        {
            var local = TimeZoneInfo.ConvertTime(
                request.ReferenceInstant,
                TimeZoneInfo.FindSystemTimeZoneById(request.ReferenceZone)
            );
            return request.ReferenceLocalDate == DateOnly.FromDateTime(local.DateTime);
        }
        catch (TimeZoneNotFoundException)
        {
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            return false;
        }
    }

    private static bool ValidUnicode(string value, bool allowLf = false)
    {
        var remaining = value.AsSpan();
        while (!remaining.IsEmpty)
        {
            if (
                Rune.DecodeFromUtf16(remaining, out var rune, out var used) != OperationStatus.Done
                || (Rune.IsControl(rune) && !(allowLf && rune.Value == 10))
            )
                return false;
            remaining = remaining[used..];
        }
        return true;
    }

    private static (TripsBriefFields Fields, TripsBriefIssue[] Issues)? Parse(
        string? text,
        string source
    )
    {
        if (
            text is null
            || text.Length > MaxResponseBytes
            || !ValidUnicode(text, true)
            || Encoding.UTF8.GetByteCount(text) > MaxResponseBytes
        )
            return null;
        try
        {
            using var doc = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 8 });
            var root = doc.RootElement;
            if (
                !Exact(root, "fields", "issues")
                || !Exact(root.GetProperty("fields"), Slots)
                || root.GetProperty("issues").ValueKind != JsonValueKind.Array
            )
                return null;
            var fields = new List<TripsBriefField>();
            foreach (var name in Slots)
            {
                var item = root.GetProperty("fields").GetProperty(name);
                if (
                    !Exact(item, "state", "value", "evidence", "alternatives")
                    || item.GetProperty("state").ValueKind != JsonValueKind.String
                    || item.GetProperty("alternatives").ValueKind != JsonValueKind.Array
                )
                    return null;
                var state = item.GetProperty("state").GetString()!;
                var value = item.GetProperty("value");
                if (
                    state
                    is not ("extracted" or "missing" or "ambiguous" or "unsupported" or "invalid")
                )
                    return null;
                if (
                    state == "extracted"
                        ? !ValidValue(name, value)
                        : value.ValueKind != JsonValueKind.Null
                )
                    return null;
                var evidence = ReadSpans(item.GetProperty("evidence"), source);
                var alternatives = item.GetProperty("alternatives").EnumerateArray().ToArray();
                if (
                    evidence is null
                    || alternatives.Length > 4
                    || (state == "missing" ? evidence.Length != 0 : evidence.Length == 0)
                    || (state != "ambiguous" && alternatives.Length != 0)
                    || alternatives.Any(a => !ValidValue(name, a))
                    || alternatives
                        .Select(a => a.GetRawText())
                        .Distinct(StringComparer.Ordinal)
                        .Count() != alternatives.Length
                )
                    return null;
                fields.Add(
                    new(
                        state,
                        state == "extracted" ? value.Clone() : null,
                        evidence,
                        alternatives.Select(a => a.Clone()).ToArray()
                    )
                );
            }
            var issues = new List<TripsBriefIssue>();
            foreach (var issue in root.GetProperty("issues").EnumerateArray())
            {
                if (
                    issues.Count >= 16
                    || !Exact(issue, "code", "slots", "evidence")
                    || issue.GetProperty("code").ValueKind != JsonValueKind.String
                    || !IssueCodes.Contains(issue.GetProperty("code").GetString()!)
                    || issue.GetProperty("slots").ValueKind != JsonValueKind.Array
                )
                    return null;
                var slotItems = issue.GetProperty("slots").EnumerateArray().ToArray();
                if (
                    slotItems.Length > 7
                    || slotItems.Any(s =>
                        s.ValueKind != JsonValueKind.String
                        || !Slots.Contains(s.GetString(), StringComparer.Ordinal)
                    )
                )
                    return null;
                var slots = slotItems.Select(s => s.GetString()!).ToArray();
                var evidence = ReadSpans(issue.GetProperty("evidence"), source);
                if (
                    evidence is not { Length: > 0 }
                    || slots.Distinct(StringComparer.Ordinal).Count() != slots.Length
                )
                    return null;
                issues.Add(new(issue.GetProperty("code").GetString()!, slots, evidence));
            }
            return (
                new(fields[0], fields[1], fields[2], fields[3], fields[4], fields[5], fields[6]),
                issues.ToArray()
            );
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool ValidValue(string slot, JsonElement value)
    {
        if (slot is "adults" or "durationDays")
            return value.ValueKind == JsonValueKind.Number
                && value.TryGetInt32(out var n)
                && n >= 1
                && n <= (slot == "adults" ? 9 : 14);
        if (slot == "interests")
        {
            if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() > 5)
                return false;
            var previous = -1;
            foreach (var item in value.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String)
                    return false;
                var index = Array.IndexOf(Interests, item.GetString());
                if (index <= previous)
                    return false;
                previous = index;
            }
            return true;
        }
        if (value.ValueKind != JsonValueKind.String)
            return false;
        var s = value.GetString();
        return slot switch
        {
            "destinationId" => s is "moscow" or "saint-petersburg" or "kaliningrad",
            "pace" => s is "relaxed" or "balanced" or "active",
            "startDate" or "endDate" => s is { Length: 10 }
                && DateOnly.TryParseExact(
                    s,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out _
                ),
            _ => false,
        };
    }

    private static TripsBriefSpan[]? ReadSpans(JsonElement element, string source)
    {
        if (element.ValueKind != JsonValueKind.Array || element.GetArrayLength() > 4)
            return null;
        var spans = new List<TripsBriefSpan>();
        foreach (var item in element.EnumerateArray())
        {
            if (
                !Exact(item, "start", "end")
                || item.GetProperty("start").ValueKind != JsonValueKind.Number
                || item.GetProperty("end").ValueKind != JsonValueKind.Number
                || !item.GetProperty("start").TryGetInt32(out var start)
                || !item.GetProperty("end").TryGetInt32(out var end)
                || start < 0
                || end <= start
                || end > source.Length
                || !Boundary(start)
                || !Boundary(end)
                || string.IsNullOrWhiteSpace(source[start..end])
            )
                return null;
            var span = new TripsBriefSpan(start, end);
            if (spans.Contains(span))
                return null;
            spans.Add(span);
        }
        return spans.ToArray();
        bool Boundary(int offset) =>
            offset == 0
            || offset == source.Length
            || !(char.IsHighSurrogate(source[offset - 1]) && char.IsLowSurrogate(source[offset]));
    }

    private static bool Exact(JsonElement element, params string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return false;
        var properties = element.EnumerateObject().Select(p => p.Name).ToArray();
        return properties.Length == names.Length
            && properties.Distinct(StringComparer.Ordinal).Count() == names.Length
            && properties.All(p => names.Contains(p, StringComparer.Ordinal));
    }
}
