using System.Globalization;
using ErrorOr;

namespace Travel.Modules.Trips.Core.Itineraries;

/// <summary>
/// Independently checks the claims of a complete computed result. It does not replay
/// selection, certify ranking optimality, parse geometry, or establish live feasibility.
/// </summary>
public static class ItineraryCandidateValidator
{
    public static ErrorOr<ItineraryOutcome> Validate(
        TripBrief brief,
        DestinationSelection savedDestination,
        ValidatedItineraryFacts facts,
        ItineraryOutcome outcome,
        DateTimeOffset asOfUtc
    )
    {
        if (
            brief is null
            || savedDestination is null
            || facts is null
            || !ItineraryValidation.Utc(asOfUtc)
            || !ItineraryValidation.Id(savedDestination.DestinationId)
            || !BoundedText(savedDestination.DisplayName, 100)
            || !BoundedText(savedDestination.CatalogueVersion, 64)
            || savedDestination.TimeZone is not { Length: <= 256 }
            || !ItineraryValidation.TryZone(savedDestination.TimeZone, out var zone)
            || brief.DestinationId != savedDestination.DestinationId
            || facts.Destination.DestinationId != savedDestination.DestinationId
            || facts.Destination.TimeZone != savedDestination.TimeZone
            || facts.Destination.CatalogueVersion != savedDestination.CatalogueVersion
        )
            return TripErrors.InvalidInput;

        var (target, walkingLimit, durationLimit) = brief.Pace switch
        {
            "relaxed" => (2, 60, 240),
            "balanced" => (3, 90, 360),
            "active" => (4, 120, 480),
            _ => (0, 0, 0),
        };
        if (target == 0 || brief.Adults is < 1 or > 9)
            return TripErrors.InvalidInput;
        if (
            asOfUtc < facts.BuildAsOfUtc
            || asOfUtc >= facts.ReleaseValidUntil
            || asOfUtc >= facts.ModelSource.ValidUntil
            || !MetadataMatches(outcome, savedDestination, facts, asOfUtc)
        )
            return Unavailable();

        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(asOfUtc, zone!).DateTime);
        var dayCount = brief.EndDate.DayNumber - brief.StartDate.DayNumber + 1;
        var scopeReason =
            dayCount is < 1 or > 3 ? "dateRangeUnsupported"
            : brief.StartDate < today ? "historicalDatesUnsupported"
            : brief.EndDate.DayNumber > today.DayNumber + 30 ? "horizonUnsupported"
            : null;
        var requested = brief.Interests.ToHashSet(StringComparer.Ordinal);
        var covered = new HashSet<string>(StringComparer.Ordinal);
        var reasons = new List<string>();
        var uncertainties = new List<string>
        {
            "walkingModelled",
            "calibrationNotEvaluated",
            "humanQualityNotEvaluated",
        };
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

