using ErrorOr;

namespace Travel.Modules.Flights.Core.ValueObjects;

public sealed record Money
{
    public decimal Amount { get; }
    public CurrencyCode Currency { get; }

    private Money(decimal amount, CurrencyCode currency)
    {
        Amount = amount;
        Currency = currency;
    }

    public static ErrorOr<Money> Create(decimal amount, CurrencyCode currency)
    {
        if (amount < 0m)
            return Error.Validation("Money.Negative", "Amount must be non-negative.");

        return new Money(amount, currency);
    }

    public ErrorOr<Money> Add(Money other)
    {
        if (Currency != other.Currency)
            return Error.Validation(
                "Money.CurrencyMismatch",
                $"Cannot add {Currency} and {other.Currency}."
            );

        return new Money(Amount + other.Amount, Currency);
    }

    public override string ToString() =>
        Amount.ToString("N2", System.Globalization.CultureInfo.InvariantCulture)
        + " "
        + Currency.Value;
}
