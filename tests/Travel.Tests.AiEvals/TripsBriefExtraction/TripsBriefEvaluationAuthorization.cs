using System.Text.Json;
using System.Text.Json.Serialization;
using Travel.AI.TripsBriefExtraction;
using Travel.IntegrationContracts.AI.TripsBriefExtraction;

namespace Travel.Tests.AiEvals.TripsBriefExtraction;

// Capture BEFORE inspection/UTC validation. Setup and pauses consume the same authority window.
public sealed class TripsBriefEvaluationAuthorizationClock
{
    private readonly TimeProvider time;
    private readonly long stamp;
    private readonly DateTimeOffset utc;

    public TripsBriefEvaluationAuthorizationClock(TimeProvider time)
    {
        this.time = time;
        stamp = time.GetTimestamp();
        utc = time.GetUtcNow();
    }

    public DateTimeOffset EffectiveUtcNow => utc + time.GetElapsedTime(stamp);

    public TimeSpan Remaining(DateTimeOffset expiresAt) => expiresAt - EffectiveUtcNow;
}

public sealed record TripsBriefEvaluationArtifacts(
    string Commit,
    string SourceTree,
    string Packet,
    string Prompt,
    string Schema,
    string Normalizer,
    string Lexicon,
    string Packages,
    string BuildClosure,
    bool Clean,
    string WireSchema = "",
    string ResourcePolicy = "",
    string WorkerClosure = "",
    string WorkerImage = ""
);

public sealed record TripsBriefEvaluationOptions(
    string ProcessorId,
    string Model,
    string PricingModel,
    decimal InputUsdPerMillion,
    decimal OutputUsdPerMillion,
    decimal MaxRequestCostUsd,
    decimal MaxHourlyCostUsd,
    string Policy
)
{
    public TripsBriefOptions ToRuntime() =>
        new()
        {
            Enabled = true,
            ExecutionAuthorized = true,
            PrivateTextProcessingAuthorized = true,
            PricingVerified = true,
            ProcessorId = ProcessorId,
            Model = Model,
            PricingModel = PricingModel,
            InputUsdPerMillion = InputUsdPerMillion,
            OutputUsdPerMillion = OutputUsdPerMillion,
            MaxRequestCostUsd = MaxRequestCostUsd,
            MaxHourlyCostUsd = MaxHourlyCostUsd,
        };
}

public sealed record TripsBriefEvaluationManifest(
    int ProtocolVersion,
    TripsBriefEvaluationBinding Binding,
    TripsBriefEvaluationArtifacts ExpectedArtifacts,
    TripsBriefEvaluationOptions Options,
    string Endpoint,
    bool FictionalOnly,
    DateTimeOffset AuthorizationExpiresAtUtc
);

public sealed record TripsBriefEvaluationPreflight(
    bool CanConstructExecutionResources,
    string[] Reasons,
    TimeSpan AuthorizationRemaining,
    bool RuntimeBudgetVerified = false
)
{
    public bool ReadyForExecution => CanConstructExecutionResources && RuntimeBudgetVerified;
}

