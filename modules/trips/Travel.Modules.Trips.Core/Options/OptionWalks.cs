using Travel.Modules.Trips.Core.Itineraries;

namespace Travel.Modules.Trips.Core.Options;

internal sealed class OptionWalks(
    TripBrief brief,
    ValidatedItineraryFacts facts,
    DateTimeOffset asOf
)
{
    private readonly Dictionary<string, ItineraryEvidence> evidence = facts.Evidence.ToDictionary(
        e => e.Id
    );
    private readonly Dictionary<string, ItineraryPoi> pois = facts.Pois.ToDictionary(p => p.Id);
    private readonly ILookup<string, ItineraryWalkEdge> outgoing = facts
        .Edges.OrderBy(e => e.ToPoiId, StringComparer.Ordinal)
        .ToLookup(e => e.FromPoiId);
    private readonly Dictionary<WalkWindows, OptionDay[]> cache = [];

    public OptionDay[] Generate(WalkWindows windows, CancellationToken token)
    {
        if (cache.TryGetValue(windows, out var found))
            return found;
        var count = brief.EndDate.DayNumber - brief.StartDate.DayNumber + 1;
        var days = new OptionDay[count];
        var used = new HashSet<string>(StringComparer.Ordinal);
        var covered = new HashSet<string>(StringComparer.Ordinal);
        int[] order = count == 3 ? [1, 0, 2] : [0, 1];
        foreach (var index in order)
        {
            var date = brief.StartDate.AddDays(index);
            var window = windows.For(index, count);
            var day = SelectDay(date, window, used, covered, token);
            foreach (var stop in day.Stops)
            {
                used.Add(stop.Id);
                covered.UnionWith(pois[stop.Id].Tags);
            }
            days[index] = day;
        }
        cache.Add(windows, days);
        return days;
    }

    private OptionDay SelectDay(
        DateOnly date,
        StopWindow window,
        HashSet<string> used,
        HashSet<string> covered,
        CancellationToken token
    )
    {
        if (window.IsEmpty)
            return new(date, null, null, [], 0, "noWalkingWindow");
        var limits = OptionSchedule.Pace(brief.Pace);
        // Resolve evidence for this trip date once; all path expansions reuse these lookups.
        bool Known(string id) =>
            evidence[id].IsKnownAt(asOf) && evidence[id].IsKnownOn(date, facts.Destination);
        var eligible = pois
            .Values.Where(poi => !used.Contains(poi.Id) && Known(poi.IdentityEvidenceId))
            .ToDictionary(
                poi => poi.Id,
                poi =>
                    poi.Access.EvidenceId is { } id && evidence[id].IsKnownAt(asOf)
                        ? poi.Access.GetIntervalsOn(date, evidence[id], facts.Destination)
                        : null
            );
        var eligibleEdges = facts
            .Edges.Where(edge => Known(edge.EvidenceId))
            .Select(edge => edge.Id)
            .ToHashSet();
        var path = new List<OptionStop>(limits.Stops);
        OptionStop[] best = [];
        int bestCoverage = -1,
            bestWalk = 0,
            bestDuration = 0;
        foreach (var first in eligible.Keys.Order(StringComparer.Ordinal))
            Visit(first, null, window.Start, 0);
        return new(
            date,
            window.StartText,
            window.EndText,
            best,
            bestDuration,
            best.Length == 0 ? "noEligibleStops" : null
        );

        void Visit(string id, ItineraryWalkEdge? incoming, int earliest, int walking)
        {
            token.ThrowIfCancellationRequested();
            if (!eligible.TryGetValue(id, out var intervals) || path.Any(s => s.Id == id))
                return;
            var poi = pois[id];
            var start = earliest;
            if (intervals is not null)
            {
                var open = intervals.FirstOrDefault(i =>
                    Math.Max(i.StartMinute, earliest) + poi.DwellMinutes <= i.EndMinute
                );
                if (open is null)
                    return;
                start = Math.Max(start, open.StartMinute);
            }
            var end = start + poi.DwellMinutes;
            var duration = end - window.Start;
            if (end > window.End || duration > limits.Duration || walking > limits.Walk)
                return;
            path.Add(
                new(
                    id,
                    poi.Title,
                    StopWindow.Clock(start),
                    StopWindow.Clock(end),
                    poi.DwellMinutes,
                    incoming?.Id,
                    incoming?.MaximumMinutes ?? 0,
                    intervals is not null
                )
            );
            var coverage = brief.Interests.Count(i =>
                covered.Contains(i) || path.Any(s => pois[s.Id].Tags.Contains(i))
            );
            // DFS visits stable POI-ID sequences in lexical order; retain the first exact tie.
            if (
                coverage > bestCoverage
                || coverage == bestCoverage
                    && (path.Count > best.Length || path.Count == best.Length && walking < bestWalk)
            )
            {
                best = path.ToArray();
                bestCoverage = coverage;
                bestWalk = walking;
                bestDuration = duration;
            }
            if (path.Count < limits.Stops)
                foreach (var edge in outgoing[id])
                    if (eligibleEdges.Contains(edge.Id))
                        Visit(
                            edge.ToPoiId,
                            edge,
                            end + edge.MaximumMinutes,
                            walking + edge.MaximumMinutes
                        );
            path.RemoveAt(path.Count - 1);
        }
    }
}
