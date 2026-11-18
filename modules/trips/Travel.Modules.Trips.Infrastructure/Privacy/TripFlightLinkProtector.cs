using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ErrorOr;
using Travel.Modules.Trips.Application.FlightLinks;
using Travel.Modules.Trips.Core;

namespace Travel.Modules.Trips.Infrastructure.Privacy;

public sealed class TripFlightLinkProtector(TripsProtectionProvider provider)
    : ITripFlightLinkProtector
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    public bool IsReady => provider.IsReady;

    public ErrorOr<string> Protect(string plaintext, TripFlightLinkBinding binding)
    {
        if (
            !Valid(binding)
            || string.IsNullOrEmpty(plaintext)
            || plaintext.Length > ITripFlightLinkProtector.MaximumBytes
        )
            return TripErrors.Unavailable;
        byte[]? bytes = null;
        try
        {
            bytes = Utf8.GetBytes(plaintext);
            if (bytes.Length > ITripFlightLinkProtector.MaximumBytes)
                return TripErrors.Unavailable;
            var result = provider.Protect(bytes, Purpose(binding));
            if (result.IsError)
                return TripErrors.Unavailable;
            var envelope = Convert.ToBase64String(result.Value);
            return envelope.Length <= ITripFlightLinkProtector.MaximumBytes
                ? envelope
                : TripErrors.Unavailable;
        }
        catch (EncoderFallbackException)
        {
            return TripErrors.Unavailable;
        }
        finally
        {
            if (bytes is not null)
                CryptographicOperations.ZeroMemory(bytes);
        }
    }

    public ErrorOr<string> Unprotect(string envelope, TripFlightLinkBinding binding)
    {
        if (
            !Valid(binding)
            || string.IsNullOrEmpty(envelope)
            || envelope.Length > ITripFlightLinkProtector.MaximumBytes
        )
            return TripErrors.Unavailable;
        try
        {
            var bytes = Convert.FromBase64String(envelope);
            if (!string.Equals(Convert.ToBase64String(bytes), envelope, StringComparison.Ordinal))
                return TripErrors.Unavailable;
            var result = provider.Unprotect(bytes, Purpose(binding));
            if (result.IsError)
                return TripErrors.Unavailable;
            try
            {
                return Utf8.GetString(result.Value);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(result.Value);
            }
        }
        catch (Exception ex) when (ex is FormatException or DecoderFallbackException)
        {
            return TripErrors.Unavailable;
        }
    }

    private static bool Valid(TripFlightLinkBinding binding) =>
        binding.Owner is not null && binding.TripId != Guid.Empty && binding.Revision > 0;

    private static string[] Purpose(TripFlightLinkBinding binding) =>
        [
            "trip-flight-links",
            "1",
            binding.Owner.Issuer,
            binding.Owner.Subject.ToString("N"),
            binding.TripId.ToString("N"),
            binding.Revision.ToString(CultureInfo.InvariantCulture),
        ];
}