public static class TripsBriefEvaluationAuthorization
{
    public static TripsBriefEvaluationManifest Parse(string json)
    {
        if (System.Text.Encoding.UTF8.GetByteCount(json) > 64 * 1024)
            throw new JsonException("ManifestTooLarge");
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 12 });
        CheckKeys(doc.RootElement);
        return JsonSerializer.Deserialize<TripsBriefEvaluationManifest>(
                json,
                new JsonSerializerOptions
                {
                    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
                    RespectRequiredConstructorParameters = true,
                    RespectNullableAnnotations = true,
                }
            ) ?? throw new JsonException("ManifestMissing");
    }

    private static void CheckKeys(JsonElement e)
    {
        if (e.ValueKind == JsonValueKind.Object)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in e.EnumerateObject())
            {
                if (!seen.Add(p.Name))
                    throw new JsonException("DuplicateManifestMember");
                CheckKeys(p.Value);
            }
        }
        else if (e.ValueKind == JsonValueKind.Array)
            foreach (var v in e.EnumerateArray())
                CheckKeys(v);
    }

    public static TripsBriefEvaluationPreflight Check(
        TripsBriefEvaluationManifest manifest,
        TripsBriefEvaluationArtifacts observed
    ) => Check(manifest, observed, TimeProvider.System.GetUtcNow());

    public static TripsBriefEvaluationPreflight Check(
        TripsBriefEvaluationManifest m,
        TripsBriefEvaluationArtifacts observed,
        DateTimeOffset now
    )
    {
        return CheckBinding(m, observed, now);
    }

    internal static TripsBriefEvaluationPreflight CheckBinding(
        TripsBriefEvaluationManifest m,
        TripsBriefEvaluationArtifacts observed,
        DateTimeOffset now
    )
    {
        var errors = new List<string>();
        if (m.ProtocolVersion != 1 || !m.FictionalOnly || m.Endpoint != "https://api.anthropic.com")
            errors.Add("ProtocolOrProcessorNotApproved");
        var b = m.Binding;
        var o = m.Options;
        if (b is null || o is null || m.ExpectedArtifacts is null)
            return new(false, ["ManifestIncomplete"], TimeSpan.Zero);
        if (!b.Authorized || b.RunId == Guid.Empty || !PublicId(b.AuthorizationId))
            errors.Add("SeparateAuthorizationRequired");
        if (
            b.ApprovedCaseIds is { } ids
            && (
                ids.Length == 0
                || ids.Length > b.MaxCalls
                || ids.Distinct(StringComparer.Ordinal).Count() != ids.Length
                || ids.Any(id =>
                    id is null
                    || id.Length != 3
                    || id[0] is not ('D' or 'H')
                    || id[1] is < '0' or > '9'
                    || id[2] is < '0' or > '9'
                    || !int.TryParse(id.AsSpan(1), out var n)
                    || n is < 1 or > 24
                )
            )
        )
            errors.Add("CaseScopeNotApproved");
        if (
            !PublicId(b.Model)
            || o.ProcessorId != "anthropic"
            || o.Model != b.Model
            || o.PricingModel != b.Model
        )
            errors.Add("ModelSelectionPendingOrChanged");
        if (
            !o.ToRuntime().IsConfigured
            || o.InputUsdPerMillion != b.InputUsdPerMillion
            || o.OutputUsdPerMillion != b.OutputUsdPerMillion
            || o.MaxRequestCostUsd != b.MaxRequestCostUsd
            || o.MaxHourlyCostUsd != b.MaxHourlyCostUsd
            || o.Policy
                != TripsBriefPolicyIdentity.Compute(
                    "anthropic",
                    b.Model,
                    TripsBriefMessageIdentity.PrivacyNoticeVersion
                )
        )
            errors.Add("PriceLimitsOrPolicyChanged");
        try
        {
            var full = checked(
                (4096m * b.InputUsdPerMillion + 1024m * b.OutputUsdPerMillion) / 1_000_000m
            );
            if (
                full <= 0
                || full > b.MaxRequestCostUsd
                || b.MaxTotalCostUsd < full
                || b.MaxCalls is < 1 or > 48
            )
                errors.Add("ReservationNotCovered");
        }
        catch (OverflowException)
        {
            errors.Add("ReservationNotCovered");
        }
        if (
            !observed.Clean
            || !m.ExpectedArtifacts.Clean
            || observed != m.ExpectedArtifacts
            || !Digest(observed.Commit, 40)
            || new[]
            {
                observed.SourceTree,
                observed.Packet,
                observed.Prompt,
                observed.Schema,
                observed.Normalizer,
                observed.Lexicon,
                observed.Packages,
                observed.BuildClosure,
            }.Any(v => !Digest(v))
            || b.SourceTreeSha256 != observed.SourceTree
            || b.PacketSha256 != observed.Packet
            || b.PromptSha256 != observed.Prompt
            || observed.Prompt != TripsBriefEvaluationRunner.EmbeddedPromptSha256()
            || observed.Schema != TripsBriefExtractor.SchemaSha256()
        )
            errors.Add("ArtifactsDirtyOrChanged");
        if (observed.WireSchema != TripsBriefWireProjection.Sha256())
            errors.Add("WireProjectionChanged");
        if (
            observed.ResourcePolicy != TripsBriefEvaluationResourcePolicy.Sha256()
            || !Digest(observed.WorkerClosure)
            || !observed.WorkerImage.StartsWith("sha256:", StringComparison.Ordinal)
            || !Digest(observed.WorkerImage[7..])
        )
            errors.Add("BoundWorkerPreparationRequired");
        var remaining = m.AuthorizationExpiresAtUtc - now;
        if (remaining < TimeSpan.FromSeconds(5))
            errors.Add("AuthorizationExpired");
        if (!QualifiedRoot(b.RunReceiptDirectory))
            errors.Add("StableEvidenceRootRequired");
        return new(
            errors.Count == 0,
            errors.ToArray(),
            remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero
        );
    }

    public static bool Digest(string? value, int length = 64) =>
        value?.Length == length && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static bool QualifiedRoot(string? value) =>
        value is not null
        && (
            Path.IsPathFullyQualified(value)
            || value.Length > 3
                && char.IsAsciiLetter(value[0])
                && value[1] == ':'
                && value[2] is '\\' or '/'
        );

    private static bool PublicId(string? value) =>
        value is { Length: > 0 and <= 128 } && value.All(c => c is >= '!' and <= '~');
}
