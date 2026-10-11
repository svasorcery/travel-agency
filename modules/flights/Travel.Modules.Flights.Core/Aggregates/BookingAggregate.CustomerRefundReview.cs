using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ErrorOr;
using Travel.Modules.Flights.Core.DomainEvents;
using Travel.Modules.Flights.Core.Refunds;

namespace Travel.Modules.Flights.Core.Aggregates;

public sealed partial class BookingAggregate
{
    private readonly Dictionary<Guid, CustomerRefundReviewRecorded> customerRefundReviewReceipts =
        new();
    public IReadOnlyCollection<CustomerRefundReviewRecorded> CustomerRefundReviewHistory =>
        customerRefundReviewReceipts.Values;

    public ErrorOr<CustomerRefundDecision> DecideCustomerRefundReview(
        Guid actor,
        CustomerRefundReviewInput input,
        DateTimeOffset now
    )
    {
        if (
            actor == Guid.Empty
            || input is null
            || !RefundTarget(input.RefundId)
            || input.ResolutionId == Guid.Empty
            || input.ExpectedRevision <= 0
            || !Enum.IsDefined(input.Decision)
            || input.Decision == CustomerRefundReviewKind.Unknown
            || !EvidenceReference(input.EvidenceReference)
        )
            return RefundInvalid();
        var c = CustomerRefund!;
        var o = input.Observation;
        var fingerprint = Convert
            .ToHexString(
                SHA256.HashData(
                    Encoding.UTF8.GetBytes(
                        JsonSerializer.Serialize(
                            new
                            {
                                input.RefundId,
                                input.ExpectedRevision,
                                input.Decision,
                                input.EvidenceReference,
                                Observation = o is null
                                    ? null
                                    : new
                                    {
                                        o.RefundId,
                                        o.PaymentReference,
                                        Amount = o.Amount?.Amount.ToString(
                                            "G29",
                                            CultureInfo.InvariantCulture
                                        ),
                                        Currency = o.Amount?.Currency?.Value,
                                        o.Destination,
                                        o.State,
                                        o.ReceiptReference,
                                        ObservedAt = o.ObservedAt.UtcTicks,
                                        o.Source,
                                        o.NoPendingOutcome,
                                    },
                                Stop = input.SenderStop is null
                                    ? null
                                    : new
                                    {
                                        input.SenderStop.SenderId,
                                        StoppedAt = input.SenderStop.StoppedAt.UtcTicks,
                                        input.SenderStop.EgressIsolated,
                                        input.SenderStop.CannotResume,
                                        input.SenderStop.NoPending,
                                        input.SenderStop.NoEffect,
                                    },
                            }
                        )
                    )
                )
            )
            .ToLowerInvariant();
        if (customerRefundReviewReceipts.TryGetValue(input.ResolutionId, out var old))
            return
                old.ActorId == actor
                && old.RefundId == input.RefundId
                && old.PayloadHash == fingerprint
                ? RefundNoOp()
                : RefundInvalid();
        if (c.Revision != input.ExpectedRevision)
            return RefundInvalid();
        if (input.Decision == CustomerRefundReviewKind.Inconclusive)
        {
            if (o is not null || input.SenderStop is not null || c.IsTerminal)
                return RefundInvalid();
        }
        else
        {
            var expected =
                input.Decision == CustomerRefundReviewKind.Sent
                    ? CustomerRefundState.Sent
                    : CustomerRefundState.ConfirmedNoEffect;
            if (
                o is null
                || o.State != expected
                || !ValidRefundObservation(c, o, now, CustomerRefundEvidenceSource.OperatorVerified)
            )
                return RefundInvalid();
            if (c.IsTerminal)
                return c.Observation is { } prior && SameRefundReceipt(prior, o)
                    ? RefundNoOp()
                    : RefundInvalid();
            if (expected == CustomerRefundState.ConfirmedNoEffect && c.ClaimedAt is { } claimed)
            {
                var stop = input.SenderStop;
                if (
                    stop is null
                    || stop.SenderId != c.SenderId
                    || !stop.EgressIsolated
                    || !stop.CannotResume
                    || !stop.NoPending
                    || !stop.NoEffect
                    || stop.StoppedAt < claimed
                    || stop.StoppedAt > o.ObservedAt
                )
                    return RefundInvalid();
            }
            else if (input.SenderStop is not null)
                return RefundInvalid();
        }
        return RefundAllow(
            new CustomerRefundReviewRecorded(
                c.Id,
                input.ResolutionId,
                actor,
                fingerprint,
                input.Decision,
                input.EvidenceReference,
                o,
                input.SenderStop,
                now
            )
        );
    }

    public void Apply(CustomerRefundReviewRecorded e)
    {
        customerRefundReviewReceipts.Add(e.ResolutionId, e);
        if (e.Decision == CustomerRefundReviewKind.Inconclusive)
            ChangeRefund(e.RefundId, c => c with { NeedsManualReview = true });
        else
            ChangeRefund(
                e.RefundId,
                c =>
                    c with
                    {
                        State = e.Observation!.State,
                        Observation = e.Observation,
                        NeedsManualReview = false,
                        Reason = "OperatorVerified",
                    }
            );
    }
}
