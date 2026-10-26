using ErrorOr;

namespace Travel.Modules.Trips.Core.Itineraries;

public enum ItineraryFactKind
{
    IdentityLocation,
    Access,
    Walking,
}

public enum ItineraryFactMethod
{
    SourceRecord,
    Observed,
    Modelled,
}

public enum ItineraryRightsBasis
{
    Cc0,
    OriginalObservation,
    ExplicitPermission,
    Odbl,
}

public enum ItineraryFactField
{
    Identity,
    Coordinates,
    OpeningHours,
    WalkingDuration,
    Restrictions,
}

public sealed class ItineraryModelSource
{
    public const string SupportedProfileVersion = "profile-v1";
    public const string SaintPetersburgProfileVersion = "spb-outdoor-profile-v1";
    public const string SupportedPairSelectorVersion = "pair-selector-v1";
    public const string SupportedModelVersion = "model-v1";
    public string SourceUrl { get; }
    public string InputSha256 { get; }
    public string PublicationInputSha256 { get; }
    public DateTimeOffset SnapshotPublishedAtUtc { get; }
    public DateTimeOffset DownloadedAtUtc { get; }
    public DateTimeOffset BuildAsOfUtc { get; }
    public string ProfileVersion { get; }
    public string PairSelectorVersion { get; }
    public string ModelVersion { get; }
    public DateTimeOffset ValidUntil { get; }

    private ItineraryModelSource(
        string sourceUrl,
        string inputSha256,
        string publicationInputSha256,
        DateTimeOffset publication,
        DateTimeOffset downloaded,
        DateTimeOffset build,
        string profile,
        string pairSelector,
        string model
    )
    {
        SourceUrl = sourceUrl;
        InputSha256 = inputSha256;
        PublicationInputSha256 = publicationInputSha256;
        SnapshotPublishedAtUtc = publication;
        DownloadedAtUtc = downloaded;
        BuildAsOfUtc = build;
        ProfileVersion = profile;
        PairSelectorVersion = pairSelector;
        ModelVersion = model;
        ValidUntil = publication.AddDays(30);
    }

    public static ErrorOr<ItineraryModelSource> Create(
        string? sourceUrl,
        string? inputSha256,
        string? publicationInputSha256,
        DateTimeOffset snapshotPublishedAtUtc,
        DateTimeOffset downloadedAtUtc,
        DateTimeOffset buildAsOfUtc,
        string? profileVersion,
        string? pairSelectorVersion,
        string? modelVersion
    )
    {
        if (
            !ItineraryValidation.Https(sourceUrl)
            || !ItineraryValidation.Hash(inputSha256)
            || !ItineraryValidation.Hash(publicationInputSha256)
            || inputSha256 != publicationInputSha256
            || !ItineraryValidation.Utc(snapshotPublishedAtUtc)
            || !ItineraryValidation.Utc(downloadedAtUtc)
            || !ItineraryValidation.Utc(buildAsOfUtc)
            || snapshotPublishedAtUtc > downloadedAtUtc
            || downloadedAtUtc > buildAsOfUtc
            || snapshotPublishedAtUtc > DateTimeOffset.MaxValue.AddDays(-30)
            || buildAsOfUtc - snapshotPublishedAtUtc >= TimeSpan.FromDays(30)
            || profileVersion is not (SupportedProfileVersion or SaintPetersburgProfileVersion)
            || pairSelectorVersion != SupportedPairSelectorVersion
            || modelVersion != SupportedModelVersion
        )
            return TripErrors.InvalidInput;
        return new ItineraryModelSource(
            sourceUrl!,
            inputSha256!,
            publicationInputSha256!,
            snapshotPublishedAtUtc,
            downloadedAtUtc,
            buildAsOfUtc,
            profileVersion,
            pairSelectorVersion,
            modelVersion
        );
    }

