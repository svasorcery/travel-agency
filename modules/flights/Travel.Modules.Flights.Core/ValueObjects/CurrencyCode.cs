using ErrorOr;

namespace Travel.Modules.Flights.Core.ValueObjects;

public sealed record CurrencyCode
{
    public string Value { get; }

    private CurrencyCode(string value) => Value = value;

    public static ErrorOr<CurrencyCode> Create(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return Error.Validation("CurrencyCode.Empty", "Currency code must not be empty.");
        if (input.Length != 3)
            return Error.Validation(
                "CurrencyCode.Length",
                "Currency code must be exactly 3 characters."
            );
        foreach (var c in input)
            if (c is < 'A' or > 'Z')
                return Error.Validation(
                    "CurrencyCode.Format",
                    "Currency code must be 3 uppercase letters A-Z."
                );
        return new CurrencyCode(input);
    }

    public override string ToString() => Value;
}
