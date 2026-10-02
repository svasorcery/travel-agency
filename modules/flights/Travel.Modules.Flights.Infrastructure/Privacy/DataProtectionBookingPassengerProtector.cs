using System.Security.Cryptography;
using System.Text.Json;
using ErrorOr;
using Travel.Modules.Flights.Application.Privacy;
using Travel.Modules.Flights.Core.ValueObjects;

namespace Travel.Modules.Flights.Infrastructure.Privacy;

public sealed class DataProtectionBookingPassengerProtector(FlightsPiiProtectionProvider provider)
    : IBookingPassengerProtector
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static string[] Purpose(Guid booking, Guid owner) =>
        ["booking-passenger.v1", booking.ToString("N"), owner.ToString("N")];

    public ErrorOr<ProtectedPassengerSnapshot> Protect(
        Guid aggregateId,
        Guid ownerUserId,
        PassengerInfo passenger
    )
    {
        if (aggregateId == Guid.Empty || ownerUserId == Guid.Empty || passenger is null)
            return PiiProtectionErrors.InvalidEnvelope;
        var bytes = JsonSerializer.SerializeToUtf8Bytes(passenger, JsonOptions);
        try
        {
            var encrypted = provider.Protect(bytes, Purpose(aggregateId, ownerUserId));
            return encrypted.IsError
                ? encrypted.Errors
                : ProtectedPassengerSnapshot.Create(1, Convert.ToBase64String(encrypted.Value));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    public ErrorOr<PassengerInfo> Unprotect(
        Guid aggregateId,
        Guid ownerUserId,
        ProtectedPassengerSnapshot snapshot
    )
    {
        if (
            aggregateId == Guid.Empty
            || ownerUserId == Guid.Empty
            || snapshot is null
            || snapshot.FormatVersion != 1
            || string.IsNullOrWhiteSpace(snapshot.Ciphertext)
        )
            return PiiProtectionErrors.PayloadUnavailable;
        try
        {
            var decrypted = provider.Unprotect(
                Convert.FromBase64String(snapshot.Ciphertext),
                Purpose(aggregateId, ownerUserId)
            );
            if (decrypted.IsError)
                return decrypted.Errors;
            try
            {
                var result = JsonSerializer.Deserialize<PassengerInfo>(
                    decrypted.Value,
                    JsonOptions
                );
                if (
                    result is null
                    || string.IsNullOrWhiteSpace(result.GivenName)
                    || string.IsNullOrWhiteSpace(result.FamilyName)
                    || string.IsNullOrWhiteSpace(result.Email)
                    || result.Phone is null
                    || result.Gender is null
                )
                    return PiiProtectionErrors.PayloadUnavailable;
                if (
                    PhoneNumber.Create(result.Phone.Value).IsError
                    || Gender.Parse(result.Gender.Code).IsError
                    || PassengerInfo
                        .Create(
                            result.GivenName,
                            result.FamilyName,
                            result.DateOfBirth,
                            result.Gender,
                            result.Email,
                            result.Phone,
                            DateOnly.MaxValue
                        )
                        .IsError
                )
                    return PiiProtectionErrors.PayloadUnavailable;
                return result;
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
