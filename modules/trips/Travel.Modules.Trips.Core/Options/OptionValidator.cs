using System.Globalization;
using Travel.Modules.Trips.Core.Itineraries;

namespace Travel.Modules.Trips.Core.Options;

/// <summary>Replay the published visits and transfers against original offers and dated facts.</summary>
public static class OptionValidator
{
    public static bool IsValid(
        TripBrief brief,
        ValidatedItineraryFacts facts,
        DateTimeOffset asOfUtc,
        FlightOffer flight,
        StayOffer room,
        TripOption option,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (
            !OptionPlanner.Available(facts, asOfUtc)
            || !OptionSchedule.Matches(flight, brief)
            || !OptionSchedule.Matches(room, brief)
            || flight.Inbound is not { } inbound
            || option.Flight.Id != flight.Id
            || option.Flight.Adults != flight.Adults
            || !SameLeg(option.Flight.Outbound, flight.Outbound)
            || !SameLeg(option.Flight.Inbound, inbound)
            || option.Hotel.Id != room.Id
            || option.Hotel.Name != room.Name
            || option.Hotel.Adults != room.Adults
            || option.Hotel.Nights != room.Nights.Count
            || option.Days.Count != room.Nights.Count + 1
            || option.ArrivalTransferMinutes != OptionSchedule.ArrivalTransfer
            || option.DepartureTransferMinutes != OptionSchedule.DepartureTransfer
            || option.HotelToWalkMinutes != OptionSchedule.ToFirstStop
            || option.WalkToHotelMinutes != OptionSchedule.BackToHotel
        )
            return false;
        // Derive both hotel boundaries from the source, independent of the planned stay and windows.
        var hotel = option.Hotel;
        var arrival = flight.Outbound.ArrivesAt.AddMinutes(OptionSchedule.ArrivalTransfer);
        var departure = inbound.DepartsAt.AddMinutes(-OptionSchedule.DepartureTransfer);
        var opening = OptionSchedule.At(brief.StartDate, room.EarliestCheckIn);
        var closing = OptionSchedule.At(brief.EndDate, room.LatestCheckOut);
        if (
            !hotel.CheckInAt.EqualsExact(arrival >= opening ? arrival : opening)
            || !hotel.CheckOutAt.EqualsExact(departure <= closing ? departure : closing)
            || hotel.CheckInAt > OptionSchedule.At(brief.StartDate, room.LatestCheckIn)
            || hotel.CheckOutAt < OptionSchedule.At(brief.EndDate, room.EarliestCheckOut)
            || hotel.CheckInAt >= hotel.CheckOutAt
        )
            return false;

        var pois = facts.Pois.ToDictionary(p => p.Id);
        var edges = facts.Edges.ToDictionary(e => (e.FromPoiId, e.ToPoiId));
        var evidence = facts.Evidence.ToDictionary(e => e.Id);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var limits = OptionSchedule.Pace(brief.Pace);
        for (var index = 0; index < option.Days.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var day = option.Days[index];
            var date = brief.StartDate.AddDays(index);
            if (day.Date != date || day.Stops.Count > limits.Stops)
                return false;
            var leaveHotel = index == 0 ? Math.Max(600, Minute(hotel.CheckInAt)) : 600;
            var returnToHotel =
                index == option.Days.Count - 1 ? Math.Min(1080, Minute(hotel.CheckOutAt)) : 1080;
            var earliest = leaveHotel + OptionSchedule.ToFirstStop;
            var latest = returnToHotel - OptionSchedule.BackToHotel;
            var emptyWindow = earliest >= latest;
            if (
                day.WindowStart != (emptyWindow ? null : StopWindow.Clock(earliest))
                || day.WindowEnd != (emptyWindow ? null : StopWindow.Clock(latest))
            )
                return false;
            if (day.Stops.Count == 0)
            {
                if (
                    day.ProgrammeMinutes != 0
                    || day.EmptyReason != (emptyWindow ? "noWalkingWindow" : "noEligibleStops")
                )
                    return false;
                continue;
            }
            if (emptyWindow || day.EmptyReason is not null)
                return false;
            int cursor = earliest,
                walking = 0;
            string? previous = null;
            foreach (var stop in day.Stops)
            {
                if (
                    !visited.Add(stop.Id)
                    || !pois.TryGetValue(stop.Id, out var poi)
                    || stop.Title != poi.Title
                    || stop.DwellMinutes != poi.DwellMinutes
                    || !Known(poi.IdentityEvidenceId)
                )
                    return false;
                if (previous is null)
                {
                    if (stop.IncomingEdgeId is not null || stop.WalkFromPreviousMinutes != 0)
                        return false;
                }
                else
                {
                    if (
                        !edges.TryGetValue((previous, stop.Id), out var edge)
                        || stop.IncomingEdgeId != edge.Id
                        || stop.WalkFromPreviousMinutes != edge.MaximumMinutes
                        || !Known(edge.EvidenceId)
                    )
                        return false;
                    walking += edge.MaximumMinutes;
                    cursor += edge.MaximumMinutes;
                }
                if (
                    !TryMinute(stop.Start, out var start)
                    || !TryMinute(stop.End, out var end)
                    || start < cursor
                    || end - start != poi.DwellMinutes
                    || end + OptionSchedule.BackToHotel > returnToHotel
                )
                    return false;
                var intervals =
                    poi.Access.EvidenceId is { } access && evidence[access].IsKnownAt(asOfUtc)
                        ? poi.Access.GetIntervalsOn(date, evidence[access], facts.Destination)
                        : null;
                if (
                    stop.AccessHoursKnown != (intervals is not null)
                    || intervals is not null
                        && !intervals.Any(i => start >= i.StartMinute && end <= i.EndMinute)
                )
                    return false;
                previous = stop.Id;
                cursor = end;
            }
            if (
                walking > limits.Walk
                || day.ProgrammeMinutes != cursor - earliest
                || day.ProgrammeMinutes > limits.Duration
            )
                return false;
            bool Known(string id) =>
                evidence[id].IsKnownAt(asOfUtc) && evidence[id].IsKnownOn(date, facts.Destination);
        }
        var covered = brief
            .Interests.Intersect(visited.SelectMany(id => pois[id].Tags))
            .Order(StringComparer.Ordinal)
            .ToArray();
        var cost = OptionPricing.Calculate(brief, flight, room);
        return visited.Count > 0
            && option.CoveredInterests.SequenceEqual(covered)
            && option.UnmetInterests.SequenceEqual(
                brief.Interests.Except(covered).Order(StringComparer.Ordinal)
            )
            && (brief.TotalBudget is null || cost.Subtotal <= brief.TotalBudget.MinorUnits)
            && option.Price.Currency == cost.Price.Currency
            && option.Price.KnownSubtotal == cost.Price.KnownSubtotal
            && option.Price.BudgetStatus == cost.Price.BudgetStatus
            && option.Price.FlightAndStayComplete == cost.Price.FlightAndStayComplete
            && option.Price.Components.SequenceEqual(cost.Price.Components)
            && option.Price.UnpricedExpenses.SequenceEqual(cost.Price.UnpricedExpenses);
    }

    private static int Minute(DateTimeOffset time) => time.Hour * 60 + time.Minute;

    private static bool SameLeg(OptionLeg displayed, OptionLeg source) =>
        displayed.FromAirport == source.FromAirport
        && displayed.ToAirport == source.ToAirport
        && displayed.DepartsAt.EqualsExact(source.DepartsAt)
        && displayed.ArrivesAt.EqualsExact(source.ArrivesAt);

    private static bool TryMinute(string text, out int minute)
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
}
