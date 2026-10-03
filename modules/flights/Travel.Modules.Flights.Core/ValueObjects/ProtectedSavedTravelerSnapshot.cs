using System.Text.Json.Serialization;
using ErrorOr;

namespace Travel.Modules.Flights.Core.ValueObjects;

public sealed record ProtectedSavedTravelerSnapshot
{
    public const int MaxCiphertextLength = 32 * 1024;
    public int FormatVersion { get; }
    public string Ciphertext { get; }

    [JsonConstructor]
    private ProtectedSavedTravelerSnapshot(int formatVersion, string ciphertext)
    {
        FormatVersion = formatVersion;
        Ciphertext = ciphertext;
    }

    public static ErrorOr<ProtectedSavedTravelerSnapshot> Create(
        int formatVersion,
        string ciphertext
    ) =>
        formatVersion == 1
        && !string.IsNullOrWhiteSpace(ciphertext)
        && ciphertext.Length <= MaxCiphertextLength
            ? new ProtectedSavedTravelerSnapshot(formatVersion, ciphertext)
            : Error.Validation(
                "Flights.PiiEnvelopeInvalid",
                "Protected saved traveler format is invalid."
            );

    public override string ToString() => "ProtectedSavedTravelerSnapshot";
}
