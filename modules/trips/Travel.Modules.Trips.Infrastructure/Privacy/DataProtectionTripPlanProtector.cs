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
    private const string V2Prefix = "v2:";

    private static string[] Purpose(TripPlanBinding binding, int schemaVersion) =>
        [
            "trip-plan-payload",
            binding.Owner.Issuer,
            binding.Owner.Subject.ToString("N"),
            binding.TripId.ToString("N"),
            schemaVersion.ToString(CultureInfo.InvariantCulture),
            binding.Revision.ToString(CultureInfo.InvariantCulture),
        ];

    private static bool Valid(TripPlanBinding binding) =>
        binding.TripId != Guid.Empty && binding.Revision > 0;

    public Task<ErrorOr<string>> ProtectAsync(
        string plaintext,
        TripPlanBinding binding,
        CancellationToken ct,
        int schemaVersion = 1
    )
    {
        ct.ThrowIfCancellationRequested();
        if (!Valid(binding) || schemaVersion is not (1 or 2))
            return Task.FromResult<ErrorOr<string>>(TripErrors.InvalidInput);
        byte[]? bytes = null;
        try
        {
            bytes = Utf8.GetBytes(plaintext);
            if (bytes.Length > 128 * 1024)
                return Task.FromResult<ErrorOr<string>>(TripErrors.PlanTooLarge);
            var protectedBytes = provider.Protect(bytes, Purpose(binding, schemaVersion));
            return Task.FromResult<ErrorOr<string>>(
                protectedBytes.IsError
                    ? TripErrors.Unavailable
                    : (schemaVersion == 2 ? V2Prefix : "")
                        + Convert.ToBase64String(protectedBytes.Value)
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

    public Task<ErrorOr<UnprotectedTripPlan>> UnprotectAsync(
        string envelope,
        TripPlanBinding binding,
        CancellationToken ct
    )
    {
        ct.ThrowIfCancellationRequested();
        if (!Valid(binding) || string.IsNullOrEmpty(envelope) || envelope.Length > 200000)
            return Task.FromResult<ErrorOr<UnprotectedTripPlan>>(TripErrors.Unavailable);
        var schemaVersion = envelope.StartsWith(V2Prefix, StringComparison.Ordinal) ? 2 : 1;
        var base64 = schemaVersion == 2 ? envelope[V2Prefix.Length..] : envelope;
        try
        {
            // A colon in any unknown prefix is outside the Base64 alphabet.
            var result = provider.Unprotect(
                Convert.FromBase64String(base64),
                Purpose(binding, schemaVersion)
            );
            if (result.IsError)
                return Task.FromResult<ErrorOr<UnprotectedTripPlan>>(TripErrors.Unavailable);
            try
            {
                return Task.FromResult<ErrorOr<UnprotectedTripPlan>>(
                    result.Value.Length > 128 * 1024
                        ? TripErrors.Unavailable
                        : new UnprotectedTripPlan(Utf8.GetString(result.Value), schemaVersion)
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
            return Task.FromResult<ErrorOr<UnprotectedTripPlan>>(TripErrors.Unavailable);
        }
    }
}