        if (scopeReason is not null)
        {
            if (
                outcome.Kind != ItineraryOutcomeKind.Unsupported
                || outcome.Days.Count != 0
                || outcome.Coverage != ItineraryCoverage.Partial
                || outcome.ExpansionCount != 0
                || outcome.SearchLimited
            )
                return Unavailable();
            reasons.Add(scopeReason);
        }
        else
        {
            if (outcome.Kind == ItineraryOutcomeKind.Unsupported || outcome.Days.Count != dayCount)
                return Unavailable();
            // A seed or extension attempt can add at most one stop. The stricter upper bound
            // follows from current 3-day/48-POI/4-stop/16-beam limits, not a replayed trace.
            // Thus cap exhaustion is a defensive branch unreachable under this contract.
            var maximumPossibleAttempts = dayCount * facts.Pois.Count * (1 + (target - 1) * 16);
            if (
                outcome.ExpansionCount > maximumPossibleAttempts
                || (outcome.SearchLimited && outcome.ExpansionCount != 20_000)
            )
                return Unavailable();
            var pois = facts.Pois.ToDictionary(p => p.Id, StringComparer.Ordinal);
            var evidence = facts.Evidence.ToDictionary(e => e.Id, StringComparer.Ordinal);
            var edges = facts.Edges.ToDictionary(e => e.Id, StringComparer.Ordinal);
            var poiIds = new HashSet<string>(StringComparer.Ordinal);
            var blockIds = new HashSet<Guid>();
            var selectedCount = 0;
            var allTargetsFilled = true;
            var unknownAccess = false;
            for (var dayIndex = 0; dayIndex < dayCount; dayIndex++)
            {
                var day = outcome.Days[dayIndex];
                var date = brief.StartDate.AddDays(dayIndex);
                if (
                    day is null
                    || day.Date != date
                    || day.Blocks is null
                    || day.Edges is null
                    || day.Blocks.Count > target
                    || day.Edges.Count != Math.Max(0, day.Blocks.Count - 1)
                )
                    return Unavailable();
                allTargetsFilled &= day.Blocks.Count == target;
                var dwell = 0;
                var walking = 0;
                for (var index = 0; index < day.Blocks.Count; index++)
                {
                    var block = day.Blocks[index];
                    if (
                        !BlockMatches(
                            block,
                            date,
                            facts,
                            evidence,
                            pois,
                            savedDestination,
                            asOfUtc,
                            out var poi,
                            out var hasUnknownAccess
                        )
                        || !poiIds.Add(block.PoiId)
                        || !blockIds.Add(block.ProposedBlockId)
                    )
                        return Unavailable();
                    selectedCount++;
                    unknownAccess |= hasUnknownAccess;
                    dwell += poi!.DwellMinutes;
                    covered.UnionWith(poi.Tags.Where(requested.Contains));
                    if (index == 0)
                        continue;
                    var previous = day.Blocks[index - 1];
                    var edge = day.Edges[index - 1];
                    if (
                        !EdgeMatches(
                            edge,
                            previous.PoiId,
                            block.PoiId,
                            date,
                            edges,
                            evidence,
                            savedDestination,
                            facts.ModelSource,
                            asOfUtc
                        )
                    )
                        return Unavailable();
                    walking += edge.MaximumMinutes;
                }
                if (walking > walkingLimit || dwell + walking > durationLimit)
                    return Unavailable();
            }
            if (
                outcome.ExpansionCount < selectedCount
                || outcome.Kind
                    != (
                        selectedCount == 0
                            ? ItineraryOutcomeKind.Empty
                            : ItineraryOutcomeKind.Candidate
                    )
            )
                return Unavailable();
            if (!allTargetsFilled)
                reasons.Add("insufficientEligibleStops");
            if (requested.Any(i => !covered.Contains(i)))
                reasons.Add("unmetInterests");
            if (outcome.SearchLimited)
                reasons.Add("searchLimited");
            if (selectedCount == 0)
                reasons.Add("noEligibleStops");
            if (unknownAccess)
                uncertainties.Add("openingHoursUnknown");
            var coverage =
                allTargetsFilled && requested.All(covered.Contains) && !outcome.SearchLimited
                    ? ItineraryCoverage.Filled
                    : ItineraryCoverage.Partial;
            if (outcome.Coverage != coverage)
                return Unavailable();
        }

