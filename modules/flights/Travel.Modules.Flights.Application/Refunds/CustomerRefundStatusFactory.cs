using System.Globalization;
using Travel.Modules.Flights.Core.Aggregates;
using Travel.Modules.Flights.Core.Cancellation;
using Travel.Modules.Flights.Core.Refunds;
using Travel.Modules.Flights.Core.ValueObjects;

namespace Travel.Modules.Flights.Application.Refunds;

public static class CustomerRefundStatusFactory
{
    public static CustomerRefundStatusResult Create(
        BookingAggregate b,
        long version,
        DateTimeOffset now
    )
    {
        var c = b.CustomerRefund;
        var op =
            c is not null
            && b.CancellationOperations.TryGetValue(c.CancellationId, out var original)
                ? original
                : b.CurrentCancellation;
        var state =
            c?.State
            ?? (
                op?.Outcome == CancellationOutcome.Succeeded ? CustomerRefundState.Assessing
                : b.HasSupplierCancellationReportBarrier ? CustomerRefundState.ManualReviewRequired
                : b.Status is BookingStatus.Cancelled or BookingStatus.Refunded
                    ? CustomerRefundState.LegacyUnverified
                : CustomerRefundState.NotApplicable
            );
        var terms = op?.Terms;
        var report = b.CurrentSupplierCancellationReport;
        return new(
            b.Id,
            version,
            c?.Revision,
            state.ToString(),
            Money(c?.Amount),
            c?.Basis is null
                ? null
                : CustomerRefundDestination.OriginalFictionalPaymentMethod.ToString(),
            c?.Observation?.Source.ToString() ?? "None",
            c?.Reason
                ?? (
                    b.HasSupplierCancellationReportBarrier ? "SupplierReportRequiresReview"
                    : state == CustomerRefundState.LegacyUnverified ? "PaymentNotEstablished"
                    : "None"
                ),
            terms is null
                ? null
                : new(
                    Money(terms.Refund)!,
                    terms.Destination.ToString(),
                    terms.Settlement.Provenance.ToString()
                ),
            "NotEstablished",
            "NotEstablished",
            c?.NeedsManualReview ?? b.HasSupplierCancellationReportBarrier,
            now,
            report is null
                ? null
                : new(
                    report.State.ToString(),
                    report.ResolutionSource == CancellationResolutionSource.None
                        ? "SignedWebhook"
                        : report.ResolutionSource.ToString(),
                    report.ObservedAt,
                    report.CancelledAt
                )
        );
    }

    private static CustomerRefundMoney? Money(Money? amount) =>
        amount is null
            ? null
            : new(
                amount.Amount.ToString(
                    "0.############################",
                    CultureInfo.InvariantCulture
                ),
                amount.Currency.Value
            );
}
