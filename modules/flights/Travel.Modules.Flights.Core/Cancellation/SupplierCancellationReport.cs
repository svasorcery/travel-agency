namespace Travel.Modules.Flights.Core.Cancellation;

public enum SupplierCancellationReportState
{
    Unverified,
    ConfirmedCancellation,
    ConfirmedNotCancelled,
}

public sealed record SupplierCancellationReport(
    Guid Id,
    Guid OwnerId,
    string ProviderOrderRef,
    DateTimeOffset ObservedAt
)
{
    public long Revision { get; init; } = 1;
    public SupplierCancellationReportState State { get; init; }
    public DateTimeOffset? CancelledAt { get; init; }
    public CancellationResolutionSource ResolutionSource { get; init; }
}

public sealed record SupplierCancellationReportEvidence(
    Guid ReportId,
    string ProviderOrderRef,
    bool CancellationConfirmed,
    bool NotCancelledConfirmed,
    bool NoPendingCancellationConfirmed,
    DateTimeOffset? CancelledAt
);
