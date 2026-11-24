using System.Globalization;
using System.Security.Cryptography;
using ErrorOr;
using Travel.Modules.Trips.Application.HotelLinks;
using Travel.Modules.Trips.Core;

namespace Travel.Modules.Trips.Infrastructure.Privacy;

public sealed class TripHotelLinkProtector(TripsProtectionProvider provider)
    : ITripHotelLinkProtector
{
    internal const int MaximumEnvelopeLength = 512;
    public bool IsReady => provider.IsReady;

    public ErrorOr<string> Protect(Guid? reservationId, TripHotelLinkBinding binding)
    {
        if (!Valid(binding) || reservationId == Guid.Empty)
            return TripErrors.Unavailable;

        // An all-zero UUID encodes an empty slot at the same length as an occupied one.
        var payload = (reservationId ?? Guid.Empty).ToByteArray(bigEndian: true);
        try
        {
            var protectedBytes = provider.Protect(payload, Purpose(binding));
            if (protectedBytes.IsError)
                return TripErrors.Unavailable;
            var envelope = Convert.ToBase64String(protectedBytes.Value);
            return envelope.Length <= MaximumEnvelopeLength ? envelope : TripErrors.Unavailable;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
        }
    }

    public ErrorOr<Guid?> Unprotect(string envelope, TripHotelLinkBinding binding)
    {
        if (
            !Valid(binding)
            || string.IsNullOrEmpty(envelope)
            || envelope.Length > MaximumEnvelopeLength
        )
            return TripErrors.Unavailable;

        var bytes = new byte[envelope.Length / 4 * 3];
        if (
            !Convert.TryFromBase64String(envelope, bytes, out var byteCount)
            || !string.Equals(
                Convert.ToBase64String(bytes, 0, byteCount),
                envelope,
                StringComparison.Ordinal
            )
        )
            return TripErrors.Unavailable;

        var unprotected = provider.Unprotect(
            bytes.AsSpan(0, byteCount).ToArray(),
            Purpose(binding)
        );
        if (unprotected.IsError)
            return TripErrors.Unavailable;

        var payload = unprotected.Value;
        try
        {
            if (payload.Length != 16)
                return TripErrors.Unavailable;
            var reservationId = new Guid(payload, bigEndian: true);
            return reservationId == Guid.Empty ? (Guid?)null : reservationId;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
        }
    }

    private static bool Valid(TripHotelLinkBinding binding) =>
        binding.Owner is not null && binding.TripId != Guid.Empty && binding.Revision > 0;

    private static string[] Purpose(TripHotelLinkBinding binding) =>
        [
            "trip-hotel-link",
            "1",
            binding.Owner.Issuer,
            binding.Owner.Subject.ToString("N"),
            binding.TripId.ToString("N"),
            binding.Revision.ToString(CultureInfo.InvariantCulture),
        ];
}
