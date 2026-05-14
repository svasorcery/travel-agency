using System.Text.Json.Serialization;
using ErrorOr;

namespace Travel.Modules.Flights.Core.ValueObjects;

public sealed record IataCode
{
    public string Value { get; }

    [JsonConstructor]
    private IataCode(string value) => Value = value;

    public static ErrorOr<IataCode> Create(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return Error.Validation("IataCode.Empty", "IATA code must not be empty.");
        if (input.Length != 3)
            return Error.Validation("IataCode.Length", "IATA code must be exactly 3 characters.");
        foreach (var c in input)
            if (c is < 'A' or > 'Z')
                return Error.Validation(
                    "IataCode.Format",
                    "IATA code must be 3 uppercase letters A-Z."
                );
        return new IataCode(input);
    }

    public override string ToString() => Value;
}
