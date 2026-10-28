using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Travel.AI.TripsBriefExtraction;
using Travel.IntegrationContracts.AI.TripsBriefExtraction;
using Travel.Modules.Trips.Application.BriefExtraction;

namespace Travel.Tests.AiEvals.TripsBriefExtraction;

// Never an xUnit test, never discovers credentials or constructs a provider client.
public interface ITripsBriefEvaluationPipeline
{
    string ModelId { get; }
    Task<TripsBriefParsed> ExtractAsync(
        TripsBriefRequested request,
        CancellationToken cancellationToken
    );
}

public sealed class ProductionTripsBriefEvaluationPipeline(
    TripsBriefExtractor extractor,
    ITripsBriefLedger ledger,
    TripsBriefOptions options
) : ITripsBriefEvaluationPipeline
{
    public string ModelId => options.Model;

    public Task<TripsBriefParsed> ExtractAsync(
        TripsBriefRequested request,
        CancellationToken cancellationToken
    ) => extractor.ExtractAsync(request, ledger, cancellationToken);
}

public sealed record TripsBriefEvaluationBinding(
    Guid RunId,
    string AuthorizationId,
    bool Authorized,
    string PacketSha256,
    string SourceTreeSha256,
    string PromptSha256,
    string Model,
    decimal InputUsdPerMillion,
    decimal OutputUsdPerMillion,
    decimal MaxTotalCostUsd,
    int MaxCalls,
    string? HoldoutAttestationId = null,
    string? RunReceiptDirectory = null,
    string[]? ApprovedCaseIds = null,
    decimal MaxRequestCostUsd = .03m,
    decimal MaxHourlyCostUsd = .30m
);

public sealed record TripsBriefEvalCaseReceipt(
    string Id,
    string Partition,
    string Status,
    bool Dispatched,
    bool Exact,
    bool Useful,
    bool Unsafe,
    double ComponentMilliseconds,
    int? InputTokens,
    int? OutputTokens,
    decimal? CostUsd,
    TripsBriefFields? Fields = null,
    TripsBriefIssue[]? Issues = null,
    TripsBriefEvaluationDiagnostics? Diagnostics = null
)
{
    public override string ToString() => $"{Id}:{Status}";
}

public sealed record TripsBriefEvaluationReceipt(
    TripsBriefEvaluationBinding Binding,
    TripsBriefEvalCaseReceipt[] Cases,
    string? StopReason,
    int Dispatched,
    int QualificationExact,
    int UsefulExact,
    int UnsafeCases,
    double ComponentP95Milliseconds,
    decimal? ActualSpendUsd,
    TimeSpan Duration,
    bool Passed,
    int ActualProviderStarts = 0,
    string JournalKind = "historical-component-pipeline",
    string CompletionState = "not-finalized",
    string? JournalIndexSha256 = null
)
{
    public string EvidenceKind => JournalKind;
}

