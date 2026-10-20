using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ErrorOr;
using Travel.Modules.Trips.Application;
using Travel.Modules.Trips.Core;

namespace Travel.Modules.Trips.Infrastructure.Privacy;

public sealed class DataProtectionTripPlanProtector(TripsProtectionProvider provider)
    : ITripPlanProtector
{
    private static readonly UTF8Encoding Utf8 = new(false, true);

    private static string[] Purpose(TripPlanBinding binding) =>
        [
            "trip-plan-payload",
            binding.Owner.Issuer,
            binding.Owner.Subject.ToString("N"),
            binding.TripId.ToString("N"),
            binding.SchemaVersion.ToString(CultureInfo.InvariantCulture),
            binding.Revision.ToString(CultureInfo.InvariantCulture),
        ];

    private static bool Valid(TripPlanBinding binding) =>
        binding.TripId != Guid.Empty && binding.Revision > 0 && binding.SchemaVersion == 1;

    public Task<ErrorOr<string>> ProtectAsync(
        string plaintext,
        TripPlanBinding binding,
        CancellationToken ct
    )
    {
        ct.ThrowIfCancellationRequested();
        if (!Valid(binding))
            return Task.FromResult<ErrorOr<string>>(TripErrors.InvalidInput);
        byte[]? bytes = null;
        try
        {
            bytes = Utf8.GetBytes(plaintext);
            if (bytes.Length > 128 * 1024)
                return Task.FromResult<ErrorOr<string>>(TripErrors.PlanTooLarge);
            var protectedBytes = provider.Protect(bytes, Purpose(binding));
            return Task.FromResult<ErrorOr<string>>(
                protectedBytes.IsError
                    ? TripErrors.Unavailable
                    : Convert.ToBase64String(protectedBytes.Value)
            );
        }
        catch (EncoderFallbackException)
        {
            return Task.FromResult<ErrorOr<string>>(TripErrors.InvalidInput);
        }
        finally
        {
            if (bytes is not null)
                CryptographicOperations.ZeroMemory(bytes);
        }
    }

    public Task<ErrorOr<string>> UnprotectAsync(
        string envelope,
        TripPlanBinding binding,
        CancellationToken ct
    )
    {
        ct.ThrowIfCancellationRequested();
        if (!Valid(binding) || string.IsNullOrEmpty(envelope) || envelope.Length > 200000)
            return Task.FromResult<ErrorOr<string>>(TripErrors.Unavailable);
        try
        {
            var result = provider.Unprotect(Convert.FromBase64String(envelope), Purpose(binding));
            if (result.IsError)
                return Task.FromResult<ErrorOr<string>>(TripErrors.Unavailable);
            try
            {
                return Task.FromResult<ErrorOr<string>>(
                    result.Value.Length > 128 * 1024
                        ? TripErrors.Unavailable
                        : Utf8.GetString(result.Value)
                );
            }
            finally
            {
                CryptographicOperations.ZeroMemory(result.Value);
            }
        }
        catch (Exception ex)
            when (ex is FormatException or DecoderFallbackException or ArgumentException)
        {
            return Task.FromResult<ErrorOr<string>>(TripErrors.Unavailable);
        }
    }
}
