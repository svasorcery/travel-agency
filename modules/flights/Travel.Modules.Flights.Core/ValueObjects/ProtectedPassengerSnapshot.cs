using System.Text.Json.Serialization;
using ErrorOr;

namespace Travel.Modules.Flights.Core.ValueObjects;

public sealed record ProtectedPassengerSnapshot
{
    public int FormatVersion { get; }
    public string Ciphertext { get; }

    [JsonConstructor]
    private ProtectedPassengerSnapshot(int formatVersion, string ciphertext)
    {
        FormatVersion = formatVersion;
        Ciphertext = ciphertext;
    }

    public static ErrorOr<ProtectedPassengerSnapshot> Create(
        int formatVersion,
        string ciphertext
    ) =>
        formatVersion == 1 && !string.IsNullOrWhiteSpace(ciphertext)
            ? new ProtectedPassengerSnapshot(formatVersion, ciphertext)
            : Error.Validation(
                "Flights.PiiEnvelopeInvalid",
                "Protected passenger format is invalid."
            );

    public override string ToString() => "ProtectedPassengerSnapshot";
}