    internal bool HasSameInput(ItineraryModelSource other) =>
        SourceUrl == other.SourceUrl
        && InputSha256 == other.InputSha256
        && PublicationInputSha256 == other.PublicationInputSha256
        && SnapshotPublishedAtUtc == other.SnapshotPublishedAtUtc
        && DownloadedAtUtc == other.DownloadedAtUtc
        && BuildAsOfUtc == other.BuildAsOfUtc
        && ProfileVersion == other.ProfileVersion
        && PairSelectorVersion == other.PairSelectorVersion
        && ModelVersion == other.ModelVersion;
}

// Integer scenario assumptions over mapped geometry, never a measured or calibrated range.
public sealed class ItineraryModelledWalk
{
    public ItineraryModelSource Source { get; }
    public string FromPoiId { get; }
    public string ToPoiId { get; }
    public string PathId { get; }
    public string PathHash { get; }
    public long PathLengthMillimetres { get; }
    public int CrossingCount { get; }
    public long FromAnchorNodeId { get; }
    public long ToAnchorNodeId { get; }
    public int MinimumMinutes { get; }
    public int MaximumMinutes { get; }
    public string CalibrationStatus => "not-evaluated";

    private ItineraryModelledWalk(
        ItineraryModelSource source,
        string from,
        string to,
        string pathId,
        string hash,
        long length,
        int crossings,
        long fromAnchor,
        long toAnchor,
        int lower,
        int upper
    )
    {
        Source = source;
        FromPoiId = from;
        ToPoiId = to;
        PathId = pathId;
        PathHash = hash;
        PathLengthMillimetres = length;
        CrossingCount = crossings;
        FromAnchorNodeId = fromAnchor;
        ToAnchorNodeId = toAnchor;
        MinimumMinutes = lower;
        MaximumMinutes = upper;
    }

    public static ErrorOr<ItineraryModelledWalk> Create(
        ItineraryModelSource? source,
        string? fromPoiId,
        string? toPoiId,
        string? pathId,
        string? pathHash,
        long pathLengthMillimetres,
        int crossingCount,
        long fromAnchorNodeId,
        long toAnchorNodeId
    )
    {
        if (
            source is null
            || !ItineraryValidation.Id(fromPoiId)
            || !ItineraryValidation.Id(toPoiId)
            || fromPoiId == toPoiId
            || !ItineraryValidation.Id(pathId)
            || !ItineraryValidation.Hash(pathHash)
            || pathLengthMillimetres < 0
            || crossingCount < 0
            || fromAnchorNodeId <= 0
            || toAnchorNodeId <= 0
            || (pathLengthMillimetres == 0) != (fromAnchorNodeId == toAnchorNodeId)
        )
            return TripErrors.InvalidInput;
        var lower = Math.Max(1, Ceiling(pathLengthMillimetres, 75000));
        var upper = Math.Max(lower, Ceiling(pathLengthMillimetres, 45000) + crossingCount);
        if (lower > 120 || upper > 120)
            return TripErrors.InvalidInput;
        return new ItineraryModelledWalk(
            source,
            fromPoiId!,
            toPoiId!,
            pathId!,
            pathHash!,
            pathLengthMillimetres,
            crossingCount,
            fromAnchorNodeId,
            toAnchorNodeId,
            (int)lower,
            (int)upper
        );
    }

    private static long Ceiling(long value, long rate) =>
        value / rate + (value % rate == 0 ? 0 : 1);
}

public sealed class ItineraryDemoPreparation
{
    public string DataMode { get; }
    public string PreparedBy { get; }
    public string? HumanCurator { get; }
    public string? HumanReviewer { get; }
    public string HumanQuality { get; }
    public string CalibrationStatus { get; }
    public DateTimeOffset BuildAsOfUtc { get; }

    private ItineraryDemoPreparation(
        string mode,
        string preparedBy,
        string quality,
        string calibration,
        DateTimeOffset build
    )
    {
        DataMode = mode;
        PreparedBy = preparedBy;
        HumanCurator = null;
        HumanReviewer = null;
        HumanQuality = quality;
        CalibrationStatus = calibration;
        BuildAsOfUtc = build;
    }

