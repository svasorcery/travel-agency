using ErrorOr;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Shared.Abstractions;

namespace Travel.Modules.Flights.Core.Booking;

public readonly record struct BookingSegmentAddress(int Leg, int Segment);

public enum BookingServiceKind
{
    CheckedBaggage = 1,
    Seat = 2,
}

public sealed record BaggageLimits(
    decimal? MaximumWeightKg = null,
    decimal? MaximumHeightCm = null,
    decimal? MaximumDepthCm = null,
    decimal? MaximumLengthCm = null
);

public sealed record BookingService(
    string Reference,
    BookingServiceKind Kind,
    Guid PassengerId,
    EquatableArray<BookingSegmentAddress> Segments,
    int Quantity,
    Money LineTotal,
    Money? UnitPrice = null,
    string? SeatDesignator = null,
    string? PhysicalSeat = null,
    string? Name = null,
    EquatableArray<string>? Disclosures = null,
    BaggageLimits? Baggage = null
);

public sealed record BookingPurchase(
    Guid QuoteRevision,
    Guid? OwnerId,
    Money BaseFare,
    Money Extras,
    Money Total,
    EquatableArray<BookingService> Services,
    DateTimeOffset ExpiresAt,
    string NoticeVersion = "booking-services-v1"
)
{
    public bool HasServices => Services.Count > 0;

    public static ErrorOr<BookingPurchase> Create(
        Guid revision,
        Guid? owner,
        Money fare,
        DateTimeOffset expiresAt,
        IEnumerable<BookingService> services
    )
    {
        var invalid = Error.Validation("Flights.PurchaseInvalid", "Selected purchase is invalid.");
        if (
            revision == Guid.Empty
            || owner == Guid.Empty
            || fare is null
            || fare.Currency is null
            || fare.Amount < 0
            || expiresAt == default
            || services is null
        )
            return invalid;
        var lines = services
            .Select(line =>
                line is null
                    ? null!
                    : line with
                    {
                        Segments = new(line.Segments.ToArray()),
                        Disclosures = line.Disclosures is { } disclosures
                            ? new(disclosures.ToArray())
                            : null,
                    }
            )
            .ToArray();
        if (
            lines.Length > 256
            || lines.Length > 0 && owner is null
            || lines.Any(line => !ValidLine(line, fare.Currency))
            || lines.Select(s => s.Reference).Distinct(StringComparer.Ordinal).Count()
                != lines.Length
        )
            return invalid;
        var seats = lines.Where(s => s.Kind == BookingServiceKind.Seat).ToArray();
        if (
            seats.GroupBy(s => (s.PassengerId, s.Segments[0])).Any(g => g.Count() > 1)
            || seats.GroupBy(s => (s.Segments[0], Key(s))).Any(g => g.Count() > 1)
        )
            return invalid;
        try
        {
            decimal extras = 0;
            foreach (var line in lines)
                extras = checked(extras + line.LineTotal.Amount);
            var total = checked(fare.Amount + extras);
            return new BookingPurchase(
                revision,
                owner,
                fare,
                Money.Create(extras, fare.Currency).Value,
                Money.Create(total, fare.Currency).Value,
                new(lines),
                expiresAt
            );
        }
        catch (OverflowException)
        {
            return Error.Validation(
                "Flights.PurchaseAmountInvalid",
                "Purchase total is unsupported."
            );
        }
    }

    private static string Key(BookingService s) =>
        (s.PhysicalSeat ?? s.SeatDesignator!).ToUpperInvariant();

    private static bool ValidLine(BookingService? line, CurrencyCode currency)
    {
        if (
            line is null
            || string.IsNullOrWhiteSpace(line.Reference)
            || line.Reference.Length > 256
            || line.Reference.Any(char.IsControl)
            || !Enum.IsDefined(line.Kind)
            || line.PassengerId == Guid.Empty
            || line.Quantity < 1
            || line.Quantity > 99
            || line.LineTotal is null
            || line.LineTotal.Amount < 0
            || line.LineTotal.Currency != currency
            || line.UnitPrice is null
            || line.UnitPrice.Amount < 0
            || line.UnitPrice.Currency != currency
            || line.Segments.Count == 0
            || line.Segments.Count > 64
            || line.Segments.Any(s => s.Leg is < 0 or > 3 || s.Segment is < 0 or > 63)
            || line.Segments.Distinct().Count() != line.Segments.Count
        )
            return false;
        if (
            line.Kind == BookingServiceKind.Seat
            && (
                line.Quantity != 1
                || line.Segments.Count != 1
                || string.IsNullOrWhiteSpace(line.SeatDesignator)
                || line.SeatDesignator.Length > 16
            )
        )
            return false;
        if (
            line.Disclosures is { } disclosures
            && (
                disclosures.Count > 32
                || disclosures.Any(s =>
                    string.IsNullOrWhiteSpace(s)
                    || s.Length > 2000
                    || s.Any(c => char.IsControl(c) && c != '\n')
                )
            )
        )
            return false;
        try
        {
            return checked(line.UnitPrice.Amount * line.Quantity) == line.LineTotal.Amount;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    public static BookingPurchase Empty(Guid revision, Money fare, DateTimeOffset expiresAt) =>
        new(revision, null, fare, Money.Create(0, fare.Currency).Value, fare, new([]), expiresAt);
}
