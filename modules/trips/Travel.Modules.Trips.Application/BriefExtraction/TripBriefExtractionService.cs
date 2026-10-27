using ErrorOr;

namespace Travel.Modules.Trips.Application.BriefExtraction;

public sealed class TripBriefExtractionService(
    ITripBriefExtractionClient client,
    TimeProvider clock
)
{
    public async Task<ErrorOr<TripBriefCandidate>> ExtractAsync(
        TripBriefExtractionInput input,
        CancellationToken ct
    )
    {
        if (!TripBriefCandidateValidator.ValidInput(input))
            return TripBriefErrors.InvalidInput;
        var remaining = input.Deadline - clock.GetUtcNow();
        if (remaining <= TimeSpan.Zero)
            return TripBriefErrors.Deadline;
        using var deadline = new CancellationTokenSource(remaining, clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, deadline.Token);
        try
        {
            var pending = client.ParseAsync(input, linked.Token);
            ObserveFault(pending);
            var reply = await pending.WaitAsync(remaining, clock, linked.Token);
            if (clock.GetUtcNow() >= input.Deadline || deadline.IsCancellationRequested)
                return TripBriefErrors.Deadline;
            if (reply.IsError)
                return reply.Errors;
            var value = reply.Value;
            if (
                value is null
                || value.CorrelationId != input.RequestId
                || value.ProcessingPolicyVersion != input.ProcessingPolicyVersion
            )
                return TripBriefErrors.InvalidOutput;
            return value.Status switch
            {
                "Ready" => TripBriefCandidateValidator.Validate(input, value),
                "InvalidOutput" => TripBriefErrors.InvalidOutput,
                "Busy" or "BudgetExceeded" => TripBriefErrors.Busy,
                "DeadlineExceeded" => TripBriefErrors.Deadline,
                "ProcessingPolicyChanged" => TripBriefErrors.PolicyChanged,
                "Refused" => Error.Custom(503, "Trips.BriefRefused", "Extraction was refused."),
                "Disabled"
                or "InputUnavailable"
                or "ModelUnavailable"
                or "InferenceFailed"
                or "AccountingUnavailable" => TripBriefErrors.Unavailable,
                _ => TripBriefErrors.InvalidOutput,
            };
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return TripBriefErrors.Deadline;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (TimeoutException)
        {
            return TripBriefErrors.Deadline;
        }
        catch (Exception)
        {
            return TripBriefErrors.Unavailable;
        }
    }

    private static void ObserveFault(Task pending) =>
        _ = pending.ContinueWith(
            static task =>
            {
                _ = task.Exception;
            },
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default
        );
}
