using System.Text.Json.Serialization;
using ErrorOr;
using Travel.Modules.Flights.Core.ValueObjects;

namespace Travel.Modules.Flights.Core.Providers.Dtos;

public enum SupplierPaymentKind
{
    Unknown = 0,
    Balance = 1,
}

public sealed record SupplierPaymentEvidence
{
    public string Reference { get; }
    public Money Amount { get; }
    public SupplierPaymentKind Kind { get; }

    [JsonConstructor]
    private SupplierPaymentEvidence(string reference, Money amount, SupplierPaymentKind kind)
    {
        Reference = reference;
        Amount = amount;
        Kind = kind;
    }

    public static ErrorOr<SupplierPaymentEvidence> Create(
        string reference,
        Money amount,
        SupplierPaymentKind kind
    )
    {
        if (
            string.IsNullOrWhiteSpace(reference)
            || reference.Length > 256
            || reference != reference.Trim()
            || reference.Any(char.IsControl)
            || amount is null
            || amount.Amount < 0m
            || amount.Currency is null
            || CurrencyCode.Create(amount.Currency.Value).IsError
            || kind != SupplierPaymentKind.Balance
        )
            return Error.Validation(
                "Flights.SupplierPaymentEvidenceInvalid",
                "Supplier payment facts are invalid."
            );
        return new SupplierPaymentEvidence(reference, amount, kind);
    }

    public override string ToString() => nameof(SupplierPaymentEvidence);
}
