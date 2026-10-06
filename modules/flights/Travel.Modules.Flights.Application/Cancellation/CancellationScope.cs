using System.Security.Cryptography;
using System.Text.Json;
using ErrorOr;
using Travel.Modules.Flights.Core.Aggregates;

namespace Travel.Modules.Flights.Application.Cancellation;

public static class CancellationScope
{
    public static ErrorOr<string> Create(BookingAggregate booking)
    {
        if (
            booking.OwnerUserId is null
            || booking.OwnerUserId == Guid.Empty
            || string.IsNullOrWhiteSpace(booking.ProviderOrderId)
            || booking.Itinerary is null
            || booking.Itinerary.Slices.Count == 0
            || booking.PassengerCount is < 1 or > 9
        )
            return Error.Validation(
                "Flights.CancellationScopeUnavailable",
                "Whole-order metadata unavailable."
            );
        var normalized = new
        {
            Version = 1,
            Order = booking.ProviderOrderId,
            Owner = booking.OwnerUserId.Value.ToString("N"),
            Count = booking.PassengerCount,
            HeldRevision = booking.HeldQuoteRevision?.ToString("N"),
            Slots = booking
                .QuoteBinding?.Slots.Select(s => new
                {
                    Id = s.Id.Value.ToString("N"),
                    Reference = s.SupplierReference.Value,
                    Kind = s.Kind.ToString(),
                })
                .ToArray(),
            PartyEnvelope = booking.ProtectedPassengerParty?.Ciphertext
                ?? booking.ProtectedPassenger?.Ciphertext,
            Legs = booking
                .Itinerary.Slices.Select(s => new
                {
                    Origin = s.Origin.Value,
                    Destination = s.Destination.Value,
                    Segments = s
                        .Segments.Select(segment => new
                        {
                            Origin = segment.Origin.Value,
                            Destination = segment.Destination.Value,
                            DepartUtc = segment.DepartAt.UtcTicks,
                            DepartOffset = segment.DepartAt.Offset.Ticks,
                            ArriveUtc = segment.ArriveAt.UtcTicks,
                            ArriveOffset = segment.ArriveAt.Offset.Ticks,
                            Carrier = segment.CarrierCode,
                            Flight = segment.FlightNumber,
                            Cabin = segment.Cabin.Code,
                        })
                        .ToArray(),
                })
                .ToArray(),
        };
        return Convert
            .ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(normalized)))
            .ToLowerInvariant();
    }
}
