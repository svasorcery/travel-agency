using System.Globalization;
using ErrorOr;

namespace Travel.Modules.Trips.Core.Itineraries;

/// <summary>
/// Bounded editorial selection of simple daily walking paths. The beam is a heuristic;
/// neither filled targets nor modelled minutes establish feasibility or a global optimum.
/// </summary>
public static class ItineraryPolicy
{
    public const string PolicyVersion = "bounded-beam-v1";
    public const int BeamWidth = 16;
    public const int MaximumExpansions = 20_000;

    public static ErrorOr<ItineraryOutcome> Generate(
        TripBrief brief,
        DestinationSelection savedDestination,
        ValidatedItineraryFacts facts,
        DateTimeOffset asOfUtc,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (
            brief is null
            || savedDestination is null
            || facts is null
            || !ItineraryValidation.Utc(asOfUtc)
            || brief.DestinationId != savedDestination.DestinationId
            || savedDestination.DestinationId != facts.Destination.DestinationId
            || savedDestination.TimeZone != facts.Destination.TimeZone
            || savedDestination.CatalogueVersion != facts.Destination.CatalogueVersion
            || !ItineraryValidation.TryZone(savedDestination.TimeZone, out var zone)
        )
            return TripErrors.InvalidInput;

        if (
            asOfUtc < facts.BuildAsOfUtc
            || asOfUtc >= facts.ReleaseValidUntil
            || asOfUtc >= facts.ModelSource.ValidUntil
        )
            return Error.Failure(
                "Trips.ItineraryUnavailable",
                "Itinerary generation is unavailable."
            );
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(asOfUtc, zone!).DateTime);
        var dayCount = brief.EndDate.DayNumber - brief.StartDate.DayNumber + 1;
        var unsupported =
            dayCount is < 1 or > 3 ? "dateRangeUnsupported"
            : brief.StartDate < today ? "historicalDatesUnsupported"
            : brief.EndDate.DayNumber > today.DayNumber + 30 ? "horizonUnsupported"
            : null;
        if (unsupported is not null)
            return Outcome(ItineraryOutcomeKind.Unsupported, [], [], [unsupported], false, 0);

        var (target, walkLimit, durationLimit) = brief.Pace switch
        {
            "relaxed" => (2, 60, 240),
            "balanced" => (3, 90, 360),
            "active" => (4, 120, 480),
            _ => (0, 0, 0),
        };
        if (target == 0 || brief.Adults is < 1 or > 9)
            return TripErrors.InvalidInput;
        var evidence = facts.Evidence.ToDictionary(e => e.Id, StringComparer.Ordinal);
        var edges = facts.Edges.ToDictionary(e => (e.FromPoiId, e.ToPoiId));
        var orderedPois = facts.Pois.OrderBy(p => p.Id, StringComparer.Ordinal).ToArray();
        var requested = brief
            .Interests.Where(ItineraryValidation.Tag)
            .ToHashSet(StringComparer.Ordinal);
        var selectedIds = new HashSet<string>(StringComparer.Ordinal);
        var covered = new HashSet<string>(StringComparer.Ordinal);
        var days = new List<ItineraryCandidateDay>();
        var expansions = 0;
        var searchLimited = false;
        for (var dayIndex = 0; dayIndex < dayCount; dayIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var date = brief.StartDate.AddDays(dayIndex);
            var eligible = new Dictionary<string, ItineraryPoi>(StringComparer.Ordinal);
            foreach (var poi in orderedPois)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (selectedIds.Contains(poi.Id) || !Known(evidence[poi.IdentityEvidenceId], date))
                    continue;
                if (Intervals(poi, date) is { Count: 0 })
                    continue;
                eligible.Add(poi.Id, poi);
            }

