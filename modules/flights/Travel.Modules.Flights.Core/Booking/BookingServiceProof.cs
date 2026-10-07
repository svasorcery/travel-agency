using Travel.Modules.Flights.Core.Providers.Dtos;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Offer;
using Travel.Shared.Abstractions;

namespace Travel.Modules.Flights.Core.Booking;

public sealed record BookingOrderContext(
    BookableOffer Offer,
    QuoteBinding Binding,
    BookingPurchase Purchase,
    Guid AttemptId
);

public sealed record BookingServiceProof(
    Guid QuoteRevision,
    string ProviderOrderId,
    EquatableArray<BookingService> Services
)
{
    public static bool ValidLines(EquatableArray<BookingService> lines) =>
        lines.Count <= 256
        && lines.All(s =>
            s is not null
            && s.Reference is { Length: > 0 and <= 256 }
            && s.Reference.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-')
            && Enum.IsDefined(s.Kind)
            && s.PassengerId != Guid.Empty
            && s.Quantity is >= 1 and <= 99
            && s.LineTotal is { Currency: not null, Amount: >= 0 }
            && !CurrencyCode.Create(s.LineTotal.Currency.Value).IsError
            && s.Segments.Count is > 0 and <= 64
            && s.Segments.Distinct().Count() == s.Segments.Count
            && s.Segments.All(a => a.Leg is >= 0 and <= 3 && a.Segment is >= 0 and <= 63)
            && (
                s.Baggage is null
                || s.Baggage.MaximumWeightKg is null or >= 0
                    && s.Baggage.MaximumHeightCm is null or >= 0
                    && s.Baggage.MaximumDepthCm is null or >= 0
                    && s.Baggage.MaximumLengthCm is null or >= 0
            )
            && (
                s.Disclosures is not { } allDisclosures
                || allDisclosures.Count <= 32
                    && allDisclosures.All(d =>
                        !string.IsNullOrWhiteSpace(d)
                        && d.Length <= 2000
                        && !d.Any(c => char.IsControl(c) && c != '\n')
                    )
            )
            && (
                s.Kind != BookingServiceKind.Seat
                || s.Quantity == 1
                    && s.Segments.Count == 1
                    && s.SeatDesignator is { Length: > 0 and <= 16 }
                    && !s.SeatDesignator.Any(char.IsControl)
                    && s.Disclosures is not null
            )
        )
        && lines.Select(s => s.Reference).Distinct(StringComparer.Ordinal).Count() == lines.Count;

    public static EquatableArray<BookingService> Copy(EquatableArray<BookingService> services) =>
        new(
            services
                .Select(s =>
                    s with
                    {
                        Segments = new(s.Segments.ToArray()),
                        Disclosures = s.Disclosures is { } d ? new(d.ToArray()) : null,
                    }
                )
                .ToArray()
        );

    public bool Matches(BookingPurchase expected, string orderId) =>
        QuoteRevision == expected.QuoteRevision
        && ProviderOrderId == orderId
        && ValidLines(Services)
        && BookedOrderFacts.ServicesMatch(Services, expected.Services);
}
