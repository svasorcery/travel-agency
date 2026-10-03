using System.Text.Json.Serialization;
using ErrorOr;

namespace Travel.Modules.Flights.Core.ValueObjects;

public sealed record PassengerTitle
{
    public string Code { get; }

    [JsonConstructor]
    private PassengerTitle(string code) => Code = code;

    public static ErrorOr<PassengerTitle> Create(string code) =>
        code is "mr" or "ms" or "mrs" or "miss" or "dr"
            ? new PassengerTitle(code)
            : Error.Validation(
                "Flights.PassengerTitleInvalid",
                "Select a supported passenger title."
            );

    public override string ToString() => Code;
}