    public static ErrorOr<ItineraryDemoPreparation> Create(
        string? dataMode,
        string? preparedBy,
        string? humanCurator,
        string? humanReviewer,
        string? humanQuality,
        string? calibrationStatus,
        DateTimeOffset buildAsOfUtc
    ) =>
        dataMode == "osm-modelled-demo"
        && ItineraryValidation.Id(preparedBy)
        && humanCurator is null
        && humanReviewer is null
        && humanQuality == "not-evaluated"
        && calibrationStatus == "not-evaluated"
        && ItineraryValidation.Utc(buildAsOfUtc)
            ? new ItineraryDemoPreparation(
                dataMode,
                preparedBy!,
                humanQuality,
                calibrationStatus,
                buildAsOfUtc
            )
            : TripErrors.InvalidInput;
}

public sealed class ItineraryEvidence
{
    public string Id { get; }
    public ItineraryFactKind Kind { get; }
    public string ScopeId { get; }
    public ItineraryFactMethod Method { get; }
    public ItineraryRightsBasis RightsBasis { get; }
    public string RightsEvidence { get; }
    public string SourceUrl { get; }
    public DateTimeOffset RetrievedAtUtc { get; }
    public DateTimeOffset ProcessedAtUtc { get; }
    public DateTimeOffset? ObservedAt { get; }
    public DateTimeOffset ValidUntil { get; }
    public IReadOnlyList<ItineraryFactField> PermittedFields { get; }
    public ItineraryModelledWalk? ModelledWalk { get; }

    private ItineraryEvidence(
        string id,
        ItineraryFactKind kind,
        string scope,
        ItineraryFactMethod method,
        ItineraryRightsBasis rights,
        string rightsEvidence,
        string source,
        DateTimeOffset retrieved,
        DateTimeOffset processed,
        DateTimeOffset? observed,
        DateTimeOffset validUntil,
        IReadOnlyList<ItineraryFactField> fields,
        ItineraryModelledWalk? model
    )
    {
        Id = id;
        Kind = kind;
        ScopeId = scope;
        Method = method;
        RightsBasis = rights;
        RightsEvidence = rightsEvidence;
        SourceUrl = source;
        RetrievedAtUtc = retrieved;
        ProcessedAtUtc = processed;
        ObservedAt = observed;
        ValidUntil = validUntil;
        PermittedFields = Array.AsReadOnly(fields.ToArray());
        ModelledWalk = model;
    }

