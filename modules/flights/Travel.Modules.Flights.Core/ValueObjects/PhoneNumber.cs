using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ErrorOr;

namespace Travel.Modules.Flights.Core.ValueObjects;

public sealed record PhoneNumber
{
    private static readonly Regex E164 = new(@"^\+[1-9]\d{7,14}$", RegexOptions.Compiled);
    public string Value { get; }

    [JsonConstructor]
    private PhoneNumber(string value) => Value = value;

    public static ErrorOr<PhoneNumber> Create(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return Error.Validation("PhoneNumber.Empty", "Phone is required.");
        if (!E164.IsMatch(input))
            return Error.Validation(
                "PhoneNumber.Format",
                "Phone must be in E.164 format (e.g., +79161234567)."
            );
        return new PhoneNumber(input);
    }

    public override string ToString() => Value;
}
