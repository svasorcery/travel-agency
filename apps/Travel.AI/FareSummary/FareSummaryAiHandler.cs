using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using Travel.IntegrationContracts.AI.FareSummary;
using Wolverine.Attributes;

namespace Travel.AI.FareSummary;

public interface IFareSummaryLedger
{
    Task RecordAsync(FareSummaryPlanned result, CancellationToken cancellationToken);
}

public static class FareSummaryAiHandler
{
    private static readonly Meter Meter = new("Travel.AI");
    private static readonly Counter<long> Outcomes = Meter.CreateCounter<long>(
        "travel.ai.fare_summary.outcomes"
    );
    private static readonly Counter<long> Tokens = Meter.CreateCounter<long>(
        "travel.ai.fare_summary.tokens"
    );
    private static readonly Histogram<double> Duration = Meter.CreateHistogram<double>(
        "travel.ai.fare_summary.duration",
        "s"
    );

    [WolverineHandler]
    public static async Task<FareSummaryPlanned> Handle(
        FareSummaryRequested request,
        FareSummaryExtractor extractor,
        IFareSummaryLedger ledger,
        TimeProvider time,
        ILogger<FareSummaryRequested> log,
        CancellationToken cancellationToken
    )
    {
        var started = time.GetTimestamp();
        var accountingDeadline = time.GetUtcNow().AddSeconds(4);
        if (request.Deadline < accountingDeadline)
            accountingDeadline = request.Deadline;
        try
        {
            return await extractor.ProcessWithCompletionAsync(request, Complete, cancellationToken);
        }
        catch (Exception)
        {
            return new(
                request.CorrelationId,
                request.Fingerprint,
                request.PromptVersion,
                request.Locale,
                "InferenceFailed"
            );
        }

        async Task<FareSummaryPlanned> Complete(FareSummaryPlanned result)
        {
            if (
                result.InputTokens is not null
                && result.OutputTokens is not null
                && result.CostUsd is not null
                && result.ModelId is not null
            )
            {
                Task? accounting = null;
                try
                {
                    var remaining = accountingDeadline - time.GetUtcNow();

                    using var deadline = new CancellationTokenSource(
                        remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero,
                        time
                    );
                    using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                        cancellationToken,
                        deadline.Token
                    );
                    if (remaining <= TimeSpan.Zero)
                        linked.Cancel();
                    accounting = ledger.RecordAsync(result, linked.Token);
                    await accounting.WaitAsync(
                        remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero,
                        time,
                        cancellationToken
                    );
                }
                catch (Exception)
                {
                    // A dependency ignoring cancellation cannot hold the reply or permit another
                    // inference while its accounting task is still active. Never retry it.
                    if (accounting is not null && !accounting.IsCompleted)
                        extractor.DeferCompletionUntil(accounting);
                    result = result with { Status = "AccountingUnavailable", Sections = null };
                }
            }
            if (
                result.Status == "Ready"
                && (
                    time.GetUtcNow() >= accountingDeadline
                    || cancellationToken.IsCancellationRequested
                )
            )
                result = result with { Status = "DeadlineExceeded", Sections = null };
            try
            {
                Outcomes.Add(1, new KeyValuePair<string, object?>("status", result.Status));
                Duration.Record(
                    time.GetElapsedTime(started).TotalSeconds,
                    new KeyValuePair<string, object?>("status", result.Status)
                );
                if (result.InputTokens is not null)
                    Tokens.Add(
                        result.InputTokens.Value,
                        new KeyValuePair<string, object?>("type", "input")
                    );
                if (result.OutputTokens is not null)
                    Tokens.Add(
                        result.OutputTokens.Value,
                        new KeyValuePair<string, object?>("type", "output")
                    );
                log.LogDebug("Fare summary completed: {Status}", result.Status);
            }
            catch (Exception)
            {
                result = result with { Status = "AccountingUnavailable", Sections = null };
            }
            return result;
        }
    }
}