    public static ErrorOr<ItineraryEvidence> Create(
        string? id,
        ItineraryFactKind kind,
        string? scopeId,
        ItineraryFactMethod method,
        ItineraryRightsBasis rightsBasis,
        string? rightsEvidence,
        string? sourceUrl,
        DateTimeOffset retrievedAtUtc,
        DateTimeOffset processedAtUtc,
        DateTimeOffset? observedAt,
        DateTimeOffset validUntil,
        IReadOnlyList<ItineraryFactField>? permittedFields,
        ItineraryModelledWalk? modelledWalk
    )
    {
        if (
            !ItineraryValidation.Id(id)
            || !ItineraryValidation.Id(scopeId)
            || !Enum.IsDefined(kind)
            || !Enum.IsDefined(method)
            || !Enum.IsDefined(rightsBasis)
            || !ItineraryValidation.Text(rightsEvidence, 500)
            || !ItineraryValidation.Https(sourceUrl)
            || !ItineraryValidation.Utc(retrievedAtUtc)
            || !ItineraryValidation.Utc(processedAtUtc)
            || !ItineraryValidation.Utc(validUntil)
            || retrievedAtUtc > processedAtUtc
            || processedAtUtc >= validUntil
            || (
                observedAt is { } observed
                && (!ItineraryValidation.Utc(observed) || observed > retrievedAtUtc)
            )
            || permittedFields is null
            || permittedFields.Count is < 1 or > 5
            || permittedFields.Distinct().Count() != permittedFields.Count
            || permittedFields.Any(f => !Enum.IsDefined(f))
        )
            return TripErrors.InvalidInput;
        var origin = retrievedAtUtc;
        var maximumDays = 0;
        switch (kind)
        {
            case ItineraryFactKind.IdentityLocation:
                if (
                    method != ItineraryFactMethod.SourceRecord
                    || rightsBasis != ItineraryRightsBasis.Cc0
                    || observedAt is not null
                    || modelledWalk is not null
                    || !Fields(
                        permittedFields,
                        ItineraryFactField.Identity,
                        ItineraryFactField.Coordinates
                    )
                )
                    return TripErrors.InvalidInput;
                maximumDays = 90;
                break;
            case ItineraryFactKind.Access:
                if (
                    modelledWalk is not null
                    || !Fields(permittedFields, ItineraryFactField.OpeningHours)
                )
                    return TripErrors.InvalidInput;
                if (
                    method == ItineraryFactMethod.SourceRecord
                    && rightsBasis == ItineraryRightsBasis.ExplicitPermission
                    && observedAt is null
                ) { }
                else if (
                    method == ItineraryFactMethod.Observed
                    && rightsBasis == ItineraryRightsBasis.OriginalObservation
                    && observedAt is { } observation
                )
                    origin = observation;
                else
                    return TripErrors.InvalidInput;
                maximumDays = 7;
                break;
            case ItineraryFactKind.Walking:
                if (
                    method != ItineraryFactMethod.Modelled
                    || rightsBasis != ItineraryRightsBasis.Odbl
                    || observedAt is not null
                    || modelledWalk is null
                    || !Fields(
                        permittedFields,
                        ItineraryFactField.WalkingDuration,
                        ItineraryFactField.Restrictions
                    )
                    || sourceUrl != modelledWalk.Source.SourceUrl
                    || retrievedAtUtc != modelledWalk.Source.DownloadedAtUtc
                    || processedAtUtc != modelledWalk.Source.BuildAsOfUtc
                    || validUntil != modelledWalk.Source.ValidUntil
                )
                    return TripErrors.InvalidInput;
                break;
        }
        if (maximumDays > 0 && validUntil - origin > TimeSpan.FromDays(maximumDays))
            return TripErrors.InvalidInput;
        return new ItineraryEvidence(
            id!,
            kind,
            scopeId!,
            method,
            rightsBasis,
            rightsEvidence!,
            sourceUrl!,
            retrievedAtUtc,
            processedAtUtc,
            observedAt,
            validUntil,
            permittedFields,
            modelledWalk
        );
    }

    private static bool Fields(
        IReadOnlyList<ItineraryFactField> actual,
        params ItineraryFactField[] expected
    ) => actual.Count == expected.Length && expected.All(actual.Contains);

    public bool IsKnownAt(DateTimeOffset instant) =>
        instant >= ProcessedAtUtc && instant < ValidUntil;

    // The whole requested local day must fit the lease; processing local date is inclusive.
    public bool IsKnownOn(DateOnly date, DestinationSelection savedDestination)
    {
        if (
            savedDestination is null
            || !ItineraryValidation.TryZone(savedDestination.TimeZone, out var zone)
            || date == DateOnly.MaxValue
        )
            return false;
        var processedDate = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(ProcessedAtUtc, zone!).DateTime
        );
        var end = date.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        if (zone!.IsInvalidTime(end) || zone.IsAmbiguousTime(end))
            return false;
        return date >= processedDate
            && TimeZoneInfo.ConvertTimeToUtc(end, zone) <= ValidUntil.UtcDateTime;
    }
}

public sealed class ItineraryWeeklyInterval
{
    public DayOfWeek Day { get; }
    public int StartMinute { get; }
    public int EndMinute { get; }

    private ItineraryWeeklyInterval(DayOfWeek day, int startMinute, int endMinute) =>
        (Day, StartMinute, EndMinute) = (day, startMinute, endMinute);

    public static ErrorOr<ItineraryWeeklyInterval> Create(
        DayOfWeek day,
        int startMinute,
        int endMinute
    ) =>
        Enum.IsDefined(day) && startMinute >= 0 && endMinute <= 1440 && startMinute < endMinute
            ? new ItineraryWeeklyInterval(day, startMinute, endMinute)
            : TripErrors.InvalidInput;
}