            var best = SearchPath.Empty;
            var beam = new List<SearchPath>();
            // Stable ID traversal precedes ranking/pruning, including rejected attempts.
            foreach (var poi in orderedPois)
            {
                if (!Attempt())
                    break;
                if (!eligible.ContainsKey(poi.Id) || poi.DwellMinutes > durationLimit)
                    continue;
                var path = Path([poi], [], 0, poi.DwellMinutes);
                beam.Add(path);
                if (Compare(path, best) < 0)
                    best = path;
            }
            beam.Sort(Compare);
            beam = beam.Take(BeamWidth).ToList();
            for (var depth = 1; depth < target && beam.Count > 0 && !searchLimited; depth++)
            {
                var next = new List<SearchPath>();
                foreach (var path in beam)
                {
                    foreach (var poi in orderedPois)
                    {
                        if (!Attempt())
                            break;
                        if (
                            !eligible.ContainsKey(poi.Id)
                            || path.Pois.Any(p => p.Id == poi.Id)
                            || !edges.TryGetValue((path.Pois[^1].Id, poi.Id), out var edge)
                            || !Known(evidence[edge.EvidenceId], date)
                            || path.WalkingUpper + edge.MaximumMinutes > walkLimit
                            || path.Dwell
                                + poi.DwellMinutes
                                + path.WalkingUpper
                                + edge.MaximumMinutes
                                > durationLimit
                        )
                            continue;
                        var expanded = Path(
                            [.. path.Pois, poi],
                            [.. path.Edges, edge],
                            path.WalkingUpper + edge.MaximumMinutes,
                            path.Dwell + poi.DwellMinutes
                        );
                        next.Add(expanded);
                        // Preserve the best found path even when it would be pruned or work ends.
                        if (Compare(expanded, best) < 0)
                            best = expanded;
                    }
                    if (searchLimited)
                        break;
                }
                next.Sort(Compare);
                beam = next.Take(BeamWidth).ToList();
            }

