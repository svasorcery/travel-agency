using System.Buffers;
using System.Globalization;
using System.Text;
using ErrorOr;

namespace Travel.Modules.Trips.Core;

public sealed record OwnerIdentity
{
    public string Issuer { get; }
    public Guid Subject { get; }

    private OwnerIdentity(string issuer, Guid subject) => (Issuer, Subject) = (issuer, subject);

    public static ErrorOr<OwnerIdentity> Create(string? issuer, Guid subject) =>
        subject != Guid.Empty
        && !string.IsNullOrWhiteSpace(issuer)
        && issuer.Length <= 512
        && issuer == issuer.Trim()
        && Uri.TryCreate(issuer, UriKind.Absolute, out var uri)
        && uri.Scheme is "http" or "https"
            ? new OwnerIdentity(issuer, subject)
            : TripErrors.Forbidden;
}

public sealed record TripDateRange
{
    public DateOnly StartDate { get; }
    public DateOnly EndDate { get; }
    public int DayCount => EndDate.DayNumber - StartDate.DayNumber + 1;

    private TripDateRange(DateOnly startDate, DateOnly endDate) =>
        (StartDate, EndDate) = (startDate, endDate);

    public static ErrorOr<TripDateRange> Create(DateOnly startDate, DateOnly endDate) =>
        endDate.DayNumber - startDate.DayNumber is >= 0 and < 14
            ? new TripDateRange(startDate, endDate)
            : TripErrors.InvalidInput;
}

public sealed record PartySize
{
    public int Adults { get; }

    private PartySize(int adults) => Adults = adults;

    public static ErrorOr<PartySize> Create(int adults) =>
        adults is >= 1 and <= 9 ? new PartySize(adults) : TripErrors.InvalidInput;
}

public sealed record TripBlockId
{
    public Guid Value { get; }

    private TripBlockId(Guid value) => Value = value;

    public static ErrorOr<TripBlockId> Create(Guid value) =>
        value != Guid.Empty ? new TripBlockId(value) : TripErrors.InvalidInput;
}

public sealed record DestinationSelection(
    string DestinationId,
    string DisplayName,
    string TimeZone,
    string CatalogueVersion
);

public static class CityCatalogue
{
    public const string Version = "1";
    public static IReadOnlyList<DestinationSelection> Cities { get; } =
        Array.AsReadOnly(
            new[]
            {
                new DestinationSelection("moscow", "Москва", "Europe/Moscow", Version),
                new DestinationSelection(
                    "saint-petersburg",
                    "Санкт-Петербург",
                    "Europe/Moscow",
                    Version
                ),
                new DestinationSelection(
                    "kaliningrad",
                    "Калининград",
                    "Europe/Kaliningrad",
                    Version
                ),
            }
        );

    public static ErrorOr<DestinationSelection> Find(string? destinationId) =>
        Cities.FirstOrDefault(x => x.DestinationId == destinationId) is { } city
            ? city
            : TripErrors.InvalidInput;
}

public sealed record PlanningMoneyInput(string Amount, string Currency);

public sealed record PlanningMoney
{
    public const long MaximumMinorUnits = 99_999_999_999;
    public long MinorUnits { get; }
    public string Currency { get; }
    public string Amount => FormatMinorUnits(MinorUnits);

    private PlanningMoney(long minorUnits, string currency) =>
        (MinorUnits, Currency) = (minorUnits, currency);

    public static bool IsSupportedCurrency(string? currency) => currency is "RUB" or "EUR" or "USD";

    public static ErrorOr<PlanningMoney> Create(string? amount, string? currency)
    {
        if (!IsSupportedCurrency(currency) || string.IsNullOrEmpty(amount) || amount.Length > 12)
            return TripErrors.InvalidInput;
        var dot = amount.IndexOf('.');
        var integer = dot < 0 ? amount : amount[..dot];
        if (
            integer.Length is < 1 or > 9
            || (integer.Length > 1 && integer[0] == '0')
            || integer.Any(c => c is < '0' or > '9')
            || (
                dot >= 0
                && (amount.Length != dot + 3 || amount[(dot + 1)..].Any(c => c is < '0' or > '9'))
            )
        )
            return TripErrors.InvalidInput;
        var units = long.Parse(integer, CultureInfo.InvariantCulture) * 100;
        if (dot >= 0)
            units += long.Parse(amount[(dot + 1)..], CultureInfo.InvariantCulture);
        return units <= MaximumMinorUnits
            ? new PlanningMoney(units, currency!)
            : TripErrors.InvalidInput;
    }

    public static string FormatMinorUnits(long units) =>
        string.Concat(
            (units / 100).ToString(CultureInfo.InvariantCulture),
            ".",
            (units % 100).ToString("D2", CultureInfo.InvariantCulture)
        );
}

public sealed record DayLocalTimeRange
{
    public string StartTime { get; }
    public string EndTime { get; }

    private DayLocalTimeRange(string startTime, string endTime) =>
        (StartTime, EndTime) = (startTime, endTime);

    public static ErrorOr<DayLocalTimeRange> Create(string? startTime, string? endTime)
    {
        if (
            startTime is null
            || endTime is null
            || startTime.Length != 5
            || endTime.Length != 5
            || !TimeOnly.TryParseExact(
                startTime,
                "HH:mm",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var start
            )
            || !TimeOnly.TryParseExact(
                endTime,
                "HH:mm",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var end
            )
            || end <= start
        )
            return TripErrors.InvalidInput;
        return new DayLocalTimeRange(startTime, endTime);
    }
}

internal static class TripText
{
    public static bool IsValid(string? text, int scalarLimit, bool note = false)
    {
        if (
            text is null
            || (!note && string.IsNullOrWhiteSpace(text))
            || text.Length > scalarLimit * 2
        )
            return false;
        var remaining = text.AsSpan();
        var count = 0;
        while (!remaining.IsEmpty)
        {
            if (
                Rune.DecodeFromUtf16(remaining, out var rune, out var consumed)
                != OperationStatus.Done
            )
                return false;
            if (++count > scalarLimit || (Rune.IsControl(rune) && !(note && rune.Value == '\n')))
                return false;
            remaining = remaining[consumed..];
        }
        return true;
    }
}
