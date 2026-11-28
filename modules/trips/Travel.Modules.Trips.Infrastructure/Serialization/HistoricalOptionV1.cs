using System.Buffers;
using System.Globalization;
using System.Text;
using Travel.Modules.Trips.Core;
using Travel.Modules.Trips.Core.Options;

namespace Travel.Modules.Trips.Infrastructure.Serialization;

// Validate the v1 historical representation without consulting the active catalogue or package.
internal static class HistoricalOptionV1
{
    internal static bool Matches(TripBriefInput brief, TripOptionPreview preview)
    {
        if (
            brief is null
            || TripBrief.Create(brief).IsError
            || preview is null
            || preview.Outcome != "option"
            || preview.Option
                is not { Flight: { } flight, Hotel: { } stay, Price: { } price } option
            || preview.Reasons is not { Count: 0 }
            || preview.AsOfUtc.Offset != TimeSpan.Zero
            || preview.DatasetVersion is not { Length: >= 1 and <= 32 }
            || preview.DatasetVersion[0] == '0'
            || !preview.DatasetVersion.All(char.IsAsciiDigit)
            || !SafeId(preview.PolicyVersion)
            || preview.DatasetHash is not { Length: 64 }
            || !preview.DatasetHash.All(c => char.IsAsciiDigit(c) || c is >= 'a' and <= 'f')
            || preview.CatalogueVersion is not ("A" or "B")
            || !SafeText(preview.Attribution, 1024)
            || preview.DatasetId != "saint-petersburg-outdoor"
            || preview.DataUrl
                != $"/trips-data/{preview.DatasetId}/{preview.DatasetVersion}/saint-petersburg-v{preview.DatasetVersion}.manifest.json"
            || brief.OriginId != "moscow"
            || brief.DestinationId != "saint-petersburg"
            || brief.Adults is < 1 or > 2
            || flight.Adults != brief.Adults
            || stay.Adults != brief.Adults
            || !SafeId(flight.Id, 64)
            || !SafeId(stay.Id, 64)
            || !SafeText(stay.Name, 200)
            || flight.Outbound is null
            || flight.Inbound is null
            || flight.Outbound.ArrivesAt <= flight.Outbound.DepartsAt
            || flight.Inbound.ArrivesAt <= flight.Inbound.DepartsAt
            || stay.CheckInAt < flight.Outbound.ArrivesAt
            || stay.CheckOutAt > flight.Inbound.DepartsAt
            || stay.CheckOutAt <= stay.CheckInAt
            || option.Days is not { Count: >= 2 and <= 3 }
            || stay.Nights != option.Days.Count - 1
            || stay.Nights != brief.EndDate.DayNumber - brief.StartDate.DayNumber
            || option.CoveredInterests is null
            || option.UnmetInterests is null
            || !Interests(brief, option)
            || !TimesMatchBrief(brief, flight, stay)
            || price.Currency != brief.Currency
            || price.Components is not { Count: > 0 and <= 32 }
            || price.UnpricedExpenses is not { Count: <= 8 }
            || price.KnownSubtotal is null
        )
            return false;
        var visited = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < option.Days.Count; i++)
        {
            var day = option.Days[i];
            if (
                day is null
                || day.Date != brief.StartDate.AddDays(i)
                || day.Stops is not { Count: <= 4 }
                || !ValidDay(day, visited)
            )
                return false;
        }
        return visited.Count > 0 && ValidPrices(brief, price);
    }

    private static bool SafeId(string? value, int limit = 100) =>
        value is { Length: > 0 }
        && value.Length <= limit
        && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    private static bool SafeText(string? value, int scalarLimit)
    {
        if (string.IsNullOrEmpty(value) || value.Length > scalarLimit * 2)
            return false;
        var remaining = value.AsSpan();
        var count = 0;
        var hasText = false;
        while (!remaining.IsEmpty)
        {
            if (
                Rune.DecodeFromUtf16(remaining, out var rune, out var consumed)
                    != OperationStatus.Done
                || Rune.IsControl(rune)
                || ++count > scalarLimit
            )
                return false;
            hasText |= !Rune.IsWhiteSpace(rune) && rune.Value != '\uFEFF';
            remaining = remaining[consumed..];
        }
        return hasText;
    }

    private static bool TimesMatchBrief(
        TripBriefInput brief,
        OptionFlight flight,
        OptionHotel hotel
    )
    {
        var outward = flight.Outbound;
        var back = flight.Inbound;
        return outward.FromAirport == "SVO"
            && outward.ToAirport == "LED"
            && back.FromAirport == "LED"
            && back.ToAirport == "SVO"
            && DateOnly.FromDateTime(outward.DepartsAt.DateTime) == brief.StartDate
            && DateOnly.FromDateTime(outward.ArrivesAt.DateTime) == brief.StartDate
            && DateOnly.FromDateTime(back.DepartsAt.DateTime) == brief.EndDate
            && DateOnly.FromDateTime(back.ArrivesAt.DateTime) == brief.EndDate
            && DateOnly.FromDateTime(hotel.CheckInAt.DateTime) == brief.StartDate
            && DateOnly.FromDateTime(hotel.CheckOutAt.DateTime) == brief.EndDate;
    }

    private static bool Interests(TripBriefInput brief, TripOption option)
    {
        var displayed = option.CoveredInterests.Concat(option.UnmetInterests).ToArray();
        return displayed.Length == brief.Interests.Count
            && new HashSet<string>(displayed, StringComparer.Ordinal).SetEquals(brief.Interests)
            && option.SelectionReason
                is (
                    "onlyCompatibleOption"
                    or "moreInterests"
                    or "moreStops"
                    or "completeFlightAndStay"
                    or "lowerComparablePrice"
                    or "equalByStableIds"
                )
            && option.ArrivalTransferMinutes is >= 0 and <= 1440
            && option.DepartureTransferMinutes is >= 0 and <= 1440
            && option.HotelToWalkMinutes is >= 0 and <= 1440
            && option.WalkToHotelMinutes is >= 0 and <= 1440;
    }

    private static bool ValidDay(OptionDay day, HashSet<string> visited)
    {
        if (
            day.ProgrammeMinutes is < 0 or > 1440
            || (day.WindowStart is null) != (day.WindowEnd is null)
        )
            return false;
        if (day.WindowStart is null)
            return day.Stops.Count == 0
                && day.ProgrammeMinutes == 0
                && day.EmptyReason == "noWalkingWindow";
        if (
            !Minute(day.WindowStart, out var start)
            || !Minute(day.WindowEnd, out var end)
            || start >= end
        )
            return false;
        if (day.Stops.Count == 0)
            return day.ProgrammeMinutes == 0 && day.EmptyReason == "noEligibleStops";
        int cursor = start;
        for (int i = 0; i < day.Stops.Count; i++)
        {
            var stop = day.Stops[i];
            if (
                stop is null
                || !SafeId(stop.Id)
                || !visited.Add(stop.Id)
                || !SafeText(stop.Title, 200)
                || !Minute(stop.Start, out var from)
                || !Minute(stop.End, out var to)
                || stop.DwellMinutes <= 0
                || stop.WalkFromPreviousMinutes is < 0 or > 1440
                || (
                    i == 0
                        ? stop.IncomingEdgeId != null || stop.WalkFromPreviousMinutes != 0
                        : !SafeId(stop.IncomingEdgeId, 200)
                )
                || from < cursor + stop.WalkFromPreviousMinutes
                || to - from != stop.DwellMinutes
                || to > end
            )
                return false;
            cursor = to;
        }
        return day.EmptyReason is null && day.ProgrammeMinutes == cursor - start;
    }

    private static bool Minute(string? text, out int minute)
    {
        var valid = TimeOnly.TryParseExact(
            text,
            "HH:mm",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var time
        );
        minute = time.Hour * 60 + time.Minute;
        return valid;
    }

    private static bool ValidPrices(TripBriefInput brief, OptionPrice price)
    {
        long known = 0;
        var comparableBases = new HashSet<string>(StringComparer.Ordinal);
        bool incomplete = false;
        foreach (var line in price.Components)
        {
            if (
                line is null
                || line.Component
                    is not ("flightBase" or "hotelBase" or "includedTax" or "localFee")
                || (line.Amount is null) != (line.Currency is null)
                || (line.Kind == "unknownMandatory" && line.Amount is not null)
            )
                return false;
            long minor = 0;
            if (line.Amount is not null)
            {
                var amount = PlanningMoney.Create(line.Amount, line.Currency);
                if (amount.IsError)
                    return false;
                minor = amount.Value.MinorUnits;
            }
            switch (line.Kind)
            {
                case "travelerMandatory"
                    when line.Amount is not null && line.Currency == price.Currency:
                    known += minor;
                    comparableBases.Add(line.Component);
                    break;
                case "travelerMandatory":
                case "unknownMandatory":
                    incomplete = true;
                    break;
                case "included":
                case "supplierNet":
                    break;
                default:
                    return false;
            }
        }
        var complete =
            !incomplete
            && comparableBases.Contains("flightBase")
            && comparableBases.Contains("hotelBase");
        return price.KnownSubtotal == PlanningMoney.FormatMinorUnits(known)
            && price.FlightAndStayComplete == complete
            && price.BudgetStatus == (brief.TotalBudget is null ? "notSpecified" : "indeterminate")
            && price.UnpricedExpenses.All(x => x is "meals" or "transfers")
            && price.UnpricedExpenses.Distinct(StringComparer.Ordinal).Count()
                == price.UnpricedExpenses.Count;
    }
}
