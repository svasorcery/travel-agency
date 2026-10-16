using System.Diagnostics;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Travel.AI.Observability;
using Travel.AI.Persistence;
using Travel.AI.Persistence.Entities;
using Travel.IntegrationContracts.AI.NlSearch;
using Wolverine.Attributes;

namespace Travel.AI.NlSearch;

public static class NlSearchAiHandler
{
    [WolverineHandler]
    public static async Task<NlSearchParsed> Handle(
        NlSearchRequested req,
        IChatClient chat,
        IServiceScopeFactory scopes,
        TimeProvider time,
        AiMetrics metrics,
        ILogger<NlSearchRequested> log,
        NlSearchAdmission admission,
        CancellationToken ct
    )
    {
        log.LogDebug(
            new EventId(1000, "NlSearchHandlerStarted"),
            "NL-search handler started for correlation {CorrelationId}",
            req.CorrelationId
        );
        var bounds = admission.Options;
        using var deadline = new CancellationTokenSource(
            TimeSpan.FromMilliseconds(bounds.HandlerTimeoutMilliseconds),
            time
        );
        using var whole = CancellationTokenSource.CreateLinkedTokenSource(ct, deadline.Token);
        whole.Token.ThrowIfCancellationRequested();
        var operation = ExecuteAsync(req, chat, scopes, time, metrics, log, admission, whole.Token);
        try
        {
            return await operation.WaitAsync(whole.Token);
        }
        catch (OperationCanceledException)
        {
            // The operation owns its scope and lease until storage settles. This observer
            // neither disposes scoped resources nor reads them after the bounded reply.
            _ = operation.ContinueWith(
                done =>
                {
                    _ = done.Exception;
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default
            );
            throw new NlSearchExtractionException(
                "DeadlineExceeded",
                new NlSearchUsage(null, null, NlSearchOptions.ModelId)
            );
        }
        finally
        {
            log.LogDebug(
                new EventId(1001, "NlSearchHandlerCompleted"),
                "NL-search handler completed for correlation {CorrelationId}",
                req.CorrelationId
            );
        }
    }

    private static async Task<NlSearchParsed> ExecuteAsync(
        NlSearchRequested req,
        IChatClient chat,
        IServiceScopeFactory scopes,
        TimeProvider time,
        AiMetrics metrics,
        ILogger<NlSearchRequested> log,
        NlSearchAdmission admission,
        CancellationToken ct
    )
    {
        var bounds = admission.Options;
        var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        var inputBound = NlSearchExtractor.ValidateInput(req.Query, today, bounds);
        using var lease =
            admission.TryEnter(NlSearchOptions.EstimateCost(inputBound, bounds.MaxOutputTokens))
            ?? throw new NlSearchExtractionException(
                "AdmissionRejected",
                new NlSearchUsage(null, null, NlSearchOptions.ModelId)
            );
        // Reverse disposal order: scope/db settles and disposes before the active lease.
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AiDbContext>();
        var record = new CostLedgerEntry
        {
            Id = Guid.NewGuid(),
            Feature = "flights.nl_search",
            Model = NlSearchOptions.ModelId,
            Outcome = "Admitted",
            MessageIdentity = NlSearchMessageIdentity.Requested,
            CorrelationId = req.CorrelationId,
            OccurredAt = time.GetUtcNow(),
        };
        db.CostLedger.Add(record);
        await SaveAccountingAsync(db, time, ct);
        ct.ThrowIfCancellationRequested();
        var sw = Stopwatch.StartNew();
        NlSearchExtraction? extraction = null;
        NlSearchExtractionException? failure = null;
        NlSearchUsage usage;
        using (
            var span = AiActivitySource.Source.StartActivity(
                "gen_ai.completion",
                ActivityKind.Client
            )
        )
        {
            span?.SetTag("gen_ai.system", "anthropic");
            span?.SetTag("correlation_id", Activity.Current?.TraceId.ToString() ?? string.Empty);
            try
            {
                extraction = await NlSearchExtractor.ExtractAsync(
                    chat,
                    req.Query,
                    today,
                    bounds,
                    time,
                    lease.DeferUntil,
                    ct
                );
                usage = new(extraction.InputTokens, extraction.OutputTokens, extraction.ModelId);
            }
            catch (NlSearchExtractionException error)
            {
                failure = error;
                usage = error.Usage;
            }
            sw.Stop();
            span?.SetTag("gen_ai.request.model", usage.ModelId);
            span?.SetTag("gen_ai.usage.input_tokens", usage.InputTokens);
            span?.SetTag("gen_ai.usage.output_tokens", usage.OutputTokens);
            span?.SetTag("outcome", failure?.Outcome ?? "Ready");
        }
        var outcome = failure?.Outcome ?? "Ready";
        decimal? cost =
            usage.InputTokens is { } input && usage.OutputTokens is { } output
                ? NlSearchOptions.EstimateCost(input, output)
                : null;
        metrics.RecordTokenUsage(usage.ModelId, "chat", usage.InputTokens, usage.OutputTokens);
        metrics.RecordOperationDuration(usage.ModelId, "chat", sw.Elapsed.TotalSeconds);
        record.Model = usage.ModelId;
        record.InputTokens = usage.InputTokens;
        record.OutputTokens = usage.OutputTokens;
        record.CostUsd = cost;
        record.Outcome = outcome;
        // Expiry or failed update leaves the committed Admitted/unknown row. Late inference
        // only releases its lease; it never accesses this scoped DbContext or retries inference.
        await SaveAccountingAsync(db, time, ct);
        lease.Settle(cost);
        log.LogDebug(
            "NlSearch completed for correlation {CorrelationId}: {Outcome}",
            req.CorrelationId,
            outcome
        );
        if (failure is not null)
            throw failure;
        ct.ThrowIfCancellationRequested();
        var p = extraction!.Result;
        return new(
            req.CorrelationId,
            p.Origin,
            p.Destination,
            p.DepartureDate,
            p.ReturnDate,
            p.PassengerCount,
            p.CabinClass,
            p.Currency,
            extraction.InputTokens,
            extraction.OutputTokens,
            cost!.Value,
            extraction.ModelId
        );
    }

    private static async Task SaveAccountingAsync(
        AiDbContext db,
        TimeProvider time,
        CancellationToken whole
    )
    {
        using var accounting = new CancellationTokenSource(TimeSpan.FromSeconds(2), time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(whole, accounting.Token);
        try
        {
            await db.SaveChangesAsync(linked.Token);
        }
        catch (Exception)
        {
            throw new NlSearchExtractionException(
                "AccountingUnavailable",
                new NlSearchUsage(null, null, NlSearchOptions.ModelId)
            );
        }
    }
}
