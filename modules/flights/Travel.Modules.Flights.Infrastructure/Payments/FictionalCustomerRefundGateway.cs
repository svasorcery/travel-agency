using ErrorOr;
using Travel.Modules.Flights.Core.Providers;
using Travel.Modules.Flights.Core.Refunds;
using Travel.Shared.Abstractions;

namespace Travel.Modules.Flights.Infrastructure.Payments;

public enum FictionalCustomerRefundPreset
{
    Sent,
    Pending,
    NoEffect,
    ResultLost,
}

[TestOnly]
public sealed class FictionalCustomerRefundGateway(
    TimeProvider time,
    FictionalCustomerRefundPreset preset = FictionalCustomerRefundPreset.Sent
) : ICustomerRefundGateway
{
    public Task<ErrorOr<CustomerRefundObservation>> ExecuteAsync(
        CustomerRefundRequest request,
        CancellationToken ct
    )
    {
        var now = time.GetUtcNow();
        if (
            ct.IsCancellationRequested
            || request is null
            || request.RefundId == Guid.Empty
            || request.OriginalPayment is not { } basis
            || basis.Source != CustomerPaymentSource.TestWalletObserved
            || basis.PaymentReference.IsEmpty
            || basis.OwnerId == Guid.Empty
            || basis.BookingId == Guid.Empty
            || basis.ConfirmationAttemptId == Guid.Empty
            || basis.CapturedAt == default
            || basis.CapturedAt > now
            || request.Amount is not { } amount
            || amount.Amount <= 0m
            || amount.Currency != basis.CapturedAmount.Currency
            || amount.Amount > basis.CapturedAmount.Amount
            || request.Destination != CustomerRefundDestination.OriginalFictionalPaymentMethod
        )
            return Task.FromResult<ErrorOr<CustomerRefundObservation>>(
                Error.Validation(
                    "Flights.CustomerRefundRequestInvalid",
                    "Customer refund request invalid."
                )
            );
        if (preset == FictionalCustomerRefundPreset.ResultLost || !Enum.IsDefined(preset))
            return Task.FromResult<ErrorOr<CustomerRefundObservation>>(
                Error.Failure(
                    "Flights.CustomerRefundOutcomeUnknown",
                    "Customer refund outcome unknown."
                )
            );
        var state = preset switch
        {
            FictionalCustomerRefundPreset.Pending => CustomerRefundState.Pending,
            FictionalCustomerRefundPreset.NoEffect => CustomerRefundState.ConfirmedNoEffect,
            _ => CustomerRefundState.Sent,
        };
        return Task.FromResult<ErrorOr<CustomerRefundObservation>>(
            new CustomerRefundObservation(
                request.RefundId,
                basis.PaymentReference,
                amount,
                request.Destination,
                state,
                "fref_" + request.RefundId.ToString("N"),
                now,
                CustomerRefundEvidenceSource.FictionalGateway,
                state == CustomerRefundState.ConfirmedNoEffect
            )
        );
    }
}
