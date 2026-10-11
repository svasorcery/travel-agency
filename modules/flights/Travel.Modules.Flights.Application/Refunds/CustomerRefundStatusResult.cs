using Travel.Modules.Flights.Core.Refunds;

namespace Travel.Modules.Flights.Application.Refunds;

public sealed record CustomerRefundMoney(string Amount, string Currency);

public sealed record CustomerRefundSupplierTerms(
    CustomerRefundMoney Amount,
    string Destination,
    string FinancialSource
);

public sealed record CustomerRefundSupplierReport(
    string State,
    string Source,
    DateTimeOffset ObservedAt,
    DateTimeOffset? CancelledAt
);

public sealed record CustomerRefundStatusResult(
    Guid AggregateId,
    long BookingVersion,
    long? CaseRevision,
    string State,
    CustomerRefundMoney? Amount,
    string? Destination,
    string Source,
    string Reason,
    CustomerRefundSupplierTerms? SupplierRefund,
    string SupplierSettlement,
    string CustomerCredit,
    bool NeedsManualReview,
    DateTimeOffset ObservedAt,
    CustomerRefundSupplierReport? SupplierReport
);

public sealed record GetCustomerRefundStatusQuery(Guid AggregateId, Guid UserId);

public sealed record GetCustomerRefundReviewQuery(Guid AggregateId, Guid ActorId);

public sealed record ResolveCustomerRefundReviewCommand(
    Guid AggregateId,
    Guid ActorId,
    CustomerRefundReviewInput Input
);

public sealed record CustomerRefundReviewResult(
    CustomerRefundStatusResult Status,
    CustomerRefundCase? Case,
    IReadOnlyList<CustomerRefundReviewAudit> History
);

public sealed record CustomerRefundReviewAudit(
    Guid ResolutionId,
    Guid ActorId,
    string Decision,
    string EvidenceReference,
    DateTimeOffset RecordedAt
);
