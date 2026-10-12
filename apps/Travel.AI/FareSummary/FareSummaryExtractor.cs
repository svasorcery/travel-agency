using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using Travel.IntegrationContracts.AI.FareSummary;

namespace Travel.AI.FareSummary;

public sealed class FareSummaryOptions
{
    public bool Enabled { get; set; }
    public string Model { get; set; } = string.Empty;
    public string PricingModel { get; set; } = string.Empty;
    public bool PricingVerified { get; set; }
    public decimal InputUsdPerMillion { get; set; }
    public decimal OutputUsdPerMillion { get; set; }
}

// Per-process rolling admissions, without waiting or a distributed-spend claim.
public sealed class FareSummaryAdmission(TimeProvider time)
{
    private readonly object gate = new();
    private readonly Queue<DateTimeOffset> admitted = new();
    private bool active;
    private bool handling;
    private Task? deferredCompletion;

    public bool TryHoldRequest()
    {
        lock (gate)
        {
            if (handling || active)
                return false;
            handling = true;
            return true;
        }
    }

    public void DeferCompletion(Task pending)
    {
        lock (gate)
            deferredCompletion = pending;
    }

    public void ReleaseRequest()
    {
        lock (gate)
        {
            var pending = deferredCompletion;
            deferredCompletion = null;
            if (pending is not null && !pending.IsCompleted)
            {
                _ = pending.ContinueWith(
                    completed =>
                    {
                        _ = completed.Exception;
                        lock (gate)
                            handling = false;
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default
                );
            }
            else
                handling = false;
        }
    }

    public bool TryEnter()
    {
        lock (gate)
        {
            var now = time.GetUtcNow();
            while (admitted.TryPeek(out var first) && now - first >= TimeSpan.FromMinutes(1))
                admitted.Dequeue();
            if (active || admitted.Count >= 6)
                return false;
            active = true;
            admitted.Enqueue(now);
            return true;
        }
    }

    public void Exit()
    {
        lock (gate)
            active = false;
    }
}

public sealed class FareSummaryExtractor(
    IChatClient chat,
    FareSummaryOptions options,
    FareSummaryAdmission admission,
    TimeProvider time
)
{
    public const int MaxInputTokens = 2048;
    public const int MaxOutputTokens = 512;
    public const decimal MaxCostUsd = 0.10m;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };
    private const string Schema =
        "{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"sections\"],\"properties\":{\"sections\":{\"type\":\"array\",\"items\":{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"kind\",\"groups\"],\"properties\":{\"kind\":{\"type\":\"string\",\"enum\":[\"change\",\"refund\",\"baggage\"]},\"groups\":{\"type\":\"array\",\"items\":{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"factIds\"],\"properties\":{\"factIds\":{\"type\":\"array\",\"items\":{\"type\":\"string\"}}}}}}}}}}";
    private static readonly Lazy<string> Prompt = new(() =>
    {
        using var stream =
            typeof(FareSummaryExtractor).Assembly.GetManifestResourceStream(
                "prompts.v1.flights.fare-summary.system.md"
            ) ?? throw new InvalidOperationException("Fare summary prompt unavailable.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    });

    public sealed record InputEstimate(
        int PayloadBytes,
        int EstimatedInputTokens,
        bool WithinBounds
    );

    public static InputEstimate EstimateInput(FareSummaryRequested request)
    {
        if (!ValidInput(request))
            return new(0, 0, false);
        try
        {
            var payload = JsonSerializer.Serialize(new { facts = request.Facts }, Json);
            var bytes = Encoding.UTF8.GetByteCount(payload);
            var tokens =
                bytes
                + Encoding.UTF8.GetByteCount(Prompt.Value)
                + Encoding.UTF8.GetByteCount(Schema)
                + 128;
            return new(bytes, tokens, bytes <= 16 * 1024 && tokens <= MaxInputTokens);
        }
        catch (Exception)
        {
            return new(0, 0, false);
        }
    }

    public Task<FareSummaryPlanned> ExtractAsync(
        FareSummaryRequested request,
        CancellationToken cancellationToken = default
    ) => ProcessWithCompletionAsync(request, result => Task.FromResult(result), cancellationToken);

    public void DeferCompletionUntil(Task pending) => admission.DeferCompletion(pending);

    public async Task<FareSummaryPlanned> ProcessWithCompletionAsync(
        FareSummaryRequested request,
        Func<FareSummaryPlanned, Task<FareSummaryPlanned>> complete,
        CancellationToken cancellationToken = default
    )
    {
        if (!admission.TryHoldRequest())
            return await complete(Reply(request, "Busy"));
        try
        {
            return await complete(await ExtractCoreAsync(request, cancellationToken));
        }
        finally
        {
            admission.ReleaseRequest();
        }
    }

    private async Task<FareSummaryPlanned> ExtractCoreAsync(
        FareSummaryRequested request,
        CancellationToken cancellationToken
    )
    {
        var reply = Reply(request, "Disabled");
        if (!options.Enabled)
            return reply;
        if (cancellationToken.IsCancellationRequested || request.Deadline <= time.GetUtcNow())
            return reply with { Status = "DeadlineExceeded" };
        if (!ValidInput(request))
            return reply with { Status = "InputUnavailable" };
        if (
            string.IsNullOrWhiteSpace(options.Model)
            || options.Model.Length > 128
            || options.PricingModel != options.Model
            || !options.PricingVerified
            || options.InputUsdPerMillion <= 0
            || options.OutputUsdPerMillion <= 0
        )
            return reply with { Status = "ModelUnavailable" };
        string payload;
        string system;
        try
        {
            payload = JsonSerializer.Serialize(new { facts = request.Facts }, Json);
            system = Prompt.Value;
        }
        catch (Exception)
        {
            return reply with { Status = "InputUnavailable" };
        }
        // UTF-8 bytes (rather than bytes/4) plus framing reserve are deliberately restrictive.
        // This gate is not a provider billing guarantee; model enablement requires measured verification.
        var inputUpper =
            Encoding.UTF8.GetByteCount(payload)
            + Encoding.UTF8.GetByteCount(system)
            + Encoding.UTF8.GetByteCount(Schema)
            + 128;
        if (Encoding.UTF8.GetByteCount(payload) > 16 * 1024 || inputUpper > MaxInputTokens)
            return reply with { Status = "InputUnavailable" };
        decimal upperCost;
        try
        {
            upperCost =
                (
                    inputUpper * options.InputUsdPerMillion
                    + MaxOutputTokens * options.OutputUsdPerMillion
                ) / 1_000_000m;
        }
        catch (OverflowException)
        {
            return reply with { Status = "BudgetExceeded" };
        }
        if (upperCost > MaxCostUsd)
            return reply with { Status = "BudgetExceeded" };
        if (!admission.TryEnter())
            return reply with { Status = "Busy" };
        Task<ChatResponse>? inference = null;
        try
        {
            var remaining = request.Deadline - time.GetUtcNow();
            if (cancellationToken.IsCancellationRequested || remaining <= TimeSpan.Zero)
                return reply with { Status = "DeadlineExceeded" };
            var inferenceBudget =
                remaining < TimeSpan.FromMilliseconds(3500)
                    ? remaining
                    : TimeSpan.FromMilliseconds(3500);
            using var timeout = new CancellationTokenSource(inferenceBudget, time);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                timeout.Token
            );
            ChatResponse response;
            try
            {
                using var schema = JsonDocument.Parse(Schema);
                inference = chat.GetResponseAsync(
                    [new(ChatRole.System, system), new(ChatRole.User, payload)],
                    new ChatOptions
                    {
                        ModelId = options.Model,
                        Temperature = 0,
                        MaxOutputTokens = MaxOutputTokens,
                        ResponseFormat = ChatResponseFormat.ForJsonSchema(
                            schema.RootElement.Clone()
                        ),
                    },
                    linked.Token
                );
                response = await inference.WaitAsync(inferenceBudget, time, linked.Token);
            }
            catch (Exception error) when (error is OperationCanceledException or TimeoutException)
            {
                if (inference is not null && !inference.IsCompleted)
                    admission.DeferCompletion(inference);
                return reply with { Status = "DeadlineExceeded" };
            }
            catch (Exception)
            {
                return reply with { Status = "InferenceFailed" };
            }
            var input = TokenCount(response.Usage?.InputTokenCount);
            var output = TokenCount(response.Usage?.OutputTokenCount);
            reply = reply with
            {
                ModelId = response.ModelId,
                InputTokens = input,
                OutputTokens = output,
            };
            if (input is null || output is null)
                return reply with { Status = "UsageUnknown" };
            if (response.ModelId != options.Model)
                return reply with { Status = "ModelUnavailable" };
            decimal cost;
            try
            {
                cost =
                    (
                        input.Value * options.InputUsdPerMillion
                        + output.Value * options.OutputUsdPerMillion
                    ) / 1_000_000m;
            }
            catch (OverflowException)
            {
                return reply with { Status = "UsageUnknown" };
            }
            reply = reply with { CostUsd = cost };
            if (
                time.GetUtcNow() >= request.Deadline
                || timeout.IsCancellationRequested
                || cancellationToken.IsCancellationRequested
            )
                return reply with { Status = "DeadlineExceeded" };
            if (cost > MaxCostUsd || output > MaxOutputTokens || input > MaxInputTokens)
                return reply with { Status = "BudgetExceeded" };
            var sections = response
                .Messages.SelectMany(m => m.Contents)
                .Any(c => c is not TextContent)
                ? null
                : Parse(response.Text, request.Facts);
            return sections is null
                ? reply with
                {
                    Status = "InvalidOutput",
                }
                : reply with
                {
                    Status = "Ready",
                    Sections = sections,
                };
        }
        finally
        {
            admission.Exit();
        }
    }

    private static int? TokenCount(long? count) =>
        count is >= 0 and <= int.MaxValue ? (int)count.Value : null;

    private static FareSummaryPlanned Reply(FareSummaryRequested r, string status) =>
        new(r.CorrelationId, r.Fingerprint, r.PromptVersion, r.Locale, status);

    private static bool ValidInput(FareSummaryRequested r)
    {
        if (
            r.CorrelationId == Guid.Empty
            || r.Fingerprint is null
            || !Regex.IsMatch(r.Fingerprint, "^[a-f0-9]{64}$", RegexOptions.CultureInvariant)
            || r.PromptVersion != FareSummaryMessageIdentity.PromptVersion
            || r.Locale != "ru"
            || r.Facts is null
            || r.Facts.Length is 0 or > 64
        )
            return false;
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var useful = false;
        foreach (var f in r.Facts)
        {
            if (
                f is null
                || f.Id is null
                || !Regex.IsMatch(f.Id, "^f[0-9]{1,4}$", RegexOptions.CultureInvariant)
                || !ids.Add(f.Id)
                || f.Scopes is null
                || f.Scopes.Length is 0 or > 576
            )
                return false;
            var baggage = f.Kind is "checked-baggage" or "carry-on-baggage";
            if (
                baggage
                    ? f.Section != "baggage" || f.State is not ("Unknown" or "Known")
                    : f.Kind is not ("change" or "refund")
                        || f.Section != f.Kind
                        || f.State is not ("Unknown" or "Allowed" or "NotAllowed")
            )
                return false;
            if (
                baggage
                    ? f.Amount is not null
                        || f.Currency is not null
                        || (f.State == "Known" ? f.Quantity is null or < 0 : f.Quantity is not null)
                    : f.Quantity is not null
            )
                return false;
            if ((f.Amount is null) != (f.Currency is null))
                return false;
            if (
                f.Amount is not null
                && (
                    f.Amount.Length > 40
                    || !Regex.IsMatch(
                        f.Amount,
                        "^[0-9]+(?:\\.[0-9]+)?$",
                        RegexOptions.CultureInvariant
                    )
                    || !decimal.TryParse(
                        f.Amount,
                        NumberStyles.AllowDecimalPoint,
                        CultureInfo.InvariantCulture,
                        out var amount
                    )
                    || amount < 0
                    || !Regex.IsMatch(f.Currency!, "^[A-Z]{3}$", RegexOptions.CultureInvariant)
                )
            )
                return false;
            foreach (var s in f.Scopes)
            {
                if (s is null)
                    return false;
                if (
                    s.Kind switch
                    {
                        "offer" => s.Leg is not null
                            || s.Segment is not null
                            || s.Adult is not null,
                        "leg" => s.Leg is not (>= 0 and <= 3)
                            || s.Segment is not null
                            || s.Adult is not null,
                        "passenger-segment" => s.Leg is not (>= 0 and <= 3)
                            || s.Segment is not (>= 0 and <= 63)
                            || s.Adult is not (>= 0 and <= 8),
                        _ => true,
                    }
                )
                    return false;
            }
            if (f.Scopes.Select(s => s.Kind).Distinct(StringComparer.Ordinal).Count() != 1)
                return false;
            useful |= f.State != "Unknown" || f.Amount is not null;
        }
        return useful;
    }

    private static FareSummaryPlanSection[]? Parse(string? text, FareSummaryFact[] facts)
    {
        if (text is null || Encoding.UTF8.GetByteCount(text) > 16 * 1024)
            return null;
        try
        {
            using var doc = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 8 });
            if (
                !Exact(doc.RootElement, "sections")
                || doc.RootElement.GetProperty("sections").ValueKind != JsonValueKind.Array
            )
                return null;
            var lookup = facts.ToDictionary(f => f.Id, StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var kinds = new HashSet<string>(StringComparer.Ordinal);
            var sections = new List<FareSummaryPlanSection>();
            var count = 0;
            foreach (var section in doc.RootElement.GetProperty("sections").EnumerateArray())
            {
                if (
                    !Exact(section, "kind", "groups")
                    || section.GetProperty("kind").ValueKind != JsonValueKind.String
                    || section.GetProperty("groups").ValueKind != JsonValueKind.Array
                )
                    return null;
                var kind = section.GetProperty("kind").GetString()!;
                if (kind is not ("change" or "refund" or "baggage") || !kinds.Add(kind))
                    return null;
                var groups = new List<FareSummaryPlanGroup>();
                foreach (var group in section.GetProperty("groups").EnumerateArray())
                {
                    if (
                        ++count > 64
                        || !Exact(group, "factIds")
                        || group.GetProperty("factIds").ValueKind != JsonValueKind.Array
                    )
                        return null;
                    var ids = new List<string>();
                    FareSummaryFact? first = null;
                    foreach (var id in group.GetProperty("factIds").EnumerateArray())
                    {
                        if (
                            id.ValueKind != JsonValueKind.String
                            || !lookup.TryGetValue(id.GetString()!, out var fact)
                            || !seen.Add(fact.Id)
                            || fact.Section != kind
                        )
                            return null;
                        if (
                            first is not null
                            && (
                                first.Kind != fact.Kind
                                || first.State != fact.State
                                || first.Amount != fact.Amount
                                || first.Currency != fact.Currency
                                || first.Quantity != fact.Quantity
                                || first.Scopes[0].Kind != fact.Scopes[0].Kind
                            )
                        )
                            return null;
                        first = fact;
                        ids.Add(fact.Id);
                    }
                    if (ids.Count == 0)
                        return null;
                    groups.Add(new(ids.ToArray()));
                }
                if (groups.Count == 0)
                    return null;
                sections.Add(new(kind, groups.ToArray()));
            }
            return seen.Count == facts.Length ? sections.ToArray() : null;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static bool Exact(JsonElement e, params string[] names) =>
        e.ValueKind == JsonValueKind.Object
        && e.EnumerateObject()
            .Select(p => p.Name)
            .Order(StringComparer.Ordinal)
            .SequenceEqual(names.Order(StringComparer.Ordinal), StringComparer.Ordinal);
}