public sealed class ItineraryAccess
{
    public string PoiId { get; }
    public bool IsKnown { get; }
    public string? EvidenceId { get; }
    public IReadOnlyList<ItineraryWeeklyInterval> WeeklyIntervals { get; }
    public IReadOnlyList<DateOnly> ClosedDates { get; }

    private ItineraryAccess(
        string poiId,
        bool isKnown,
        string? evidenceId,
        IReadOnlyList<ItineraryWeeklyInterval> intervals,
        IReadOnlyList<DateOnly> closedDates
    )
    {
        PoiId = poiId;
        IsKnown = isKnown;
        EvidenceId = evidenceId;
        WeeklyIntervals = Array.AsReadOnly(
            intervals.OrderBy(i => i.Day).ThenBy(i => i.StartMinute).ToArray()
        );
        ClosedDates = Array.AsReadOnly(closedDates.Order().ToArray());
    }

    public static ErrorOr<ItineraryAccess> Create(
        string? poiId,
        bool isKnown,
        string? evidenceId,
        IReadOnlyList<ItineraryWeeklyInterval>? weeklyIntervals,
        IReadOnlyList<DateOnly>? closedDates
    )
    {
        if (
            !ItineraryValidation.Id(poiId)
            || weeklyIntervals is null
            || closedDates is null
            || weeklyIntervals.Count > 56
            || closedDates.Count > 366
            || weeklyIntervals.Any(i => i is null)
            || closedDates.Distinct().Count() != closedDates.Count
            || (
                isKnown
                    ? !ItineraryValidation.Id(evidenceId)
                    : evidenceId is not null || weeklyIntervals.Count != 0 || closedDates.Count != 0
            )
        )
            return TripErrors.InvalidInput;
        var intervals = weeklyIntervals.OrderBy(i => i.Day).ThenBy(i => i.StartMinute).ToArray();
        for (var i = 1; i < intervals.Length; i++)
            if (
                intervals[i].Day == intervals[i - 1].Day
                && intervals[i].StartMinute < intervals[i - 1].EndMinute
            )
                return TripErrors.InvalidInput;
        return new ItineraryAccess(poiId!, isKnown, evidenceId, intervals, closedDates);
    }

    public IReadOnlyList<ItineraryWeeklyInterval>? GetIntervalsOn(
        DateOnly date,
        ItineraryEvidence? evidence,
        DestinationSelection savedDestination
    )
    {
        if (
            !IsKnown
            || evidence is null
            || evidence.Id != EvidenceId
            || evidence.Kind != ItineraryFactKind.Access
            || evidence.ScopeId != PoiId
            || !evidence.PermittedFields.Contains(ItineraryFactField.OpeningHours)
            || !evidence.IsKnownOn(date, savedDestination)
        )
            return null;
        return Array.AsReadOnly(
            ClosedDates.Contains(date)
                ? []
                : WeeklyIntervals.Where(i => i.Day == date.DayOfWeek).ToArray()
        );
    }
}

public sealed class ItineraryPoi
{
    public string Id { get; }
    public string Title { get; }
    public IReadOnlyList<string> Tags { get; }
    public int DwellMinutes { get; }
    public string WikidataEntityId { get; }
    public string WikidataRevision { get; }
    public string IdentityEvidenceId { get; }
    public decimal Latitude { get; }
    public decimal Longitude { get; }
    public ItineraryAccess Access { get; }

    private ItineraryPoi(
        string id,
        string title,
        IReadOnlyList<string> tags,
        int dwellMinutes,
        string entity,
        string revision,
        string evidenceId,
        decimal latitude,
        decimal longitude,
        ItineraryAccess access
    )
    {
        Id = id;
        Title = title;
        Tags = Array.AsReadOnly(tags.ToArray());
        DwellMinutes = dwellMinutes;
        WikidataEntityId = entity;
        WikidataRevision = revision;
        IdentityEvidenceId = evidenceId;
        Latitude = latitude;
        Longitude = longitude;
        Access = access;
    }

