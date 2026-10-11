using ErrorOr;
using Travel.Modules.Flights.Core.DomainEvents;
using Travel.Modules.Flights.Core.Refunds;
using Travel.Shared.Abstractions;

namespace Travel.Modules.Flights.Core.Aggregates;

public sealed partial class BookingAggregate
{
    public CustomerRefundCase? CustomerRefund { get; private set; }

    public ErrorOr<CustomerRefundDecision> DecideCustomerRefundAdmission(
        Guid owner,
        Guid cancellationId,
        Guid refundId,
        DateTimeOffset now,
        bool executionAvailable = true
    )
    {
        if (!HasConsistentMutationOwner || OwnerUserId != owner || owner == Guid.Empty)
            return RefundNotFound();
        if (CustomerRefund is not null)
            return RefundNoOp();
        if (refundId == Guid.Empty || cancellationId == Guid.Empty || now == default)
            return RefundInvalid();
        var assessment = CustomerRefundPolicy.Assess(this, cancellationId, now);
        if (assessment.IsError)
            return assessment.Errors;
        if (assessment.Value.State == CustomerRefundState.NotApplicable)
            return RefundNoOp();
        DateTimeOffset deadline;
        try
        {
            deadline = now.AddSeconds(180);
        }
        catch (ArgumentOutOfRangeException)
        {
            return RefundInvalid();
        }
        var value = assessment.Value;
        if (value.State == CustomerRefundState.Ready && !executionAvailable)
            value = value with
            {
                State = CustomerRefundState.ManualReviewRequired,
                Reason = "GatewayUnavailable",
            };
        var current = new CustomerRefundCase(
            refundId,
            owner,
            cancellationId,
            now,
            deadline,
            value.State,
            value.Amount,
            value.Basis,
            value.Reason
        );
        return value.State switch
        {
            CustomerRefundState.Ready => RefundAllow(new CustomerRefundRequested(current, now)),
            CustomerRefundState.NoRefundDue or CustomerRefundState.NoCustomerPayment => RefundAllow(
                new CustomerRefundWasNotRequired(current, now)
            ),
            _ => RefundAllow(
                new CustomerRefundRequiredManualReview(refundId, value.Reason, now, current)
            ),
        };
    }

    public ErrorOr<CustomerRefundDecision> DecideCustomerRefundClaim(
        Guid id,
        long revision,
        Guid sender,
        DateTimeOffset now
    )
    {
        if (!RefundTarget(id) || sender == Guid.Empty)
            return RefundNotFound();
        var c = CustomerRefund!;
        if (c.ClaimedAt is not null)
            return RefundNoOp();
        if (
            c.State != CustomerRefundState.Ready
            || c.Revision != revision
            || now >= c.DeadlineAt
            || now < c.AdmittedAt
            || c.Basis is null
            || c.Amount is null
            || c.Basis.OwnerId != OwnerUserId
            || c.Basis.BookingId != Id
        )
            return RefundInvalid();
        return RefundAllow(new CustomerRefundDispatchClaimed(id, sender, now));
    }

    public bool CanContinueCustomerRefund(Guid id, Guid sender) =>
        RefundTarget(id)
        && CustomerRefund is { } c
        && c.SenderId == sender
        && sender != Guid.Empty
        && c.State == CustomerRefundState.DispatchClaimed;

    public ErrorOr<CustomerRefundDecision> DecideCustomerRefundOutcome(
        Guid id,
        Guid sender,
        CustomerRefundObservation? observation,
        DateTimeOffset now
    )
    {
        if (
            !RefundTarget(id)
            || sender == Guid.Empty
            || CustomerRefund!.SenderId != sender
            || CustomerRefund.ClaimedAt is null
        )
            return RefundNotFound();
        var c = CustomerRefund;
        if (
            observation is null
            || !ValidRefundObservation(
                c,
                observation,
                now,
                CustomerRefundEvidenceSource.FictionalGateway
            )
        )
            return c.IsTerminal
                ? RefundAllow(new CustomerRefundRequiredManualReview(id, "ConflictingReceipt", now))
                : RefundAllow(new CustomerRefundOutcomeRecorded(id, null, "OutcomeUnknown", now));
        if (c.IsTerminal)
            return c.Observation is { } known && SameRefundReceipt(known, observation)
                ? RefundNoOp()
                : RefundAllow(
                    new CustomerRefundRequiredManualReview(id, "ConflictingReceipt", now)
                );
        if (observation.State == CustomerRefundState.ConfirmedNoEffect && c.HadUnknown)
            return RefundAllow(
                new CustomerRefundRequiredManualReview(id, "PriorOutcomeUncertain", now)
            );
        return RefundAllow(new CustomerRefundOutcomeRecorded(id, observation, "None", now));
    }