        if (
            !CodesEqual(outcome.CoveredInterests, covered)
            || !CodesEqual(
                outcome.UnmetInterests,
                requested.Except(covered, StringComparer.Ordinal)
            )
            || !CodesEqual(outcome.Reasons, reasons)
            || !CodesEqual(outcome.Exclusions, exclusions)
            || !CodesEqual(outcome.Uncertainties, uncertainties)
        )
            return Unavailable();
        return outcome;
    }

    private static bool MetadataMatches(
        ItineraryOutcome? outcome,
        DestinationSelection destination,
        ValidatedItineraryFacts facts,
        DateTimeOffset asOfUtc
    )
    {
        if (
            outcome is null
            || !Enum.IsDefined(outcome.Kind)
            || !Enum.IsDefined(outcome.Coverage)
            || outcome.Destination is null
            || !ItineraryValidation.Id(outcome.Destination.DestinationId)
            || !BoundedText(outcome.Destination.DisplayName, 100)
            || outcome.Destination.TimeZone is not { Length: <= 256 }
            || !BoundedText(outcome.Destination.CatalogueVersion, 64)
            || outcome.Destination != destination
            || !ItineraryValidation.Id(outcome.DatasetId)
            || !ItineraryValidation.Version(outcome.DatasetVersion)
            || !ItineraryValidation.Hash(outcome.DatasetHash)
            || !ItineraryValidation.Version(outcome.PolicyVersion)
            || outcome.DatasetId != facts.DatasetId
            || outcome.DatasetVersion != facts.Version
            || outcome.DatasetHash != facts.DatasetHash
            || outcome.PolicyVersion != "bounded-beam-v1"
            || !ItineraryValidation.Utc(outcome.GeneratedAt)
            || !ItineraryValidation.Utc(outcome.ExpiresAt)
            || outcome.GeneratedAt != asOfUtc
            || outcome.Days is null
            || outcome.Days.Count > 3
            || !CodesBounded(outcome.CoveredInterests, 5)
            || !CodesBounded(outcome.UnmetInterests, 5)
            || !CodesBounded(outcome.Reasons, 4)
            || !CodesBounded(outcome.Exclusions, 5)
            || !CodesBounded(outcome.Uncertainties, 4)
            || outcome.ExpansionCount is < 0 or > 20_000
            || !PreparationEquals(outcome.Preparation, facts.Preparation)
            || !SourceEquals(outcome.ModelSource, facts.ModelSource)
        )
            return false;
        var expiry =
            facts.ReleaseValidUntil - asOfUtc > TimeSpan.FromMinutes(15)
                ? asOfUtc.AddMinutes(15)
                : facts.ReleaseValidUntil;
        return outcome.ExpiresAt == expiry;
    }

    private static bool BlockMatches(
        ItineraryCandidateBlock? block,
        DateOnly date,
        ValidatedItineraryFacts facts,
        IReadOnlyDictionary<string, ItineraryEvidence> evidence,
        IReadOnlyDictionary<string, ItineraryPoi> pois,
        DestinationSelection destination,
        DateTimeOffset asOfUtc,
        out ItineraryPoi? poi,
        out bool unknownAccess
    )
    {
        poi = null;
        unknownAccess = false;
        if (
            block is null
            || !ItineraryValidation.Id(block.PoiId)
            || block.ProposedBlockId == Guid.Empty
            || !BoundedText(block.Title, 100)
            || block.Date != date
            || block.StartTime is not null
            || block.EndTime is not null
            || block.Estimate is not null
            || !BoundedText(block.Note, 500)
            || block.Facts is null
            || block.Facts.Count != 2
            || block.OpeningIntervals is null
            || block.OpeningIntervals.Count > 56
            || !pois.TryGetValue(block.PoiId, out poi)
            || block.Title != poi.Title
        )
            return false;
        // Exact initial historical text excludes invented feasibility/observation/budget claims.
        var expectedNote = string.Create(
            CultureInfo.InvariantCulture,
            $"Historical proposal: {facts.DatasetId}/{facts.Version}; asOf={asOfUtc:O}; POI={poi.Id}. \u00A9 OpenStreetMap contributors; ODbL: https://opendatacommons.org/licenses/odbl/1-0/; https://www.openstreetmap.org/copyright."
        );
        if (block.Note != expectedNote)
            return false;
        ItineraryCandidateFact? identity = null;
        ItineraryCandidateFact? access = null;
        foreach (var fact in block.Facts)
        {
            if (fact is null || !Enum.IsDefined(fact.Kind) || !Enum.IsDefined(fact.State))
                return false;
            if (fact.Kind == ItineraryFactKind.IdentityLocation && identity is null)
                identity = fact;
            else if (fact.Kind == ItineraryFactKind.Access && access is null)
                access = fact;
            else
                return false;
        }
        if (
            identity is null
            || access is null
            || identity.State != ItineraryFactState.Known
            || !ItineraryValidation.Id(identity.EvidenceId)
            || identity.UnknownReason is not null
            || identity.EvidenceId != poi.IdentityEvidenceId
            || !evidence.TryGetValue(identity.EvidenceId, out var identitySource)
            || identitySource.Kind != ItineraryFactKind.IdentityLocation
            || identitySource.ScopeId != poi.Id
            || identitySource.Method != ItineraryFactMethod.SourceRecord
            || identitySource.RightsBasis != ItineraryRightsBasis.Cc0
            || identitySource.ObservedAt is not null
            || !FieldsEqual(
                identitySource.PermittedFields,
                ItineraryFactField.Identity,
                ItineraryFactField.Coordinates
            )
            || !identitySource.IsKnownAt(asOfUtc)
            || !identitySource.IsKnownOn(date, destination)
        )
            return false;
        var accessSource = poi.Access.EvidenceId is { } id ? evidence.GetValueOrDefault(id) : null;
        var intervals =
            accessSource is not null && accessSource.IsKnownAt(asOfUtc)
                ? poi.Access.GetIntervalsOn(date, accessSource, destination)
                : null;
        if (intervals is null)
        {
            unknownAccess = true;
            return access.State == ItineraryFactState.Unknown
                && access.EvidenceId is null
                && access.UnknownReason
                    == (
                        poi.Access.IsKnown
                            ? "openingHoursExpiredOrNotApplicable"
                            : "openingHoursUnknown"
                    )
                && block.OpeningIntervals.Count == 0;
        }
        if (
            intervals.Count == 0
            || access.State != ItineraryFactState.Known
            || access.EvidenceId != poi.Access.EvidenceId
            || access.UnknownReason is not null
            || block.OpeningIntervals.Count != intervals.Count
        )
            return false;
        for (var index = 0; index < intervals.Count; index++)
        {
            var actual = block.OpeningIntervals[index];
            var expected = intervals[index];
            if (
                actual is null
                || actual.Day != expected.Day
                || actual.StartMinute != expected.StartMinute
                || actual.EndMinute != expected.EndMinute
            )
                return false;
        }
        return true;
    }

    private static bool EdgeMatches(
        ItineraryCandidateEdge? edge,
        string from,
        string to,
        DateOnly date,
        IReadOnlyDictionary<string, ItineraryWalkEdge> edges,
        IReadOnlyDictionary<string, ItineraryEvidence> evidence,
        DestinationSelection destination,
        ItineraryModelSource source,
        DateTimeOffset asOfUtc
    )
    {
        if (
            edge is null
            || !ItineraryValidation.Id(edge.EdgeId)
            || !ItineraryValidation.Id(edge.FromPoiId)
            || !ItineraryValidation.Id(edge.ToPoiId)
            || !ItineraryValidation.Id(edge.EvidenceId)
            || !BoundedText(edge.Restrictions, 500)
            || edge.FromPoiId != from
            || edge.ToPoiId != to
            || !edges.TryGetValue(edge.EdgeId, out var original)
            || original.FromPoiId != from
            || original.ToPoiId != to
            || edge.MinimumMinutes != original.MinimumMinutes
            || edge.MaximumMinutes != original.MaximumMinutes
            || edge.EvidenceId != original.EvidenceId
            || edge.Restrictions != original.Restrictions
            || edge.Method != ItineraryFactMethod.Modelled
            || edge.RightsBasis != ItineraryRightsBasis.Odbl
            || !evidence.TryGetValue(edge.EvidenceId, out var fact)
            || fact.Kind != ItineraryFactKind.Walking
            || fact.ScopeId != edge.EdgeId
            || fact.Method != edge.Method
            || fact.RightsBasis != edge.RightsBasis
            || fact.ObservedAt is not null
            || !FieldsEqual(
                fact.PermittedFields,
                ItineraryFactField.WalkingDuration,
                ItineraryFactField.Restrictions
            )
            || !fact.IsKnownAt(asOfUtc)
            || !fact.IsKnownOn(date, destination)
            || !WalkEquals(edge.ModelledWalk, fact.ModelledWalk, source)
        )
            return false;
        var model = edge.ModelledWalk;
        // Quotient/remainder avoids addition overflow when calculating integer ceilings.
        var lower = Math.Max(
            1,
            model.PathLengthMillimetres / 75_000
                + (model.PathLengthMillimetres % 75_000 == 0 ? 0 : 1)
        );
        var upper = Math.Max(
            lower,
            model.PathLengthMillimetres / 45_000
                + (model.PathLengthMillimetres % 45_000 == 0 ? 0 : 1)
                + model.CrossingCount
        );
        return edge.MinimumMinutes == lower
            && edge.MaximumMinutes == upper
            && model.MinimumMinutes == lower
            && model.MaximumMinutes == upper
            && lower is >= 1 and <= 120
            && upper is >= 1 and <= 120;
    }

    private static bool WalkEquals(
        ItineraryModelledWalk? actual,
        ItineraryModelledWalk? expected,
        ItineraryModelSource source
    ) =>
        actual is not null
        && expected is not null
        && SourceEquals(actual.Source, source)
        && SourceEquals(actual.Source, expected.Source)
        && actual.FromPoiId == expected.FromPoiId
        && actual.ToPoiId == expected.ToPoiId
        && actual.PathId == expected.PathId
        && actual.PathHash == expected.PathHash
        && actual.PathLengthMillimetres == expected.PathLengthMillimetres
        && actual.CrossingCount == expected.CrossingCount
        && actual.FromAnchorNodeId == expected.FromAnchorNodeId
        && actual.ToAnchorNodeId == expected.ToAnchorNodeId
        && actual.CalibrationStatus == "not-evaluated";

    private static bool SourceEquals(ItineraryModelSource? actual, ItineraryModelSource expected) =>
        actual is not null
        && actual.SourceUrl == expected.SourceUrl
        && actual.InputSha256 == expected.InputSha256
        && actual.PublicationInputSha256 == expected.PublicationInputSha256
        && actual.SnapshotPublishedAtUtc == expected.SnapshotPublishedAtUtc
        && actual.DownloadedAtUtc == expected.DownloadedAtUtc
        && actual.BuildAsOfUtc == expected.BuildAsOfUtc
        && actual.ProfileVersion == expected.ProfileVersion
        && actual.PairSelectorVersion == expected.PairSelectorVersion
        && actual.ModelVersion == expected.ModelVersion
        && actual.ValidUntil == expected.ValidUntil;

    private static bool PreparationEquals(
        ItineraryDemoPreparation? actual,
        ItineraryDemoPreparation expected
    ) =>
        actual is not null
        && actual.DataMode == expected.DataMode
        && actual.PreparedBy == expected.PreparedBy
        && actual.HumanCurator == expected.HumanCurator
        && actual.HumanReviewer == expected.HumanReviewer
        && actual.HumanQuality == expected.HumanQuality
        && actual.CalibrationStatus == expected.CalibrationStatus
        && actual.BuildAsOfUtc == expected.BuildAsOfUtc;

    private static bool FieldsEqual(
        IReadOnlyList<ItineraryFactField> actual,
        params ItineraryFactField[] expected
    ) => actual.Count == expected.Length && expected.All(actual.Contains);

    private static bool BoundedText(string? text, int maximum) =>
        text is not null && text.Length <= maximum * 2 && ItineraryValidation.Text(text, maximum);

    private static bool CodesBounded(IReadOnlyList<string>? codes, int maximum) =>
        codes is not null
        && codes.Count <= maximum
        && codes.All(code => code is { Length: >= 1 and <= 64 } && code.All(char.IsAsciiLetter));

    private static bool CodesEqual(IReadOnlyList<string> actual, IEnumerable<string> expected) =>
        actual.SequenceEqual(expected.Order(StringComparer.Ordinal), StringComparer.Ordinal);

    private static Error Unavailable() =>
        Error.Failure("Trips.ItineraryUnavailable", "Itinerary generation is unavailable.");
}
