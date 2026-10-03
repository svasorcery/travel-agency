using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using ErrorOr;
using Travel.Modules.Flights.Application.Privacy;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Shared.Abstractions;

namespace Travel.Modules.Flights.Infrastructure.Privacy;

public sealed class DataProtectionBookingPassengerPartyProtector(
    FlightsPiiProtectionProvider provider
) : IBookingPassengerPartyProtector
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static bool ValidContext(BookingPassengerPartyProtectionContext? context) =>
        context is not null
        && context.AggregateId != Guid.Empty
        && context.OwnerUserId != Guid.Empty
        && context.QuoteRevision != Guid.Empty
        && context.PassengerCount is >= 1 and <= 9;

    private static string[] Purpose(BookingPassengerPartyProtectionContext context) =>
        [
            "booking-party.v1",
            context.AggregateId.ToString("N"),
            context.OwnerUserId.ToString("N"),
            context.QuoteRevision.ToString("N"),
            context.PassengerCount.ToString(CultureInfo.InvariantCulture),
        ];

    private static bool ValidParty(
        BookingPassengerPartyProtectionContext context,
        EquatableArray<BookingPassenger> passengers
    ) =>
        passengers.Count == context.PassengerCount
        && passengers.All(p => p is not null && !BookingPassenger.Create(p.Id, p.Details).IsError)
        && passengers.Select(p => p.Id).Distinct().Count() == passengers.Count;

    public ErrorOr<ProtectedPassengerPartySnapshot> Protect(
        BookingPassengerPartyProtectionContext context,
        EquatableArray<BookingPassenger> passengers
    )
    {
        passengers = new(passengers.ToArray());
        if (!ValidContext(context) || !ValidParty(context, passengers))
            return PiiProtectionErrors.InvalidEnvelope;
        // Serialize only local IDs/details: supplier references never enter this payload.
        var bytes = JsonSerializer.SerializeToUtf8Bytes(passengers, JsonOptions);
        try
        {
            var protectedBytes = provider.Protect(bytes, Purpose(context));
            return protectedBytes.IsError
                ? protectedBytes.Errors
                : ProtectedPassengerPartySnapshot.Create(
                    1,
                    Convert.ToBase64String(protectedBytes.Value)
                );
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    public ErrorOr<EquatableArray<BookingPassenger>> Unprotect(
        BookingPassengerPartyProtectionContext context,
        ProtectedPassengerPartySnapshot snapshot
    )
    {
        if (
            !ValidContext(context)
            || snapshot is null
            || snapshot.FormatVersion != 1
            || string.IsNullOrWhiteSpace(snapshot.Ciphertext)
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
                var passengers = JsonSerializer.Deserialize<EquatableArray<BookingPassenger>>(
                    decrypted.Value,
                    JsonOptions
                );
                return ValidParty(context, passengers)
                    ? new EquatableArray<BookingPassenger>(passengers.ToArray())
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