    public static ErrorOr<ItineraryPoi> Create(
        string? id,
        string? title,
        IReadOnlyList<string>? tags,
        int dwellMinutes,
        string? wikidataEntityId,
        string? wikidataRevision,
        string? identityEvidenceId,
        decimal latitude,
        decimal longitude,
        ItineraryAccess? access
    )
    {
        if (
            !ItineraryValidation.Id(id)
            || !ItineraryValidation.Text(title, 100)
            || tags is null
            || tags.Count is < 1 or > 4
            || tags.Distinct(StringComparer.Ordinal).Count() != tags.Count
            || tags.Any(t => !ItineraryValidation.Tag(t))
            || dwellMinutes is < 15 or > 120
            || wikidataEntityId is null
            || !wikidataEntityId.StartsWith('Q')
            || !ItineraryValidation.PositiveDigits(wikidataEntityId[1..])
            || !ItineraryValidation.PositiveDigits(wikidataRevision)
            || !ItineraryValidation.Id(identityEvidenceId)
            || latitude is < -90 or > 90
            || longitude is < -180 or > 180
            || access is null
            || access.PoiId != id
        )
            return TripErrors.InvalidInput;
        return new ItineraryPoi(
            id!,
            title!,
            tags,
            dwellMinutes,
            wikidataEntityId,
            wikidataRevision!,
            identityEvidenceId!,
            latitude,
            longitude,
            access
        );
    }
}

public sealed class ItineraryWalkEdge
{
    public string Id { get; }
    public string FromPoiId { get; }
    public string ToPoiId { get; }
    public int MinimumMinutes { get; }
    public int MaximumMinutes { get; }
    public string EvidenceId { get; }
    public string Restrictions { get; }

    private ItineraryWalkEdge(
        string id,
        string from,
        string to,
        int minimum,
        int maximum,
        string evidence,
        string restrictions
    ) =>
        (Id, FromPoiId, ToPoiId, MinimumMinutes, MaximumMinutes, EvidenceId, Restrictions) = (
            id,
            from,
            to,
            minimum,
            maximum,
            evidence,
            restrictions
        );

    public static ErrorOr<ItineraryWalkEdge> Create(
        string? id,
        string? fromPoiId,
        string? toPoiId,
        int minimumMinutes,
        int maximumMinutes,
        string? evidenceId,
        string? restrictions
    ) =>
        ItineraryValidation.Id(id)
        && ItineraryValidation.Id(fromPoiId)
        && ItineraryValidation.Id(toPoiId)
        && fromPoiId != toPoiId
        && minimumMinutes >= 1
        && maximumMinutes <= 120
        && minimumMinutes <= maximumMinutes
        && ItineraryValidation.Id(evidenceId)
        && ItineraryValidation.Text(restrictions, 500)
            ? new ItineraryWalkEdge(
                id!,
                fromPoiId!,
                toPoiId!,
                minimumMinutes,
                maximumMinutes,
                evidenceId!,
                restrictions!
            )
            : TripErrors.InvalidInput;
}

public sealed class ValidatedItineraryFacts
{
    public string DatasetId { get; }
    public string Version { get; }
    public string DatasetHash { get; }
    public DestinationSelection Destination { get; }
    public string Coverage { get; }
    public ItineraryDemoPreparation Preparation { get; }
    public ItineraryModelSource ModelSource { get; }
    public string DataMode => Preparation.DataMode;
    public string PreparedBy => Preparation.PreparedBy;
    public string? HumanCurator => Preparation.HumanCurator;
    public string? HumanReviewer => Preparation.HumanReviewer;
    public string HumanQuality => Preparation.HumanQuality;
    public string CalibrationStatus => Preparation.CalibrationStatus;
    public DateTimeOffset BuildAsOfUtc => Preparation.BuildAsOfUtc;
    public DateTimeOffset ReleaseValidUntil { get; }
    public IReadOnlyList<ItineraryPoi> Pois { get; }
    public IReadOnlyList<ItineraryWalkEdge> Edges { get; }
    public IReadOnlyList<ItineraryEvidence> Evidence { get; }

