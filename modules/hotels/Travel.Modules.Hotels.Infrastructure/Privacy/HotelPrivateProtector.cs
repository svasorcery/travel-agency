using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ErrorOr;
using Travel.Modules.Hotels.Application;
using Travel.Modules.Hotels.Core;

namespace Travel.Modules.Hotels.Infrastructure.Privacy;

public sealed class HotelPrivateProtector(HotelPiiProtectionProvider provider)
    : IHotelPrivateProtector
{
    private sealed record Envelope(int Format, string Ciphertext);

    private static readonly UTF8Encoding Utf8 = new(false, true);

    private static bool Valid(HotelPrivacyBinding b) =>
        b.OwnerId != Guid.Empty
        && b.ReservationId != Guid.Empty
        && b.QuoteRevision > 0
        && b.Guests is 1 or 2
        && b.Kind is "party" or "context" or "webhook";

    private static string[] Purpose(HotelPrivacyBinding b) =>
        [
            "hotel-private.v1",
            b.OwnerId.ToString("N"),
            b.ReservationId.ToString("N"),
            b.QuoteRevision.ToString(CultureInfo.InvariantCulture),
            b.Guests.ToString(CultureInfo.InvariantCulture),
            b.Kind,
        ];

    public ErrorOr<string> Protect(string plaintext, HotelPrivacyBinding binding)
    {
        if (!Valid(binding) || plaintext.Length > 16384)
            return HotelErrors.Invalid;
        var bytes = Utf8.GetBytes(plaintext);
        try
        {
            var protectedValue = provider.Protect(bytes, Purpose(binding));
            return protectedValue.IsError
                ? HotelErrors.Unavailable
                : JsonSerializer.Serialize(
                    new Envelope(1, Convert.ToBase64String(protectedValue.Value))
                );
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    public ErrorOr<string> Unprotect(string envelope, HotelPrivacyBinding binding)
    {
        if (!Valid(binding) || envelope.Length > 32768)
            return HotelErrors.Unavailable;
        try
        {
            var parsed = JsonSerializer.Deserialize<Envelope>(envelope);
            if (parsed is null || parsed.Format != 1)
                return HotelErrors.Unavailable;
            var bytes = provider.Unprotect(
                Convert.FromBase64String(parsed.Ciphertext),
                Purpose(binding)
            );
            if (bytes.IsError)
                return HotelErrors.Unavailable;
            try
            {
                return Utf8.GetString(bytes.Value);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(bytes.Value);
            }
        }
        catch (Exception ex) when (ex is JsonException or FormatException or ArgumentException)
        {
            return HotelErrors.Unavailable;
        }
    }
}