public static class TripsBriefEvaluationRunner
{
    public static string EmbeddedPromptSha256()
    {
        using var stream =
            typeof(TripsBriefExtractor).Assembly.GetManifestResourceStream(
                "prompts.v1.trips.brief-extraction.system.md"
            ) ?? throw new InvalidOperationException("Prompt identity unavailable.");
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    public static async Task<TripsBriefEvaluationReceipt> RunAuthorizedAsync(
        string packetPath,
        TripsBriefEvaluationBinding binding,
        ITripsBriefEvaluationPipeline pipeline,
        TimeProvider time,
        ITripsBriefEvaluationJournal journal,
        TripsBriefEvaluationSchedule schedule,
        Func<TimeSpan, CancellationToken, Task>? wait = null,
        Func<bool>? stopRequested = null,
        CancellationToken cancellationToken = default
    )
    {
        if (
            binding is null
            || !binding.Authorized
            || binding.RunId == Guid.Empty
            || (
                string.IsNullOrWhiteSpace(binding.AuthorizationId)
                || binding.AuthorizationId.Length > 128
            )
            || !Digest(binding.SourceTreeSha256)
            || !Digest(binding.PacketSha256)
            || binding.PromptSha256 != EmbeddedPromptSha256()
            || string.IsNullOrWhiteSpace(binding.Model)
            || binding.Model.Length > 128
            || pipeline.ModelId != binding.Model
            || binding.InputUsdPerMillion <= 0
            || binding.OutputUsdPerMillion <= 0
            || binding.MaxRequestCostUsd is <= 0 or > .03m
            || binding.MaxHourlyCostUsd is <= 0 or > .30m
            || binding.MaxHourlyCostUsd < binding.MaxRequestCostUsd
            || binding.MaxTotalCostUsd <= 0
            || binding.MaxCalls is < 1 or > 48
        )
            throw new InvalidOperationException(
                "Separate explicit evaluation authorization and exact source/prompt/model/price binding are required."
            );
        var packet = TripsBriefEvaluationPacket.Load(packetPath);
        if (binding.PacketSha256 != TripsBriefEvaluationPacket.HashFile(packetPath))
            throw new InvalidOperationException("Corpus hash changed.");
        var holdout = packet.Kind == "sealed-fictional-holdout";
        if (
            binding.MaxCalls > packet.Cases.Length
            || holdout && string.IsNullOrWhiteSpace(binding.HoldoutAttestationId)
        )
            throw new InvalidOperationException(
                "Independent custody binding is required for sealed evaluation."
            );
        if (
            binding.RunReceiptDirectory is null
            || !journal.HasStableReceiptRoot(binding.RunReceiptDirectory)
            || binding.ApprovedCaseIds is { Length: 0 }
            || binding.ApprovedCaseIds is not null
                && (
                    binding.ApprovedCaseIds.Length > binding.MaxCalls
                    || binding.ApprovedCaseIds.Distinct(StringComparer.Ordinal).Count()
                        != binding.ApprovedCaseIds.Length
                    || binding.ApprovedCaseIds.Any(id => !packet.Cases.Any(c => c.Id == id))
                )
        )
            throw new InvalidOperationException(
                "An approved stable receipt directory and exact optional remaining case IDs are required."
            );
        journal.BeginRun(binding, time);
        wait ??= (duration, ct) => Task.Delay(duration, time, ct);
        var runLimit = TimeSpan.FromHours(holdout ? 3 : 6);
        schedule.Begin(runLimit);
        var policy = TripsBriefPolicyIdentity.Compute(
            "anthropic",
            binding.Model,
            TripsBriefMessageIdentity.PrivacyNoticeVersion
        );
        var results = new List<TripsBriefEvalCaseReceipt>();
        var reservations = new List<(TimeSpan At, decimal Cost)>();
        decimal spent = 0;
        var unknownCost = false;
        string? stop = null;
        var dispatched = 0;

        for (var i = 0; i < packet.Cases.Length; i++)
        {
            var c = packet.Cases[i];
            if (
                binding.ApprovedCaseIds is not null
                && !binding.ApprovedCaseIds.Contains(c.Id, StringComparer.Ordinal)
            )
            {
                results.Add(NotRun(c));
                continue;
            }
            if (
                stop is null
                && (cancellationToken.IsCancellationRequested || stopRequested?.Invoke() == true)
            )
                stop = "StopRequested";
            if (stop is null && dispatched >= binding.MaxCalls)
                stop = "AuthorizedCallLimit";
            if (
                stop is null
                && (!schedule.HasBudget(TimeSpan.Zero) || pipeline.ModelId != binding.Model)
            )
                stop = "RunBindingOrDeadlineChanged";
            var delay = schedule.DelayBeforeCase(i);
            if (stop is null && !schedule.HasBudget(delay + TimeSpan.FromSeconds(5)))
                stop = "StopRequestedOrRunDeadline";
            if (stop is null && delay > TimeSpan.Zero)
            {
                try
                {
                    await wait(delay, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    stop = "StopRequested";
                }
            }
            if (
                stop is null
                && (
                    cancellationToken.IsCancellationRequested
                    || stopRequested?.Invoke() == true
                    || !schedule.HasBudget(TimeSpan.FromSeconds(5))
                )
            )
                stop = "StopRequestedOrRunDeadline";
            if (
                stop is null
                && (
                    schedule.DelayBeforeCase(i) > TimeSpan.Zero || pipeline.ModelId != binding.Model
                )
            )
                stop = "RunBindingOrScheduleChanged";
            if (stop is not null)
            {
                results.Add(NotRun(c));
                continue;
            }

            var input = TripsBriefEvaluationPacket.Input(c, time.GetUtcNow()) with
            {
                ProcessingPolicyVersion = policy,
            };
            var request = new TripsBriefRequested(
                input.RequestId,
                policy,
                c.Text,
                "ru",
                c.ReferenceInstant,
                c.ReferenceZone,
                input.ReferenceLocalDate,
                input.Deadline
            );
            var estimate = TripsBriefExtractor.EstimateInput(request, binding.Model);
            decimal reserve;
            try
            {
                reserve = checked(
                    (
                        estimate.EstimatedInputTokens * binding.InputUsdPerMillion
                        + TripsBriefExtractor.MaxOutputTokens * binding.OutputUsdPerMillion
                    ) / 1_000_000m
                );
            }
            catch (OverflowException)
            {
                stop = "BudgetExceeded";
                results.Add(NotRun(c));
                continue;
            }
            reservations.RemoveAll(x => schedule.Elapsed - x.At >= TimeSpan.FromHours(1));
            if (
                !estimate.WithinBounds
                || reserve > binding.MaxRequestCostUsd
                || reserve <= 0
                || spent + reserve > binding.MaxTotalCostUsd
                || reservations.Sum(x => x.Cost) + reserve > binding.MaxHourlyCostUsd
            )
            {
                stop = "AdmissionUnavailable";
                results.Add(NotRun(c));
                continue;
            }

            try
            {
                await journal.RecordAttemptAsync(
                    new(
                        binding.RunId,
                        c.Id,
                        input.RequestId,
                        journal.ManifestSha256,
                        reserve,
                        time.GetUtcNow()
                    ),
                    cancellationToken
                );
            }
            catch (Exception e)
                when (e is IOException or InvalidOperationException or OperationCanceledException)
            {
                stop = "JournalUnavailable";
                results.Add(NotRun(c));
                continue;
            }
            if (
                !schedule.HasBudget(TimeSpan.FromSeconds(5))
                || cancellationToken.IsCancellationRequested
            )
            {
                stop = "StopRequestedOrRunDeadline";
                results.Add(NotRun(c));
                continue;
            }
            input = input with { Deadline = time.GetUtcNow().AddSeconds(5) };
            request = request with { Deadline = input.Deadline };
            var reservationIndex = reservations.Count;
            reservations.Add((schedule.Elapsed, reserve));
            dispatched++;
            var processed = await new TripsBriefEvaluationCaseProcessor(binding, time).ProcessAsync(
                c,
                input,
                pipeline,
                cancellationToken
            );
            if (processed.CostUsd is null)
            {
                unknownCost = true;
                stop = "UnknownAccounting";
            }
            else
            {
                spent += processed.CostUsd.Value;
                reservations[reservationIndex] = (schedule.Elapsed, processed.CostUsd.Value);
            }
            results.Add(processed);
            try
            {
                await journal.RecordResultAsync(results[^1], CancellationToken.None);
            }
            catch (Exception e) when (e is IOException or InvalidOperationException)
            {
                stop = "JournalUnavailable";
            }
        }
        var actual = results.ToArray();
        var qualification = actual.Count(x => x.Partition != "development" && x.Exact);
        var useful = actual.Count(x => x.Useful);
        var unsafeCases = actual.Count(x => x.Unsafe);
        var durations = actual
            .Where(x => x.Dispatched)
            .Select(x => x.ComponentMilliseconds)
            .Order()
            .ToArray();
        var p95 =
            durations.Length == 0 ? 0 : durations[(int)Math.Ceiling(durations.Length * .95) - 1];
        var duration = schedule.Elapsed;
        if (!schedule.HasBudget(TimeSpan.Zero))
            stop ??= "RunDeadlineDuringFinalization";
        if (cancellationToken.IsCancellationRequested || stopRequested?.Invoke() == true)
            stop ??= "StopRequested";
        var receipt = new TripsBriefEvaluationReceipt(
            binding,
            actual,
            stop,
            dispatched,
            qualification,
            useful,
            unsafeCases,
            p95,
            unknownCost ? null : spent,
            duration,
            false,
            schedule.ProviderStarts,
            journal.EvidenceKind,
            "FinalizationPending"
        );
        await journal.CompleteAsync(receipt, CancellationToken.None);
        // Immutable evidence cannot know its own flush duration in advance. Never persist a stale pass.
        duration = schedule.Elapsed;
        if (!schedule.HasBudget(TimeSpan.Zero))
            stop ??= "RunDeadlineDuringFinalization";
        if (cancellationToken.IsCancellationRequested || stopRequested?.Invoke() == true)
            stop ??= "StopRequested";
        return receipt with
        {
            Duration = duration,
            StopReason = stop,
            Passed = TripsBriefEvaluationCaseProcessor.Accepted(
                actual,
                stop,
                unknownCost,
                duration,
                schedule.EffectiveLimit
            ),
            CompletionState = "PostFlushVerdict",
            JournalIndexSha256 = journal.CompletedIndexSha256,
        };
    }

    internal static void ClaimAuthorization(TripsBriefEvaluationBinding binding, TimeProvider time)
    {
        // One small metadata-only claim, kept on success/failure/crash. It is never automatically removed.
        var directory = TripsBriefEvaluationFileJournal.CheckRoot(binding.RunReceiptDirectory!);
        Directory.CreateDirectory(directory);
        var id = Convert.ToHexStringLower(
            SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(binding.AuthorizationId))
        );
        var path = Path.Combine(directory, "trips-brief-authorization-" + id + ".claim");
        if (File.Exists(Path.Combine(directory, "trips-brief-authorization-" + id + ".json")))
            throw new InvalidOperationException("LegacyAuthorityConsumed");
        try
        {
            using var file = new FileStream(
                path,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.WriteThrough
            );
            var metadata = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(
                new
                {
                    schemaVersion = 1,
                    binding.RunId,
                    authorizationReferenceSha256 = id,
                    binding.PacketSha256,
                    binding.SourceTreeSha256,
                    binding.PromptSha256,
                    binding.Model,
                    binding.MaxTotalCostUsd,
                    binding.MaxCalls,
                    binding.ApprovedCaseIds,
                    claimedAtUtc = time.GetUtcNow(),
                }
            );
            file.Write(metadata);
            file.Flush(true);
        }
        catch (IOException)
        {
            throw new InvalidOperationException(
                "Evaluation authorization was consumed or its durable receipt is unavailable; no automatic resume."
            );
        }
    }

    private static TripsBriefEvalCaseReceipt NotRun(TripsBriefEvalCase c) =>
        new(c.Id, c.Partition, "NotRun", false, false, false, false, 0, null, null, null);

    private static bool Digest(string? value) =>
        value is not null
        && Regex.IsMatch(
            value,
            "^[a-f0-9]{64}$",
            RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(20)
        );
}
