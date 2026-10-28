using ErrorOr;
using Travel.AI.TripsBriefExtraction;
using Travel.IntegrationContracts.AI.TripsBriefExtraction;
using Travel.Modules.Trips.Application.BriefExtraction;

namespace Travel.Tests.AiEvals.TripsBriefExtraction;

public sealed class TripsBriefEvaluationCaseProcessor(
    TripsBriefEvaluationBinding binding,
    TimeProvider time,
    Func<TripBriefExtractionInput, TripsBriefParsed, ErrorOr<TripBriefCandidate>>? validator = null
)
{
    public async Task<TripsBriefEvalCaseReceipt> ProcessAsync(
        TripsBriefEvalCase c,
        TripBriefExtractionInput input,
        ITripsBriefEvaluationPipeline pipeline,
        CancellationToken ct
    )
    {
        var stamp = time.GetTimestamp();
        var request = new TripsBriefRequested(
            input.RequestId,
            input.ProcessingPolicyVersion,
            input.Text,
            input.Locale,
            input.ReferenceInstant,
            input.ReferenceZone,
            input.ReferenceLocalDate,
            input.Deadline
        );
        TripsBriefParsed? reply = null;
        Task<TripsBriefParsed>? pending = null;
        var status = "TransportOrDeadlineFailure";
        try
        {
            pending = pipeline.ExtractAsync(request, ct);
            reply = await pending.WaitAsync(TimeSpan.FromSeconds(5), time, ct);
            status = ControlledStatus(reply.Status);
        }
        catch (Exception)
        {
            if (pending is not null)
                _ = pending.ContinueWith(
                    t =>
                    {
                        _ = t.Exception;
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted
                        | TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default
                );
        }
        decimal? cost = null;
        try
        {
            if (
                reply is { InputTokens: >= 0 and <= 4096, OutputTokens: >= 0 and <= 1024 }
                && reply.ModelId == binding.Model
                && reply.CorrelationId == input.RequestId
                && reply.ProcessingPolicyVersion == input.ProcessingPolicyVersion
            )
            {
                var calculated = checked(
                    (
                        reply.InputTokens.Value * binding.InputUsdPerMillion
                        + reply.OutputTokens.Value * binding.OutputUsdPerMillion
                    ) / 1_000_000m
                );
                var estimate = TripsBriefExtractor.EstimateInput(request, binding.Model);
                var reserve = checked(
                    (
                        estimate.EstimatedInputTokens * binding.InputUsdPerMillion
                        + 1024 * binding.OutputUsdPerMillion
                    ) / 1_000_000m
                );
                if (
                    reply.CostUsd == calculated
                    && calculated <= reserve
                    && calculated <= binding.MaxRequestCostUsd
                )
                    cost = calculated;
            }
        }
        catch (OverflowException) { }
        if (cost is null)
            status = "UnknownAccounting";
        TripBriefCandidate? candidate = null;
        var observed = reply is { Status: "Ready", Fields: not null, Issues: not null };
        var rejected = false;
        string[] added = [],
            derived = [];
        if (
            reply is not null
            && cost is not null
            && time.GetElapsedTime(stamp).TotalMilliseconds <= 5000
            && time.GetUtcNow() < input.Deadline
            && reply.Status == "Ready"
        )
        {
            try
            {
                var result = (validator ?? TripBriefCandidateValidator.Validate)(input, reply);
                if (result.IsError)
                {
                    rejected = true;
                    status = "InvalidOutput";
                }
                else
                {
                    candidate = result.Value;
                    added = candidate
                        .Issues.Select(x => x.Code)
                        .Except(reply.Issues!.Select(x => x.Code), StringComparer.Ordinal)
                        .ToArray();
                    var before = TripsBriefEvaluationPacket.Values(reply.Fields!);
                    var after = TripsBriefEvaluationPacket.Values(candidate.Fields);
                    derived = Enumerable
                        .Range(0, 7)
                        .Where(i => before[i].State == "missing" && after[i].State != "missing")
                        .Select(i => TripsBriefEvaluationPacket.Slots[i])
                        .ToArray();
                }
            }
            catch (Exception)
            {
                candidate = null;
                rejected = true;
                status = "InvalidOutput";
            }
        }
        var milliseconds = time.GetElapsedTime(stamp).TotalMilliseconds;
        if (
            milliseconds >= 5000
            || time.GetUtcNow() >= input.Deadline
            || ct.IsCancellationRequested
        )
        {
            candidate = null;
            // Preserve unknown accounting rather than replacing it with a timing label.
            if (cost is not null)
                status = "ComponentDeadlineFailure";
        }
        var exact = candidate is not null && TripsBriefEvaluationPacket.Matches(c, candidate);
        return new(
            c.Id,
            c.Partition,
            status,
            true,
            exact,
            c.Useful && exact,
            candidate is not null && TripsBriefEvaluationPacket.Unsafe(c, candidate),
            milliseconds,
            reply?.InputTokens,
            reply?.OutputTokens,
            cost,
            candidate?.Fields,
            candidate?.Issues,
            new(
                observed ? "structured-proposal-observed" : "not-assessed",
                rejected,
                added,
                derived
            )
        );
    }

    public static bool Accepted(
        TripsBriefEvalCaseReceipt[] cases,
        string? stop,
        bool unknownCost,
        TimeSpan duration,
        TimeSpan runLimit
    ) =>
        stop is null
        && !unknownCost
        && cases.All(x => x.Dispatched)
        && cases.Count(x => x.Partition != "development" && x.Exact) >= 22
        && cases.Count(x => x.Useful) >= 11
        && cases.All(x => !x.Unsafe && x.ComponentMilliseconds <= 5000)
        && duration <= runLimit;

    public static string ControlledStatus(string status) =>
        status
            is "Ready"
                or "Disabled"
                or "ModelUnavailable"
                or "ProcessingPolicyChanged"
                or "DeadlineExceeded"
                or "InputUnavailable"
                or "BudgetExceeded"
                or "Busy"
                or "AccountingUnavailable"
                or "InferenceFailed"
                or "Refused"
                or "InvalidOutput"
            ? status
            : "InvalidOutput";
}
