using System.Text.Json.Serialization;
using ErrorOr;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;

namespace Travel.Modules.Flights.Core.Refunds;

public enum CustomerPaymentSource
{
    TestWalletObserved = 1,
}

public enum CustomerRefundState
{
    NotApplicable,
    Assessing,
    Ready,
    DispatchClaimed,
    Pending,
    Unknown,
    ManualReviewRequired,
    Sent,
    ConfirmedNoEffect,
    NoRefundDue,
    NoCustomerPayment,
    LegacyUnverified,
}

public enum CustomerRefundDestination
{
    OriginalFictionalPaymentMethod = 1,
}

public sealed record CustomerPaymentBasis
{
    public Guid BookingId { get; }
    public Guid OwnerId { get; }
    public Guid ConfirmationAttemptId { get; }
    public PaymentRef PaymentReference { get; }
    public Money CapturedAmount { get; }
    public DateTimeOffset CapturedAt { get; }
    public CustomerPaymentSource Source { get; }

    [JsonConstructor]
    private CustomerPaymentBasis(
        Guid bookingId,
        Guid ownerId,
        Guid confirmationAttemptId,
        PaymentRef paymentReference,
        Money capturedAmount,
        DateTimeOffset capturedAt,
        CustomerPaymentSource source
    )
    {
        BookingId = bookingId;
        OwnerId = ownerId;
        ConfirmationAttemptId = confirmationAttemptId;
        PaymentReference = paymentReference;
        CapturedAmount = capturedAmount;
        CapturedAt = capturedAt;
        Source = source;
    }

    public static ErrorOr<CustomerPaymentBasis> Create(
        Guid bookingId,
        Guid ownerId,
        Guid confirmationAttemptId,
        PaymentRef paymentReference,
        Money amount,
        DateTimeOffset capturedAt
    )
    {
        if (
            bookingId == Guid.Empty
            || ownerId == Guid.Empty
            || confirmationAttemptId == Guid.Empty
            || paymentReference.IsEmpty
            || amount is null
            || amount.Amount <= 0m
            || amount.Currency is null
            || CurrencyCode.Create(amount.Currency.Value).IsError
            || capturedAt == default
        )
            return Error.Validation(
                "Flights.CustomerPaymentBasisInvalid",
                "Customer payment evidence invalid."
            );
        return new CustomerPaymentBasis(
            bookingId,
            ownerId,
            confirmationAttemptId,
            paymentReference,
            amount,
            capturedAt,
            CustomerPaymentSource.TestWalletObserved
        );
    }

    public override string ToString() => nameof(CustomerPaymentBasis);
}

public sealed record CustomerRefundAssessment(
    CustomerRefundState State,
    Money? Amount,
    CustomerPaymentBasis? Basis,
    string Reason
);