            var blocks = best
                .Pois.Select(poi =>
                {
                    selectedIds.Add(poi.Id);
                    covered.UnionWith(poi.Tags.Where(requested.Contains));
                    var intervals = Intervals(poi, date);
                    var access = intervals is null
                        ? new ItineraryCandidateFact(
                            ItineraryFactKind.Access,
                            ItineraryFactState.Unknown,
                            null,
                            poi.Access.IsKnown
                                ? "openingHoursExpiredOrNotApplicable"
                                : "openingHoursUnknown"
                        )
                        : new ItineraryCandidateFact(
                            ItineraryFactKind.Access,
                            ItineraryFactState.Known,
                            poi.Access.EvidenceId,
                            null
                        );
                    var note = string.Create(
                        CultureInfo.InvariantCulture,
                        $"Historical proposal: {facts.DatasetId}/{facts.Version}; asOf={asOfUtc:O}; POI={poi.Id}. © OpenStreetMap contributors; ODbL: https://opendatacommons.org/licenses/odbl/1-0/; https://www.openstreetmap.org/copyright."
                    );
                    return new ItineraryCandidateBlock(
                        poi.Id,
                        Guid.NewGuid(),
                        poi.Title,
                        date,
                        null,
                        null,
                        null,
                        note,
                        [
                            new(
                                ItineraryFactKind.IdentityLocation,
                                ItineraryFactState.Known,
                                poi.IdentityEvidenceId,
                                null
                            ),
                            access,
                        ],
                        intervals ?? []
                    );
                })
                .ToArray();
            var usedEdges = best
                .Edges.Select(edge =>
                {
                    var fact = evidence[edge.EvidenceId];
                    return new ItineraryCandidateEdge(
                        edge.Id,
                        edge.FromPoiId,
                        edge.ToPoiId,
                        edge.MinimumMinutes,
                        edge.MaximumMinutes,
                        edge.EvidenceId,
                        edge.Restrictions,
                        fact.Method,
                        fact.RightsBasis,
                        fact.ModelledWalk!
                    );
                })
                .ToArray();
            days.Add(new(date, blocks, usedEdges));
        }

        var reasons = new List<string>();
        if (days.Any(d => d.Blocks.Count < target))
            reasons.Add("insufficientEligibleStops");
        if (brief.Interests.Any(i => !covered.Contains(i)))
            reasons.Add("unmetInterests");
        if (searchLimited)
            reasons.Add("searchLimited");
        if (selectedIds.Count == 0)
            reasons.Add("noEligibleStops");
        cancellationToken.ThrowIfCancellationRequested();
        return Outcome(
            selectedIds.Count > 0 ? ItineraryOutcomeKind.Candidate : ItineraryOutcomeKind.Empty,
            days,
            covered,
            reasons,
            searchLimited,
            expansions
        );

        bool Known(ItineraryEvidence fact, DateOnly date) =>
            fact.IsKnownAt(asOfUtc) && fact.IsKnownOn(date, savedDestination);

        IReadOnlyList<ItineraryWeeklyInterval>? Intervals(ItineraryPoi poi, DateOnly date)
        {
            var fact = poi.Access.EvidenceId is { } id ? evidence[id] : null;
            return fact is not null && fact.IsKnownAt(asOfUtc)
                ? poi.Access.GetIntervalsOn(date, fact, savedDestination)
                : null;
        }

        bool Attempt()
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (expansions >= MaximumExpansions)
            {
                searchLimited = true;
                return false;
            }
            expansions++;
            return true;
        }

        SearchPath Path(
            ItineraryPoi[] pois,
            ItineraryWalkEdge[] pathEdges,
            int walking,
            int dwell
        ) =>
            new(
                pois,
                pathEdges,
                walking,
                dwell,
                requested.Count(interest =>
                    covered.Contains(interest) || pois.Any(p => p.Tags.Contains(interest))
                )
            );

        ItineraryOutcome Outcome(
            ItineraryOutcomeKind kind,
            IReadOnlyList<ItineraryCandidateDay> resultDays,
            IEnumerable<string> resultCovered,
            IReadOnlyList<string> resultReasons,
            bool limited,
            int count
        )
        {
            var coveredInterests = resultCovered.Order(StringComparer.Ordinal).ToArray();
            var unmet = brief
                .Interests.Except(coveredInterests, StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();
            var exclusions = new List<string>
            {
                "indoorReservationsNotPlanned",
                "interdayTransfersNotPlanned",
                "arrivalScheduleNotPlanned",
            };
            if (brief.OriginId is not null)
                exclusions.Add("transferNotPlanned");
            if (brief.TotalBudget is not null)
                exclusions.Add("totalCostUnknown");
            var uncertainties = new List<string>
            {
                "walkingModelled",
                "calibrationNotEvaluated",
                "humanQualityNotEvaluated",
            };
            if (
                resultDays
                    .SelectMany(d => d.Blocks)
                    .Any(b =>
                        b.Facts.Any(f =>
                            f.Kind == ItineraryFactKind.Access
                            && f.State == ItineraryFactState.Unknown
                        )
                    )
            )
                uncertainties.Add("openingHoursUnknown");
            var expiresAt =
                asOfUtc.AddMinutes(15) < facts.ReleaseValidUntil
                    ? asOfUtc.AddMinutes(15)
                    : facts.ReleaseValidUntil;
            return new(
                kind,
                savedDestination,
                facts.DatasetId,
                facts.Version,
                facts.DatasetHash,
                PolicyVersion,
                asOfUtc,
                expiresAt,
                facts.Preparation,
                facts.ModelSource,
                resultDays,
                kind == ItineraryOutcomeKind.Candidate && resultReasons.Count == 0
                    ? ItineraryCoverage.Filled
                    : ItineraryCoverage.Partial,
                coveredInterests,
                unmet,
                resultReasons
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal)
                    .ToArray(),
                exclusions.Order(StringComparer.Ordinal).ToArray(),
                uncertainties.Order(StringComparer.Ordinal).ToArray(),
                limited,
                count
            );
        }
    }

    private static int Compare(SearchPath left, SearchPath right)
    {
        var comparison = right.InterestCount.CompareTo(left.InterestCount);
        if (comparison != 0)
            return comparison;
        comparison = right.Pois.Length.CompareTo(left.Pois.Length);
        if (comparison != 0)
            return comparison;
        comparison = left.WalkingUpper.CompareTo(right.WalkingUpper);
        if (comparison != 0)
            return comparison;
        for (var index = 0; index < left.Pois.Length; index++)
        {
            comparison = string.CompareOrdinal(left.Pois[index].Id, right.Pois[index].Id);
            if (comparison != 0)
                return comparison;
        }
        return 0;
    }

    private sealed record SearchPath(
        ItineraryPoi[] Pois,
        ItineraryWalkEdge[] Edges,
        int WalkingUpper,
        int Dwell,
        int InterestCount
    )
    {
        internal static readonly SearchPath Empty = new([], [], 0, 0, 0);
    }
}
