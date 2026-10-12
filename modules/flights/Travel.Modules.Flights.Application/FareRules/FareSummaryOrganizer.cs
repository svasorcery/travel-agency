using Microsoft.Extensions.Logging;
using Travel.IntegrationContracts.AI.FareSummary;
using Travel.Modules.Flights.Core.ValueObjects;
using Wolverine;

namespace Travel.Modules.Flights.Application.FareRules;

public sealed class FareSummaryOrganizationOptions
{
    public bool OrganizedEnabled { get; set; }
}

public interface IFareSummaryOrganizer
{
    Task<FareSummaryResult> OrganizeAsync(
        QuotedFareSnapshot snapshot,
        string? mode,
        bool authenticated,
        bool hasSelectedServices,
        DateTimeOffset handlerStartedAt,
        Func<CancellationToken, Task<bool>> isCurrent,
        CancellationToken ct
    );
}

public sealed class FareSummaryOrganizer(
    IMessageBus bus,
    TimeProvider time,
    FareSummaryOrganizationOptions options,
    ILogger<FareSummaryOrganizer> log
) : IFareSummaryOrganizer
{
    public async Task<FareSummaryResult> OrganizeAsync(
        QuotedFareSnapshot snapshot,
        string? mode,
        bool authenticated,
        bool hasSelectedServices,
        DateTimeOffset handlerStartedAt,
        Func<CancellationToken, Task<bool>> isCurrent,
        CancellationToken ct
    )
    {
        ct.ThrowIfCancellationRequested();
        FareSummaryResult Baseline(string status) =>
            FareSummaryFormatter.Current(
                snapshot,
                time.GetUtcNow(),
                hasSelectedServices,
                options.OrganizedEnabled && authenticated,
                status
            );
        if (!options.OrganizedEnabled)
            return Baseline("Disabled");
        if (!authenticated)
            return Baseline("NotAuthenticated");
        if (mode is null or "deterministic")
            return Baseline("NotRequested");
        if (mode != "organized")
            return Baseline("UnsupportedMode");
        var baseline = Baseline("NotRequested");
        if (baseline.State != "Current")
            return baseline with { AiStatus = "SourceStale" };
        var facts = FareSummaryFormatter.BuildFacts(snapshot);
        if (facts.Length == 0 || !facts.Any(f => f.State != "Unknown" || f.Amount is not null))
            return Baseline("InputUnavailable");
        var now = time.GetUtcNow();
        var deadline = new[]
        {
            now.AddSeconds(4),
            handlerStartedAt.AddSeconds(12),
            snapshot.Evidence.OfferExpiresAt,
            snapshot.Evidence.FirstDepartureAt,
        }.Min();
        if (deadline <= now)
            return Baseline("DeadlineExceeded");
        var request = new FareSummaryRequested(
            Guid.NewGuid(),
            snapshot.Fingerprint,
            FareSummaryMessageIdentity.PromptVersion,
            "ru",
            deadline,
            facts
        );
        string status;
        FareSummaryPlanSection[]? plan = null;
        using var deadlineCancellation = new CancellationTokenSource(deadline - now, time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            ct,
            deadlineCancellation.Token
        );
        try
        {
            // One publication only. A timeout never resubmits a possibly billed request.
            var response = await bus.InvokeAsync<FareSummaryPlanned>(
                    request,
                    linked.Token,
                    deadline - now
                )
                .WaitAsync(deadline - now, time, ct);
            ct.ThrowIfCancellationRequested();
            status = AllowedStatus(response.Status);
            if (response.Status == "Ready")
            {
                if (FareSummaryPlanValidator.Validate(request, response).IsError)
                    status = "InvalidOutput";
                else
                    plan = response.Sections;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error)
        {
            status = error is TimeoutException or OperationCanceledException
                ? "DeadlineExceeded"
                : "InferenceFailed";
            log.LogWarning("Fare summary unavailable: {ErrorType}", error.GetType().Name);
        }
        ct.ThrowIfCancellationRequested();
        baseline = Baseline(status);
        if (baseline.State != "Current")
            return baseline with { AiStatus = "SourceStale" };
        if (time.GetUtcNow() >= deadline)
            return baseline with
            {
                State = "Stale",
                Reason = "FreshnessUnknown",
                AiStatus = "FreshnessUnknown",
            };
        try
        {
            var remaining = deadline - time.GetUtcNow();
            if (remaining <= TimeSpan.Zero)
                return baseline with
                {
                    State = "Stale",
                    Reason = "FreshnessUnknown",
                    AiStatus = "FreshnessUnknown",
                };
            var current = await isCurrent(linked.Token).WaitAsync(remaining, time, ct);
            ct.ThrowIfCancellationRequested();
            baseline = Baseline(status);
            if (baseline.State != "Current")
                return baseline with { AiStatus = "SourceStale" };
            if (time.GetUtcNow() >= deadline)
                return baseline with
                {
                    State = "Stale",
                    Reason = "FreshnessUnknown",
                    AiStatus = "FreshnessUnknown",
                };
            if (!current)
                return baseline with
                {
                    State = "Stale",
                    Reason = "Superseded",
                    AiStatus = "SourceStale",
                };
            return FareSummaryFormatter.Current(
                snapshot,
                time.GetUtcNow(),
                hasSelectedServices,
                true,
                status,
                plan
            );
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error)
        {
            log.LogWarning("Fare summary freshness unavailable: {ErrorType}", error.GetType().Name);
            return Baseline("FreshnessUnknown") with
            {
                State = "Stale",
                Reason = "FreshnessUnknown",
            };
        }
    }

    private static string AllowedStatus(string? status) =>
        status
            is "Disabled"
                or "InputUnavailable"
                or "Busy"
                or "ModelUnavailable"
                or "InferenceFailed"
                or "InvalidOutput"
                or "UsageUnknown"
                or "AccountingUnavailable"
                or "DeadlineExceeded"
                or "BudgetExceeded"
                or "Ready"
            ? status
            : "InvalidOutput";
}
