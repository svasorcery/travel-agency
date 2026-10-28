using Travel.Tests.AiEvals.TripsBriefExtraction;

namespace Travel.Tests.Contract.Trips;

// Deliberately unavailable in the tool and paid project. This evidence proves mechanics only.
internal sealed class MechanicalJournal : ITripsBriefEvaluationJournal
{
    public string EvidenceKind => "mechanical-memory-only";
    public string ManifestSha256 => new('a', 64);
    public bool FailIntent { get; init; }
    public List<TripsBriefEvalAttempt> Attempts { get; } = [];
    public List<TripsBriefEvalCaseReceipt> Results { get; } = [];

    public Task RecordAttemptAsync(TripsBriefEvalAttempt attempt, CancellationToken ct)
    {
        if (FailIntent)
            throw new IOException("ControlledWriteFailure");
        Attempts.Add(attempt);
        return Task.CompletedTask;
    }

    public Task RecordResultAsync(TripsBriefEvalCaseReceipt result, CancellationToken ct)
    {
        Results.Add(result);
        return Task.CompletedTask;
    }

    public Task CompleteAsync(TripsBriefEvaluationReceipt receipt, CancellationToken ct) =>
        Task.CompletedTask;
}
