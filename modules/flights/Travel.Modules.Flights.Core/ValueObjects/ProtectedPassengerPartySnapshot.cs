using System.Text.Json.Serialization;
using ErrorOr;

namespace Travel.Modules.Flights.Core.ValueObjects;

public sealed record ProtectedPassengerPartySnapshot
{
    public int FormatVersion { get; }
    public string Ciphertext { get; }

    [JsonConstructor]
    private ProtectedPassengerPartySnapshot(int formatVersion, string ciphertext)
    {
        FormatVersion = formatVersion;
        Ciphertext = ciphertext;
    }

    public static ErrorOr<ProtectedPassengerPartySnapshot> Create(
        int formatVersion,
        string ciphertext
    ) =>
        formatVersion == 1 && !string.IsNullOrWhiteSpace(ciphertext)
            ? new ProtectedPassengerPartySnapshot(formatVersion, ciphertext)
            : Error.Validation(
                "Flights.PiiEnvelopeInvalid",
                "Protected passenger party format is invalid."
            );

    public override string ToString() => "ProtectedPassengerPartySnapshot";
}
