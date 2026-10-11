using System.Globalization;
using ErrorOr;
using Travel.Modules.Flights.Core.Refunds;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;

namespace Travel.Modules.Flights.Api.Contracts;

public sealed record CustomerRefundReviewRequest(
    Guid RefundId,
    long ExpectedRevision,
    Guid ResolutionId,
    string Decision,
    string EvidenceReference,
    CustomerRefundObservationRequest? Observation = null,
    CustomerRefundSenderStop? SenderStop = null
)
{
    public ErrorOr<CustomerRefundReviewInput> ToInput()
    {
        if (
            RefundId == Guid.Empty
            || ResolutionId == Guid.Empty
            || ExpectedRevision <= 0
            || !CancellationRequestValidation.EnumValue(
                Decision,
                out CustomerRefundReviewKind decision
            )
            || EvidenceReference is null
            || !System.Text.RegularExpressions.Regex.IsMatch(
                EvidenceReference,
                "^[A-Z0-9][A-Z0-9-]{7,63}$"
            )
        )
            return CustomerRefundRequestValidation.Invalid;
        CustomerRefundObservation? observation = null;
        if (Observation is not null)
        {
            var parsed = Observation.ToObservation();
            if (parsed.IsError)
                return parsed.Errors;
            observation = parsed.Value;
        }
        return new CustomerRefundReviewInput(
            RefundId,
            ExpectedRevision,
            ResolutionId,
            decision,
            EvidenceReference,
            observation,
            SenderStop
        );
    }
}

public sealed record CustomerRefundObservationRequest(
    Guid RefundId,
    Guid PaymentReference,
    string Amount,
    string Currency,
    string Destination,
    string State,
    string ReceiptReference,
    DateTimeOffset ObservedAt,
    bool NoPendingOutcome = false
)
{
    internal ErrorOr<CustomerRefundObservation> ToObservation()
    {
        if (
            RefundId == Guid.Empty
            || PaymentReference == Guid.Empty
            || ObservedAt == default
            || !CancellationRequestValidation.EnumValue(
                Destination,
                out CustomerRefundDestination destination
            )
            || !CancellationRequestValidation.EnumValue(State, out CustomerRefundState state)
            || state
                is not (
                    CustomerRefundState.Sent
                    or CustomerRefundState.Pending
                    or CustomerRefundState.ConfirmedNoEffect
                )
            || Amount is null
            || !System.Text.RegularExpressions.Regex.IsMatch(Amount, "^[0-9]+(\\.[0-9]+)?$")
            || !decimal.TryParse(
                Amount,
                NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out var value
            )
            || !ExactDecimalAmount(Amount, value)
            || ReceiptReference is null
            || ReceiptReference.Length is < 1 or > 128
            || !ReceiptReference.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-')
        )
            return CustomerRefundRequestValidation.Invalid;
        var currency = CurrencyCode.Create(Currency);
        if (currency.IsError)
            return currency.Errors;
        var money = Money.Create(value, currency.Value);
        return money.IsError
            ? money.Errors
            : new CustomerRefundObservation(
                RefundId,
                new PaymentRef(PaymentReference),
                money.Value,
                destination,
                state,
                ReceiptReference,
                ObservedAt,
                CustomerRefundEvidenceSource.OperatorVerified,
                NoPendingOutcome
            );
    }

    private static bool ExactDecimalAmount(string input, decimal parsed)
    {
        var parts = input.Split('.');
        var whole = parts[0].TrimStart('0');
        if (whole.Length == 0)
            whole = "0";
        var fraction = parts.Length == 2 ? parts[1].TrimEnd('0') : "";
        var normalized = fraction.Length == 0 ? whole : whole + "." + fraction;
        return normalized
            == parsed.ToString("0.############################", CultureInfo.InvariantCulture);
    }
}
