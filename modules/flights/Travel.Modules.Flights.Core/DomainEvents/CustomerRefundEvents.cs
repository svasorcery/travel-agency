using Travel.Modules.Flights.Core.Refunds;
using Travel.Shared.Abstractions;

namespace Travel.Modules.Flights.Core.DomainEvents;

public sealed record CustomerRefundRequested(CustomerRefundCase Case, DateTimeOffset OccurredAt)
    : IDomainEvent;

public sealed record CustomerRefundDispatchClaimed(
    Guid RefundId,
    Guid SenderId,
    DateTimeOffset OccurredAt
) : IDomainEvent;

public sealed record CustomerRefundOutcomeRecorded(
    Guid RefundId,
    CustomerRefundObservation? Observation,
    string Reason,
    DateTimeOffset OccurredAt
) : IDomainEvent;

public sealed record CustomerRefundRequiredManualReview(
    Guid RefundId,
    string Reason,
    DateTimeOffset OccurredAt,
    CustomerRefundCase? NewCase = null
) : IDomainEvent;

public sealed record CustomerRefundWasNotRequired(
    CustomerRefundCase Case,
    DateTimeOffset OccurredAt
) : IDomainEvent;

public sealed record CustomerRefundReviewRecorded(
    Guid RefundId,
    Guid ResolutionId,
    Guid ActorId,
    string PayloadHash,
    CustomerRefundReviewKind Decision,
    string EvidenceReference,
    CustomerRefundObservation? Observation,
    CustomerRefundSenderStop? SenderStop,
    DateTimeOffset OccurredAt
) : IDomainEvent;
