namespace Travel.Tests.AiEvals.TripsBriefExtraction;

public sealed record TripsBriefEvalAttempt(
    Guid RunId,
    string CaseId,
    Guid CorrelationId,
    string ManifestSha256,
    decimal ReservationUsd,
    DateTimeOffset AtUtc
);

public sealed record TripsBriefEvaluationJournalRead(
    TripsBriefEvaluationReceipt? Receipt,
    string[] UncertainCaseIds,
    string? IndexSha256 = null
);

public interface ITripsBriefEvaluationJournal
{
    bool HasStableReceiptRoot(string? root) => root is not null && Path.IsPathFullyQualified(root);
    void BeginRun(TripsBriefEvaluationBinding binding, TimeProvider time) =>
        TripsBriefEvaluationRunner.ClaimAuthorization(binding, time);
    Task RecordAttemptAsync(TripsBriefEvalAttempt attempt, CancellationToken ct);
    Task RecordResultAsync(TripsBriefEvalCaseReceipt result, CancellationToken ct);
    Task CompleteAsync(TripsBriefEvaluationReceipt receipt, CancellationToken ct);
    string EvidenceKind { get; }
    string ManifestSha256 { get; }
    string? CompletedIndexSha256 => null;
}