    private ValidatedItineraryFacts(
        string id,
        string version,
        string hash,
        DestinationSelection destination,
        string coverage,
        ItineraryDemoPreparation preparation,
        ItineraryModelSource source,
        DateTimeOffset release,
        IReadOnlyList<ItineraryPoi> pois,
        IReadOnlyList<ItineraryWalkEdge> edges,
        IReadOnlyList<ItineraryEvidence> evidence
    )
    {
        DatasetId = id;
        Version = version;
        DatasetHash = hash;
        Destination = destination;
        Coverage = coverage;
        Preparation = preparation;
        ModelSource = source;
        ReleaseValidUntil = release;
        Pois = Array.AsReadOnly(pois.ToArray());
        Edges = Array.AsReadOnly(edges.ToArray());
        Evidence = Array.AsReadOnly(evidence.ToArray());
    }

    public static ErrorOr<ValidatedItineraryFacts> Create(
        string? datasetId,
        string? version,
        string? datasetHash,
        DestinationSelection? destination,
        string? coverage,
        ItineraryDemoPreparation? preparation,
        ItineraryModelSource? modelSource,
        DateTimeOffset releaseValidUntil,
        IReadOnlyList<ItineraryPoi>? pois,
        IReadOnlyList<ItineraryWalkEdge>? edges,
        IReadOnlyList<ItineraryEvidence>? evidence
    )
    {
        if (
            !ItineraryValidation.Id(datasetId)
            || !ItineraryValidation.Version(version)
            || !ItineraryValidation.Hash(datasetHash)
            || destination is null
            || !ItineraryValidation.TryZone(destination.TimeZone, out _)
            || !ItineraryValidation.Id(destination.DestinationId)
            || !ItineraryValidation.Text(destination.DisplayName, 100)
            || !ItineraryValidation.Text(destination.CatalogueVersion, 64)
            || !ItineraryValidation.Text(coverage, 1000)
            || preparation is null
            || modelSource is null
            || (
                modelSource.ProfileVersion == ItineraryModelSource.SaintPetersburgProfileVersion
                && (
                    destination.DestinationId != "saint-petersburg"
                    || destination.TimeZone != "Europe/Moscow"
                    || destination.CatalogueVersion != "1"
                )
            )
            || preparation.BuildAsOfUtc != modelSource.BuildAsOfUtc
            || !ItineraryValidation.Utc(releaseValidUntil)
            || releaseValidUntil <= preparation.BuildAsOfUtc
            || releaseValidUntil > modelSource.ValidUntil
            || pois is null
            || pois.Count is < 18 or > 48
            || pois.Any(p => p is null)
            || edges is null
            || edges.Count is < 60 or > 256
            || edges.Any(e => e is null)
            || evidence is null
            || evidence.Count is < 1 or > 352
            || evidence.Any(e => e is null)
            || pois.Select(p => p.Id).Distinct(StringComparer.Ordinal).Count() != pois.Count
            || edges.Select(e => e.Id).Distinct(StringComparer.Ordinal).Count() != edges.Count
            || evidence.Select(e => e.Id).Distinct(StringComparer.Ordinal).Count() != evidence.Count
            || edges.Select(e => (e.FromPoiId, e.ToPoiId)).Distinct().Count() != edges.Count
        )
            return TripErrors.InvalidInput;
        var poiIds = pois.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
        if (
            edges.Any(e => !poiIds.Contains(e.FromPoiId) || !poiIds.Contains(e.ToPoiId))
            || new[] { "architecture", "culture", "history", "nature" }.Any(tag =>
                pois.Count(p => p.Tags.Contains(tag)) < 3
            )
        )
            return TripErrors.InvalidInput;
        var byId = evidence.ToDictionary(e => e.Id, StringComparer.Ordinal);
        var usedEvidence = new HashSet<string>(StringComparer.Ordinal);
        foreach (var poi in pois)
        {
            if (
                !byId.TryGetValue(poi.IdentityEvidenceId, out var identity)
                || identity.Kind != ItineraryFactKind.IdentityLocation
                || identity.ScopeId != poi.Id
                || identity.SourceUrl
                    != $"https://www.wikidata.org/w/index.php?title={poi.WikidataEntityId}&oldid={poi.WikidataRevision}"
                || releaseValidUntil > identity.ValidUntil
            )
                return TripErrors.InvalidInput;
            usedEvidence.Add(identity.Id);
            if (poi.Access.IsKnown)
            {
                if (
                    !byId.TryGetValue(poi.Access.EvidenceId!, out var access)
                    || access.Kind != ItineraryFactKind.Access
                    || access.ScopeId != poi.Id
                )
                    return TripErrors.InvalidInput;
                usedEvidence.Add(access.Id);
            }
        }
        var pathIds = new HashSet<string>(StringComparer.Ordinal);
        var anchors = new Dictionary<string, long>(StringComparer.Ordinal);
        var digests = new Dictionary<string, (long From, long To, long Length, int Crossings)>(
            StringComparer.Ordinal
        );
        foreach (var edge in edges)
        {
            if (
                !byId.TryGetValue(edge.EvidenceId, out var walk)
                || walk.Kind != ItineraryFactKind.Walking
                || walk.ScopeId != edge.Id
                || walk.ModelledWalk is not { } model
                || !model.Source.HasSameInput(modelSource)
                || model.FromPoiId != edge.FromPoiId
                || model.ToPoiId != edge.ToPoiId
                || model.MinimumMinutes != edge.MinimumMinutes
                || model.MaximumMinutes != edge.MaximumMinutes
                || !pathIds.Add(model.PathId)
            )
                return TripErrors.InvalidInput;
            if (
                !BindAnchor(anchors, edge.FromPoiId, model.FromAnchorNodeId)
                || !BindAnchor(anchors, edge.ToPoiId, model.ToAnchorNodeId)
            )
                return TripErrors.InvalidInput;
            var signature = (
                model.FromAnchorNodeId,
                model.ToAnchorNodeId,
                model.PathLengthMillimetres,
                model.CrossingCount
            );
            if (digests.TryGetValue(model.PathHash, out var prior) && prior != signature)
                return TripErrors.InvalidInput;
            digests[model.PathHash] = signature;
            usedEvidence.Add(walk.Id);
        }
        if (
            usedEvidence.Count != evidence.Count
            || evidence.Any(e => !e.IsKnownAt(preparation.BuildAsOfUtc))
        )
            return TripErrors.InvalidInput;
        return new ValidatedItineraryFacts(
            datasetId!,
            version!,
            datasetHash!,
            destination,
            coverage!,
            preparation,
            modelSource,
            releaseValidUntil,
            pois,
            edges,
            evidence
        );
    }

