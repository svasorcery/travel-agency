using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using ErrorOr;

namespace Travel.Modules.Rail.Core;

public sealed record StationRef
{
    private StationRef(string source, string code) => (Source, Code) = (source, code);

    public string Source { get; }
    public string Code { get; }

    public static ErrorOr<StationRef> Create(string source, string code) =>
        !string.IsNullOrWhiteSpace(source)
        && Encoding.UTF8.GetByteCount(source) <= 128
        && !string.IsNullOrWhiteSpace(code)
        && Encoding.UTF8.GetByteCount(code) <= 128
        && !code.Any(char.IsControl)
            ? new StationRef(source, code)
            : Error.Validation("Rail.Ref.Invalid");
}

public sealed record RailStation(
    StationRef Reference,
    string Name,
    string? Settlement,
    string? Region
);

public sealed record RailRequest(
    StationRef From,
    StationRef To,
    string CatalogueGeneration,
    string DepartureDate,
    string Transport
);

public sealed record RailAttribution(
    string FrameUrl,
    string Text,
    string Url,
    int Width = 240,
    int Height = 130
);

public sealed record RailPrice(
    string State,
    string? Amount,
    string? Currency,
    string? Reason,
    DateTimeOffset ReceivedAt
);

public sealed record RailFare(string? Name, RailPrice Price);

public sealed record TicketInformation(
    string ElectronicTicket,
    string? ElectronicTicketReason,
    string FaresState,
    ImmutableArray<RailFare> Fares,
    ImmutableArray<string> Reasons
)
{
    public string SeatAvailability => "unknown";
    public string OperationalUpdates => "not-provided";
    public bool Damaged =>
        ElectronicTicketReason == "invalid-indicator"
        || FaresState is "limited" or "unavailable"
        || Fares.Any(x => x.Price.Reason == "incomplete-price");
}

public sealed record ScheduledJourney(
    string Key,
    string ThreadReference,
    string Transport,
    StationRef From,
    StationRef To,
    DateTimeOffset Departure,
    DateTimeOffset Arrival,
    decimal DurationSeconds,
    string? SourceReportedDate,
    string? Number,
    string? Title,
    string? Carrier,
    string? DeparturePlatform,
    string? ArrivalPlatform,
    string? Stops,
    TicketInformation Tickets,
    DateTimeOffset ReceivedAt,
    ImmutableArray<string> Reasons
)
{
    public string Source => "yandex-rasp";
    public string? DepartureTimeZone => null;
    public string? ArrivalTimeZone => null;
    public DateTimeOffset? SourceUpdatedAt => null;
}

public static class RailRules
{
    public static bool LabelValid(string value) =>
        Encoding.UTF8.GetByteCount(value) <= 1024
        && !value.Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t'));

    public static ErrorOr<RailRequest> Validate(RailRequest request, DateTimeOffset now)
    {
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        return
            request.From != request.To
            && request.Transport is "train" or "suburban" or "all"
            && Guid.TryParseExact(request.CatalogueGeneration, "N", out _)
            && DateOnly.TryParseExact(
                request.DepartureDate,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var date
            )
            && date >= today.AddDays(-1)
            && date <= today.AddDays(30)
            ? request
            : Error.Validation("Rail.Request.Invalid");
    }

    public static ErrorOr<ScheduledJourney> Journey(ScheduledJourney journey, RailRequest request)
    {
        return
            journey.From == request.From
            && journey.To == request.To
            && journey.From.Source == journey.To.Source
            && journey.Transport is "train" or "suburban"
            && (request.Transport == "all" || request.Transport == journey.Transport)
            && !string.IsNullOrWhiteSpace(journey.ThreadReference)
            && journey.ThreadReference.Length <= 100
            && !journey.ThreadReference.Any(char.IsControl)
            && journey.Arrival > journey.Departure
            && journey.DurationSeconds > 0
            && (decimal)(journey.Arrival - journey.Departure).TotalSeconds
                == journey.DurationSeconds
            && journey.Departure.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                == request.DepartureDate
            ? journey
            : Error.Validation("Rail.Journey.Invalid");
    }

    public static ErrorOr<string> Money(decimal whole, decimal cents, string currency)
    {
        if (currency is not ("RUB" or "USD" or "EUR"))
            return Error.Validation("Rail.Price.UnsupportedCurrency");
        if (
            whole < 0
            || whole != decimal.Truncate(whole)
            || cents < 0
            || cents > 99
            || cents != decimal.Truncate(cents)
        )
            return Error.Validation("Rail.Price.Invalid");
        try
        {
            var amount = checked(whole * 100 + cents);
            return (amount / 100).ToString("F2", CultureInfo.InvariantCulture);
        }
        catch (OverflowException)
        {
            return Error.Validation("Rail.Price.Invalid");
        }
    }
}