    public ErrorOr<CustomerRefundDecision> DecideCustomerRefundDeadline(
        Guid id,
        DateTimeOffset deadline,
        DateTimeOffset now
    )
    {
        if (!RefundTarget(id))
            return RefundNotFound();
        var c = CustomerRefund!;
        if (
            c.IsTerminal
            || c.State
                is CustomerRefundState.ManualReviewRequired
                    or CustomerRefundState.LegacyUnverified
            || deadline != c.DeadlineAt
            || now < deadline
        )
            return RefundNoOp();
        return RefundAllow(
            new CustomerRefundRequiredManualReview(
                id,
                c.ClaimedAt is null ? "NotDispatchedDeadline" : "OutcomeUnprovenDeadline",
                now
            )
        );
    }

    public void Apply(CustomerRefundRequested e) => CustomerRefund = e.Case;

    public void Apply(CustomerRefundWasNotRequired e) => CustomerRefund = e.Case;

    public void Apply(CustomerRefundDispatchClaimed e) =>
        ChangeRefund(
            e.RefundId,
            c =>
                c with
                {
                    SenderId = e.SenderId,
                    ClaimedAt = e.OccurredAt,
                    State = CustomerRefundState.DispatchClaimed,
                }
        );

    public void Apply(CustomerRefundOutcomeRecorded e) =>
        ChangeRefund(
            e.RefundId,
            c =>
                c with
                {
                    State =
                        RefundOutcomeStaysManual(c, e) ? CustomerRefundState.ManualReviewRequired
                        : e.Observation is null ? CustomerRefundState.Unknown
                        : c.HadUnknown && e.Observation.State == CustomerRefundState.Pending
                            ? CustomerRefundState.Unknown
                        : e.Observation.State,
                    Observation = e.Observation ?? c.Observation,
                    HadUnknown = c.HadUnknown || e.Observation is null,
                    Reason = RefundOutcomeStaysManual(c, e) ? c.Reason : e.Reason,
                    NeedsManualReview = RefundOutcomeStaysManual(c, e),
                }
        );

    public void Apply(CustomerRefundRequiredManualReview e)
    {
        if (e.NewCase is { } created)
            CustomerRefund = created with { NeedsManualReview = true };
        else
            ChangeRefund(
                e.RefundId,
                c =>
                    c with
                    {
                        State = c.IsTerminal ? c.State : CustomerRefundState.ManualReviewRequired,
                        HadUnknown = c.HadUnknown || c.ClaimedAt is not null,
                        NeedsManualReview = true,
                        Reason = e.Reason,
                    }
            );
    }

    private static bool RefundOutcomeStaysManual(
        CustomerRefundCase c,
        CustomerRefundOutcomeRecorded e
    ) =>
        (c.NeedsManualReview || c.State == CustomerRefundState.ManualReviewRequired)
        && (e.Observation is null || e.Observation.State == CustomerRefundState.Pending);

    private void ChangeRefund(Guid id, Func<CustomerRefundCase, CustomerRefundCase> update)
    {
        if (CustomerRefund is not { } c || c.Id != id)
            throw new InvalidOperationException("Customer refund source inconsistent.");
        CustomerRefund = update(c) with { Revision = c.Revision + 1 };
    }

    private bool RefundTarget(Guid id) =>
        HasConsistentMutationOwner
        && CustomerRefund is { } c
        && c.Id == id
        && c.OwnerId == OwnerUserId;

    private static Error RefundNotFound() =>
        Error.NotFound("Flights.BookingNotFound", "Booking not found.");

    private static Error RefundInvalid() =>
        Error.Conflict("Flights.CustomerRefundConflict", "Customer refund action unavailable.");

    private static CustomerRefundDecision RefundNoOp() => new(new([]));

    private static CustomerRefundDecision RefundAllow(params IDomainEvent[] events) =>
        new(new(events));

    private static bool ValidRefundObservation(
        CustomerRefundCase c,
        CustomerRefundObservation o,
        DateTimeOffset now,
        CustomerRefundEvidenceSource source
    ) =>
        c.Basis is { } basis
        && c.Amount is { } amount
        && o.RefundId == c.Id
        && o.PaymentReference == basis.PaymentReference
        && o.Amount == amount
        && amount.Amount > 0m
        && o.Destination == CustomerRefundDestination.OriginalFictionalPaymentMethod
        && o.Source == source
        && o.ObservedAt != default
        && o.ObservedAt <= now
        && o.ObservedAt >= (c.ClaimedAt ?? c.AdmittedAt)
        && (
            o.State is CustomerRefundState.Sent or CustomerRefundState.Pending
            || o.State == CustomerRefundState.ConfirmedNoEffect && o.NoPendingOutcome
        )
        && RefundReference(o.ReceiptReference);

    private static bool RefundReference(string? value) =>
        value is { Length: >= 1 and <= 128 }
        && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-');

    private static bool SameRefundReceipt(
        CustomerRefundObservation a,
        CustomerRefundObservation b
    ) =>
        a.RefundId == b.RefundId
        && a.PaymentReference == b.PaymentReference
        && a.Amount == b.Amount
        && a.Destination == b.Destination
        && a.State == b.State
        && a.ReceiptReference == b.ReceiptReference
        && a.Source == b.Source
        && a.NoPendingOutcome == b.NoPendingOutcome;
}