    private static bool BindAnchor(Dictionary<string, long> anchors, string poi, long node)
    {
        if (anchors.TryGetValue(poi, out var existing))
            return existing == node;
        anchors.Add(poi, node);
        return true;
    }
}

internal static class ItineraryValidation
{
    internal static bool Id(string? value) =>
        value is { Length: >= 1 and <= 64 }
        && value[0] is >= 'a' and <= 'z'
        && value.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-');

    internal static bool Version(string? value) =>
        value is { Length: >= 1 and <= 64 }
        && value.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '.');

    internal static bool Text(string? value, int maximum) =>
        TripText.IsValid(value, maximum) && value == value!.Trim();

    internal static bool Tag(string? value) =>
        value is "architecture" or "culture" or "history" or "nature";

    internal static bool PositiveDigits(string? value) =>
        value is { Length: >= 1 and <= 20 }
        && value[0] is >= '1' and <= '9'
        && value.All(c => c is >= '0' and <= '9');

    internal static bool Hash(string? value) =>
        value is { Length: 64 } && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    internal static bool Utc(DateTimeOffset value) =>
        value != default && value.Offset == TimeSpan.Zero;

    internal static bool Https(string? value) =>
        Text(value, 256)
        && value!.Length <= 256
        && Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && uri.UserInfo.Length == 0
        && uri.Fragment.Length == 0;

    internal static bool TryZone(string? id, out TimeZoneInfo? zone)
    {
        zone = null;
        if (id is null || !id.Contains('/') || !TimeZoneInfo.TryConvertIanaIdToWindowsId(id, out _))
            return false;
        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(id);
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            return false;
        }
    }
}
