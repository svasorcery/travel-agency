namespace Travel.Modules.Flights.Core.Refunds;

public enum CustomerRefundReviewKind
{
    Unknown,
    Inconclusive,
    Sent,
    ConfirmedNoEffect,
}

public sealed record CustomerRefundSenderStop(
    Guid SenderId,
    DateTimeOffset StoppedAt,
    bool EgressIsolated,
    bool CannotResume,
    bool NoPending,
    bool NoEffect
);

public sealed record CustomerRefundReviewInput(
    Guid RefundId,
    long ExpectedRevision,
    Guid ResolutionId,
    CustomerRefundReviewKind Decision,
    string EvidenceReference,
    CustomerRefundObservation? Observation = null,
    CustomerRefundSenderStop? SenderStop = null
);
