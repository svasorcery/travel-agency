using ErrorOr;
using Travel.Modules.Trips.Core.Itineraries;

namespace Travel.Modules.Trips.Core.Options;

public static class OptionPlanner
{
    internal static Error Unavailable =>
        Error.Failure("Trips.OptionUnavailable", "Trip option data is unavailable.");

    // An out-of-scope brief describes the configured demo; it requires no live evidence.
    public static TripOptionPreview? Unsupported(
        TripBrief brief,
        DateTimeOffset asOfUtc,
        string catalogueVersion
    )
    {
        var today = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(asOfUtc, OptionSchedule.Zone).DateTime
        );
        var nights = brief.EndDate.DayNumber - brief.StartDate.DayNumber;
        List<string> unsupported = [];
        if (brief.OriginId != "moscow" || brief.DestinationId != "saint-petersburg")
            unsupported.Add("directionUnsupported");
        if (nights is not (1 or 2))
            unsupported.Add("dateRangeUnsupported");
        if (brief.Adults is not (1 or 2))
            unsupported.Add("groupUnsupported");
        if (brief.StartDate < today || brief.EndDate.DayNumber > today.DayNumber + 30)
            unsupported.Add("horizonUnsupported");
        return unsupported.Count == 0
            ? null
            : TripOptionPreview.ForDeclaredScope(catalogueVersion, asOfUtc, unsupported);
    }

    public static ErrorOr<TripOptionPreview> Preview(
        TripBrief brief,
        ValidatedItineraryFacts facts,
        DateTimeOffset asOfUtc,
        OptionCatalogue catalogue,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (brief.OriginId is null)
            return TripErrors.InvalidInput;
        if (Unsupported(brief, asOfUtc, catalogue.Version) is { } outside)
            return outside;
        if (
            !Available(facts, asOfUtc)
            || catalogue.Flights.Count > 4
            || catalogue.Hotels.Count > 4
            || catalogue.Flights.DistinctBy(f => f.Id).Count() != catalogue.Flights.Count
            || catalogue.Hotels.DistinctBy(h => h.Id).Count() != catalogue.Hotels.Count
        )
            return Unavailable;
        var flights = catalogue.Flights.Where(f => OptionSchedule.Matches(f, brief)).ToArray();
        var hotels = catalogue.Hotels.Where(h => OptionSchedule.Matches(h, brief)).ToArray();
        var reasons = new List<string>();
        if (flights.Length == 0)
            reasons.Add("flightUnavailable");
        if (hotels.Length == 0)
            reasons.Add("hotelUnavailable");
        if (reasons.Count > 0)
            return Response("noMatch", reasons);

        var walks = new OptionWalks(brief, facts, asOfUtc);
        var poiById = facts.Pois.ToDictionary(poi => poi.Id);
        var candidates = new List<Candidate>();
        bool fitsTime = false,
            exceedsBudget = false,
            missingWalks = false;
        foreach (var flight in flights)
        foreach (var hotel in hotels)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var stay = OptionSchedule.Stay(flight, hotel, brief);
            if (stay is null)
                continue;
            fitsTime = true;
            var price = OptionPricing.Calculate(brief, flight, hotel);
            if (brief.TotalBudget is { } budget && price.Subtotal > budget.MinorUnits)
            {
                exceedsBudget = true;
                continue;
            }
            var days = walks.Generate(OptionSchedule.Windows(stay), cancellationToken);
            var stops = days.SelectMany(d => d.Stops).ToArray();
            if (stops.Length == 0)
            {
                missingWalks = true;
                continue;
            }
            var covered = brief
                .Interests.Intersect(stops.SelectMany(s => poiById[s.Id].Tags))
                .Order(StringComparer.Ordinal)
                .ToArray();
            var option = new TripOption(
                new(flight.Id, flight.Adults, flight.Outbound, flight.Inbound!),
                stay,
                days,
                price.Price,
                covered,
                brief.Interests.Except(covered).Order(StringComparer.Ordinal).ToArray(),
                "",
                OptionSchedule.ArrivalTransfer,
                OptionSchedule.DepartureTransfer,
                OptionSchedule.ToFirstStop,
                OptionSchedule.BackToHotel
            );
            candidates.Add(
                new(
                    flight,
                    hotel,
                    option,
                    stops.Length,
                    price.Price.FlightAndStayComplete ? price.Subtotal : 0
                )
            );
        }
        if (candidates.Count == 0)
        {
            if (!fitsTime)
                reasons.Add("timeIncompatible");
            if (exceedsBudget)
                reasons.Add("budgetExceeded");
            if (missingWalks)
                reasons.Add("noWalks");
            return Response("noMatch", reasons);
        }
        var ranked = candidates
            .OrderByDescending(c => c.Option.CoveredInterests.Count)
            .ThenByDescending(c => c.StopCount)
            .ThenByDescending(c => c.Option.Price.FlightAndStayComplete)
            .ThenBy(c => c.ComparableTotal)
            .ThenBy(c => c.Flight.Id, StringComparer.Ordinal)
            .ThenBy(c => c.Hotel.Id, StringComparer.Ordinal)
            .ToArray();
        var winner = ranked[0];
        var chosen = winner.Option with
        {
            SelectionReason = Explain(winner, ranked.ElementAtOrDefault(1)),
        };
        if (
            !OptionValidator.IsValid(
                brief,
                facts,
                asOfUtc,
                winner.Flight,
                winner.Hotel,
                chosen,
                cancellationToken
            )
        )
            return Unavailable;
        return Response("option", [], chosen);

        TripOptionPreview Response(
            string outcome,
            IReadOnlyList<string> details,
            TripOption? option = null
        ) =>
            new(
                outcome,
                details,
                catalogue.Version,
                TripOptionPreview.Policy,
                asOfUtc,
                facts.DatasetId,
                facts.Version,
                facts.DatasetHash,
                TripOptionPreview.Credits,
                SaintPetersburgOutdoorV1.DataUrl,
                option
            );
    }

    internal static bool Available(ValidatedItineraryFacts facts, DateTimeOffset asOf) =>
        asOf.Offset == TimeSpan.Zero
        && asOf >= facts.BuildAsOfUtc
        && asOf < facts.ReleaseValidUntil
        && asOf < facts.ModelSource.ValidUntil
        && facts.Destination.DestinationId == "saint-petersburg"
        && facts.Destination.TimeZone == "Europe/Moscow";

    private static string Explain(Candidate best, Candidate? other) =>
        other is null ? "onlyCompatibleOption"
        : best.Option.CoveredInterests.Count != other.Option.CoveredInterests.Count
            ? "moreInterests"
        : best.StopCount != other.StopCount ? "moreStops"
        : best.Option.Price.FlightAndStayComplete != other.Option.Price.FlightAndStayComplete
            ? "completeFlightAndStay"
        : best.ComparableTotal != other.ComparableTotal ? "lowerComparablePrice"
        : "equalByStableIds";

    private sealed record Candidate(
        FlightOffer Flight,
        StayOffer Hotel,
        TripOption Option,
        int StopCount,
        long ComparableTotal
    );
}
