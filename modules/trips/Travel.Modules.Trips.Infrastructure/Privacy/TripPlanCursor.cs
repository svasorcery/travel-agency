using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using ErrorOr;
using Microsoft.AspNetCore.WebUtilities;
using Travel.Modules.Trips.Application;
using Travel.Modules.Trips.Core;

namespace Travel.Modules.Trips.Infrastructure.Privacy;

public sealed class TripPlanCursor(TripsProtectionProvider provider) : ITripPlanCursor
{
    private static byte[] OwnerHash(OwnerIdentity owner) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(owner.Issuer + "\n" + owner.Subject.ToString("N")));

    public ErrorOr<string> Encode(OwnerIdentity owner, TripPlanPosition position)
    {
        var bytes = new byte[56];
        OwnerHash(owner).CopyTo(bytes, 0);
        BinaryPrimitives.WriteInt64BigEndian(bytes.AsSpan(32), position.UpdatedAt.UtcTicks);
        position.TripId.TryWriteBytes(bytes.AsSpan(40));
        var result = provider.Protect(bytes, "trip-list-cursor.v1");
        return result.IsError ? TripErrors.Unavailable : WebEncoders.Base64UrlEncode(result.Value);
    }

    public ErrorOr<TripPlanPosition> Decode(OwnerIdentity owner, string cursor)
    {
        if (string.IsNullOrEmpty(cursor) || cursor.Length > 256)
            return TripErrors.InvalidCursor;
        try
        {
            var result = provider.Unprotect(
                WebEncoders.Base64UrlDecode(cursor),
                "trip-list-cursor.v1"
            );
            if (
                result.IsError
                || result.Value.Length != 56
                || !CryptographicOperations.FixedTimeEquals(
                    result.Value.AsSpan(0, 32),
                    OwnerHash(owner)
                )
            )
                return TripErrors.InvalidCursor;
            var id = new Guid(result.Value.AsSpan(40));
            return id == Guid.Empty
                ? TripErrors.InvalidCursor
                : new TripPlanPosition(
                    new DateTimeOffset(
                        BinaryPrimitives.ReadInt64BigEndian(result.Value.AsSpan(32)),
                        TimeSpan.Zero
                    ),
                    id
                );
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            return TripErrors.InvalidCursor;
        }
    }
}
