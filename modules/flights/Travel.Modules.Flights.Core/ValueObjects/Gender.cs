using System.Text.Json.Serialization;
using ErrorOr;

namespace Travel.Modules.Flights.Core.ValueObjects;

public sealed record Gender
{
    public string Code { get; }

    [JsonConstructor]
    private Gender(string code) => Code = code;

    public static Gender Male { get; } = new("male");
    public static Gender Female { get; } = new("female");
    public static Gender Unspecified { get; } = new("unspecified");

    public static ErrorOr<Gender> Parse(string input) =>
        input?.ToLowerInvariant() switch
        {
            "m" or "male" => Male,
            "f" or "female" => Female,
            "u" or "unspecified" => Unspecified,
            _ => Error.Validation("Gender.Unknown", $"Unknown gender '{input}'."),
        };

    public override string ToString() => Code;
}
