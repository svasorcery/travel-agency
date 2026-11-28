using Travel.Modules.Trips.Core.Itineraries;

namespace Travel.Modules.Trips.Core.Options;

public sealed record TripOptionPreview(
    string Outcome,
    IReadOnlyList<string> Reasons,
    string CatalogueVersion,
    string PolicyVersion,
    DateTimeOffset AsOfUtc,
    string DatasetId,
    string DatasetVersion,
    string DatasetHash,
    string Attribution,
    string DataUrl,
    TripOption? Option
)
{
    public const string Policy = "spb-option-v1";
    internal const string Credits = "© OpenStreetMap contributors · ODbL 1.0 · Wikidata (CC0)";

    internal static TripOptionPreview ForDeclaredScope(
        string version,
        DateTimeOffset asOfUtc,
        IReadOnlyList<string> reasons
    ) =>
        new(
            "unsupported",
            reasons,
            version,
            Policy,
            asOfUtc,
            SaintPetersburgOutdoorV1.DatasetId,
            SaintPetersburgOutdoorV1.Version,
            SaintPetersburgOutdoorV1.PayloadHash,
            Credits,
            SaintPetersburgOutdoorV1.DataUrl,
            null
        );
}

public sealed record TripOption(
    OptionFlight Flight,
    OptionHotel Hotel,
    IReadOnlyList<OptionDay> Days,
    OptionPrice Price,
    IReadOnlyList<string> CoveredInterests,
    IReadOnlyList<string> UnmetInterests,
    string SelectionReason,
    int ArrivalTransferMinutes,
    int DepartureTransferMinutes,
    int HotelToWalkMinutes,
    int WalkToHotelMinutes
);

public sealed record OptionFlight(string Id, int Adults, OptionLeg Outbound, OptionLeg Inbound);

public sealed record OptionLeg(
    string FromAirport,
    string ToAirport,
    DateTimeOffset DepartsAt,
    DateTimeOffset ArrivesAt
);

public sealed record OptionHotel(
    string Id,
    string Name,
    int Adults,
    int Nights,
    DateTimeOffset CheckInAt,
    DateTimeOffset CheckOutAt
);

public sealed record OptionDay(
    DateOnly Date,
    string? WindowStart,
    string? WindowEnd,
    IReadOnlyList<OptionStop> Stops,
    int ProgrammeMinutes,
    string? EmptyReason
);

public sealed record OptionStop(
    string Id,
    string Title,
    string Start,
    string End,
    int DwellMinutes,
    string? IncomingEdgeId,
    int WalkFromPreviousMinutes,
    bool AccessHoursKnown
);

public sealed record OptionPrice(
    string Currency,
    string KnownSubtotal,
    bool FlightAndStayComplete,
    string BudgetStatus,
    IReadOnlyList<OptionPriceLine> Components,
    IReadOnlyList<string> UnpricedExpenses
);

public sealed record OptionPriceLine(
    string Component,
    string Kind,
    string? Amount,
    string? Currency
);
