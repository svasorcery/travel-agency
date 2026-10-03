using System.Security.Cryptography;
using System.Text.Json;
using ErrorOr;
using Travel.Modules.Flights.Application.Privacy;
using Travel.Modules.Flights.Core.ValueObjects;

namespace Travel.Modules.Flights.Infrastructure.Privacy;

public sealed class DataProtectionSavedTravelerProtector(FlightsPiiProtectionProvider provider)
    : ISavedTravelerProtector
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static bool ValidContext(SavedTravelerProtectionContext? context) =>
        context is not null
        && context.OwnerUserId != Guid.Empty
        && context.TravelerId != Guid.Empty
        && context.Revision != Guid.Empty;

    private static string[] Purpose(SavedTravelerProtectionContext context) =>
        [
            "saved-traveler.v1",
            context.OwnerUserId.ToString("N"),
            context.TravelerId.ToString("N"),
            context.Revision.ToString("N"),
        ];

    public ErrorOr<ProtectedSavedTravelerSnapshot> Protect(
        SavedTravelerProtectionContext context,
        SavedTravelerDetails details
    )
    {
        if (!ValidContext(context) || details is null || details.Validate().IsError)
            return PiiProtectionErrors.InvalidEnvelope;
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(details, JsonOptions);
        try
        {
            var encrypted = provider.Protect(plaintext, Purpose(context));
            return encrypted.IsError
                ? encrypted.Errors
                : ProtectedSavedTravelerSnapshot.Create(1, Convert.ToBase64String(encrypted.Value));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public ErrorOr<SavedTravelerDetails> Unprotect(
        SavedTravelerProtectionContext context,
        ProtectedSavedTravelerSnapshot snapshot
    )
    {
        if (
            !ValidContext(context)
            || snapshot is null
            || ProtectedSavedTravelerSnapshot
                .Create(snapshot.FormatVersion, snapshot.Ciphertext)
                .IsError
        )
            return PiiProtectionErrors.PayloadUnavailable;
        try
        {
            var decrypted = provider.Unprotect(
                Convert.FromBase64String(snapshot.Ciphertext),
                Purpose(context)
            );
            if (decrypted.IsError)
                return decrypted.Errors;
            try
            {
                var details = JsonSerializer.Deserialize<SavedTravelerDetails>(
                    decrypted.Value,
                    JsonOptions
                );
                return details is not null && !details.Validate().IsError
                    ? details
                    : PiiProtectionErrors.PayloadUnavailable;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(decrypted.Value);
            }
        }
        catch (Exception ex) when (ex is JsonException or FormatException or ArgumentException)
        {
            return PiiProtectionErrors.PayloadUnavailable;
        }
    }
}
