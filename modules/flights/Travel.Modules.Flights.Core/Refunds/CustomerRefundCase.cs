using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Travel.Shared.Abstractions;

namespace Travel.Modules.Flights.Core.Refunds;

public enum CustomerRefundEvidenceSource
{
    FictionalGateway = 1,
    OperatorVerified = 2,
}

public sealed record CustomerRefundObservation(
    Guid RefundId,
    PaymentRef PaymentReference,
    Money Amount,
    CustomerRefundDestination Destination,
    CustomerRefundState State,
    string ReceiptReference,
    DateTimeOffset ObservedAt,
    CustomerRefundEvidenceSource Source,
    bool NoPendingOutcome = false
)
{
    public override string ToString() => nameof(CustomerRefundObservation);
}

public sealed record CustomerRefundCase(
    Guid Id,
    Guid OwnerId,
    Guid CancellationId,
    DateTimeOffset AdmittedAt,
    DateTimeOffset DeadlineAt,
    CustomerRefundState State,
    Money? Amount,
    CustomerPaymentBasis? Basis,
    string Reason
)
{
    public long Revision { get; init; } = 1;
    public string PolicyVersion => "customer-refund-v1";
    public Guid? SenderId { get; init; }
    public DateTimeOffset? ClaimedAt { get; init; }
    public CustomerRefundObservation? Observation { get; init; }
    public bool HadUnknown { get; init; }
    public bool NeedsManualReview { get; init; }
    public bool IsTerminal =>
        State
            is CustomerRefundState.Sent
                or CustomerRefundState.ConfirmedNoEffect
                or CustomerRefundState.NoRefundDue
                or CustomerRefundState.NoCustomerPayment;

    public override string ToString() => nameof(CustomerRefundCase);
}

public sealed record CustomerRefundDecision(EquatableArray<IDomainEvent> Events);
