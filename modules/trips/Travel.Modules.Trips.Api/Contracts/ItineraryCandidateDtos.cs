using System.Globalization;
using System.Text;
using System.Text.Json;
using ErrorOr;
using Travel.Modules.Trips.Application.Itineraries;
using Travel.Modules.Trips.Core;
using Travel.Modules.Trips.Core.Itineraries;

namespace Travel.Modules.Trips.Api.Contracts;

/// <summary>One v1 wire projection. Package objects never cross this boundary wholesale.</summary>
public sealed class ItineraryCandidateResponseEncoder : IItineraryCandidateResponseEncoder
{
    public const string DerivedDataOffer = "/trips-data/moscow-outdoor/1/moscow-v1.manifest.json";
    public const string Attribution =
        "© OpenStreetMap contributors; ODbL: https://opendatacommons.org/licenses/odbl/1-0/; https://www.openstreetmap.org/copyright.";
    public const string Assumptions =
        "75/45 metres per minute plus one modelled minute per mapped crossing: author scenarios, not observations, confidence or a guaranteed upper bound.";
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public Task<ErrorOr<ReadOnlyMemory<byte>>> EncodeAsync(
        ItineraryCandidateResult result,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            Require(result.BaseRevision > 0 && result.CandidateVersion is 1 or 2);
            object wire = result switch
            {
                ScopeUnsupportedCandidateResult scope => Unsupported(scope),
                ComputedCandidateResult computed => Computed(computed, cancellationToken),
                _ => throw new InvalidOperationException(),
            };
            var bytes = JsonSerializer.SerializeToUtf8Bytes(
                wire,
                new JsonSerializerOptions { MaxDepth = 16 }
            );
            Require(bytes.Length is > 0 and <= 128 * 1024);
            using var parsed = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 });
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<ErrorOr<ReadOnlyMemory<byte>>>(new ReadOnlyMemory<byte>(bytes));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
            when (ex
                    is InvalidOperationException
                        or ArgumentException
                        or JsonException
                        or EncoderFallbackException
                        or KeyNotFoundException
                        or OverflowException
            )
        {
            return Task.FromResult<ErrorOr<ReadOnlyMemory<byte>>>(
                Error.Failure("Trips.ItineraryUnavailable", "Itinerary generation is unavailable.")
            );
        }
    }

    private static object Unsupported(ScopeUnsupportedCandidateResult value)
    {
        Require(
            value.Reason
                is "destinationUnsupported"
                    or "dateRangeUnsupported"
                    or "historicalDatesUnsupported"
                    or "horizonUnsupported"
        );
        Destination(value.Destination);
        Require(value.PolicyVersion == ItineraryPolicy.PolicyVersion);
        return new
        {
            candidateVersion = value.CandidateVersion,
            outcome = "unsupported",
            baseRevision = Digits(value.BaseRevision),
            destinationId = value.Destination.DestinationId,
            timeZone = value.Destination.TimeZone,
            catalogueVersion = value.Destination.CatalogueVersion,
            policyVersion = value.PolicyVersion,
            generatedAt = Utc(value.AsOfUtc),
            reason = value.Reason,
        };
    }

    private static object Computed(ComputedCandidateResult value, CancellationToken ct)
    {
        var o = value.Outcome;
        var facts = value.Facts;
        Require(
            value.CandidateVersion == 1
                ? o.Destination.DestinationId == "moscow"
                : TripsItineraryProtocol.MatchesReleasedTuple(facts)
        );
        Destination(o.Destination);
        Require(o.Kind is ItineraryOutcomeKind.Candidate or ItineraryOutcomeKind.Empty);
        Require(
            o.Destination == facts.Destination
                && o.DatasetId == facts.DatasetId
                && o.DatasetVersion == facts.Version
                && o.DatasetHash == facts.DatasetHash
                && o.PolicyVersion == ItineraryPolicy.PolicyVersion
                && o.Preparation == facts.Preparation
                && o.ModelSource == facts.ModelSource
                && o.GeneratedAt >= facts.BuildAsOfUtc
                && o.ExpiresAt > o.GeneratedAt
                && o.ExpiresAt <= facts.ReleaseValidUntil
                && o.ExpiresAt <= o.GeneratedAt.AddMinutes(15)
                && o.Days.Count is >= 1 and <= 3
                && Enum.IsDefined(o.Coverage)
                && o.ExpansionCount is >= 0 and <= 20000
        );
        Require(
            o.Days.Sum(d => d.Blocks.Count) <= 12
                && o.Days.Sum(d => d.Edges.Count) <= 48
                && o.Days.Sum(d => d.Blocks.Sum(b => b.Facts.Count)) <= 96
        );
        Require((o.Kind == ItineraryOutcomeKind.Empty) == (o.Days.Sum(d => d.Blocks.Count) == 0));
        Id(o.DatasetId);
        Version(o.DatasetVersion);
        Hash(o.DatasetHash);
        Version(o.PolicyVersion);
        var pois = facts.Pois.ToDictionary(p => p.Id, StringComparer.Ordinal);
        var evidence = facts.Evidence.ToDictionary(e => e.Id, StringComparer.Ordinal);
        var sourceEdges = facts.Edges.ToDictionary(e => e.Id, StringComparer.Ordinal);
        var used = new HashSet<string>(StringComparer.Ordinal);
        var blockIds = new HashSet<Guid>();
        var poiIds = new HashSet<string>(StringComparer.Ordinal);
        var days = o
            .Days.Select(
                (day, dayIndex) =>
                {
                    ct.ThrowIfCancellationRequested();
                    Require(
                        day.Blocks.Count <= 4
                            && day.Edges.Count == Math.Max(0, day.Blocks.Count - 1)
                            && (dayIndex == 0 || day.Date == o.Days[dayIndex - 1].Date.AddDays(1))
                    );
                    var blocks = day
                        .Blocks.Select(block =>
                        {
                            var poi = pois[block.PoiId];
                            Require(
                                blockIds.Add(block.ProposedBlockId)
                                    && block.ProposedBlockId != Guid.Empty
                                    && poiIds.Add(block.PoiId)
                                    && block.Title == poi.Title
                                    && block.Date == day.Date
                                    && block.StartTime is null
                                    && block.EndTime is null
                                    && block.Estimate is null
                                    && block.Facts.Count == 2
                                    && block.OpeningIntervals.Count <= 56
                            );
                            Text(block.Title, 100);
                            Text(block.Note, 500);
                            var identity = block.Facts.Single(f =>
                                f.Kind == ItineraryFactKind.IdentityLocation
                            );
                            var access = block.Facts.Single(f =>
                                f.Kind == ItineraryFactKind.Access
                            );
                            Require(
                                identity.State == ItineraryFactState.Known
                                    && identity.EvidenceId == poi.IdentityEvidenceId
                            );
                            Require(
                                access.State
                                    is ItineraryFactState.Known
                                        or ItineraryFactState.Unknown
                            );
                            foreach (var f in block.Facts)
                            {
                                if (f.State == ItineraryFactState.Known)
                                {
                                    Require(f.UnknownReason is null && f.EvidenceId is not null);
                                    var e = evidence[f.EvidenceId!];
                                    Require(
                                        e.Kind == f.Kind
                                            && e.ScopeId == block.PoiId
                                            && e.IsKnownAt(o.GeneratedAt)
                                            && e.IsKnownOn(day.Date, o.Destination)
                                    );
                                    if (f.Kind == ItineraryFactKind.Access)
                                        Require(f.EvidenceId == poi.Access.EvidenceId);
                                    used.Add(e.Id);
                                }
                                else
                                    Require(
                                        f.Kind == ItineraryFactKind.Access
                                            && f.EvidenceId is null
                                            && f.UnknownReason
                                                is "openingHoursUnknown"
                                                    or "openingHoursExpiredOrNotApplicable"
                                    );
                            }
                            var accessEvidence = poi.Access.EvidenceId is { } aid
                                ? evidence[aid]
                                : null;
                            var expectedIntervals =
                                accessEvidence is not null
                                && accessEvidence.IsKnownAt(o.GeneratedAt)
                                    ? poi.Access.GetIntervalsOn(
                                        day.Date,
                                        accessEvidence,
                                        o.Destination
                                    )
                                    : null;
                            Require(
                                (expectedIntervals is null)
                                    == (access.State == ItineraryFactState.Unknown)
                            );
                            Require(block.OpeningIntervals.SequenceEqual(expectedIntervals ?? []));
                            return new
                            {
                                poiId = block.PoiId,
                                proposedBlockId = block.ProposedBlockId.ToString("D"),
                                title = block.Title,
                                date = Date(block.Date),
                                startTime = (string?)null,
                                endTime = (string?)null,
                                estimate = (object?)null,
                                note = block.Note,
                                dwellMinutes = poi.DwellMinutes,
                                tags = poi.Tags,
                                wikidataEntityId = poi.WikidataEntityId,
                                wikidataRevision = poi.WikidataRevision,
                                latitude = poi.Latitude,
                                longitude = poi.Longitude,
                                facts = block
                                    .Facts.Select(f => new
                                    {
                                        kind = Kind(f.Kind),
                                        state = f.State == ItineraryFactState.Known
                                            ? "known"
                                            : "unknown",
                                        evidenceId = f.EvidenceId,
                                        unknownReason = f.UnknownReason,
                                    })
                                    .ToArray(),
                                openingIntervals = block
                                    .OpeningIntervals.Select(i => new
                                    {
                                        day = (int)i.Day,
                                        startMinute = i.StartMinute,
                                        endMinute = i.EndMinute,
                                    })
                                    .ToArray(),
                            };
                        })
                        .ToArray();
                    var edges = day
                        .Edges.Select(
                            (edge, index) =>
                            {
                                var source = sourceEdges[edge.EdgeId];
                                var e = evidence[edge.EvidenceId];
                                var path = edge.ModelledWalk;
                                Require(
                                    source.FromPoiId == edge.FromPoiId
                                        && source.ToPoiId == edge.ToPoiId
                                        && source.MinimumMinutes == edge.MinimumMinutes
                                        && source.MaximumMinutes == edge.MaximumMinutes
                                        && source.Restrictions == edge.Restrictions
                                        && source.EvidenceId == edge.EvidenceId
                                        && edge.FromPoiId == day.Blocks[index].PoiId
                                        && edge.ToPoiId == day.Blocks[index + 1].PoiId
                                        && e.ScopeId == edge.EdgeId
                                        && e.Kind == ItineraryFactKind.Walking
                                        && e.ModelledWalk == path
                                        && path.Source == o.ModelSource
                                        && edge.Method == ItineraryFactMethod.Modelled
                                        && edge.RightsBasis == ItineraryRightsBasis.Odbl
                                        && e.IsKnownAt(o.GeneratedAt)
                                        && e.IsKnownOn(day.Date, o.Destination)
                                );
                                used.Add(e.Id);
                                Text(edge.Restrictions, 500);
                                return new
                                {
                                    edgeId = edge.EdgeId,
                                    fromPoiId = edge.FromPoiId,
                                    toPoiId = edge.ToPoiId,
                                    minimumMinutes = edge.MinimumMinutes,
                                    maximumMinutes = edge.MaximumMinutes,
                                    evidenceId = edge.EvidenceId,
                                    restrictions = edge.Restrictions,
                                    method = "modelled",
                                    rightsBasis = "odbl",
                                    modelledWalk = new
                                    {
                                        pathId = path.PathId,
                                        pathHash = path.PathHash,
                                        pathLengthMillimetres = Digits(path.PathLengthMillimetres),
                                        crossingCount = path.CrossingCount,
                                        fromAnchorNodeId = Digits(path.FromAnchorNodeId),
                                        toAnchorNodeId = Digits(path.ToAnchorNodeId),
                                        modelVersion = path.Source.ModelVersion,
                                        calibrationStatus = path.CalibrationStatus,
                                    },
                                };
                            }
                        )
                        .ToArray();
                    return new
                    {
                        date = Date(day.Date),
                        blocks,
                        edges,
                    };
                }
            )
            .ToArray();
        Require(used.Count <= 48);
        return new
        {
            candidateVersion = value.CandidateVersion,
            outcome = o.Kind == ItineraryOutcomeKind.Candidate ? "candidate" : "empty",
            baseRevision = Digits(value.BaseRevision),
            destinationId = o.Destination.DestinationId,
            timeZone = o.Destination.TimeZone,
            catalogueVersion = o.Destination.CatalogueVersion,
            datasetId = o.DatasetId,
            datasetVersion = o.DatasetVersion,
            datasetHash = o.DatasetHash,
            policyVersion = o.PolicyVersion,
            generatedAt = Utc(o.GeneratedAt),
            expiresAt = Utc(o.ExpiresAt),
            releaseValidUntil = Utc(facts.ReleaseValidUntil),
            dataMode = facts.DataMode,
            humanQuality = facts.HumanQuality,
            preparation = new
            {
                preparedBy = facts.PreparedBy,
                humanCurator = facts.HumanCurator,
                humanReviewer = facts.HumanReviewer,
                calibrationStatus = facts.CalibrationStatus,
                buildAsOfUtc = Utc(facts.BuildAsOfUtc),
            },
            modelSource = Model(o.ModelSource),
            attribution = Attribution,
            derivedDataOffer = value.CandidateVersion == 1
                ? DerivedDataOffer
                : TripsItineraryProtocol.Offer(facts),
            coverage = o.Coverage == ItineraryCoverage.Filled ? "filled" : "partial",
            coveredInterests = Strings(o.CoveredInterests),
            unmetInterests = Strings(o.UnmetInterests),
            reasons = Strings(o.Reasons),
            exclusions = Strings(o.Exclusions),
            uncertainties = Strings(o.Uncertainties),
            searchLimited = o.SearchLimited,
            expansionCount = o.ExpansionCount,
            days,
            evidence = used.Order(StringComparer.Ordinal)
                .Select(id => Evidence(evidence[id]))
                .ToArray(),
        };
    }

    private static object Model(ItineraryModelSource s) =>
        new
        {
            sourceUrl = Url(s.SourceUrl),
            inputSha256 = s.InputSha256,
            publicationInputSha256 = s.PublicationInputSha256,
            snapshotPublishedAtUtc = Utc(s.SnapshotPublishedAtUtc),
            downloadedAtUtc = Utc(s.DownloadedAtUtc),
            buildAsOfUtc = Utc(s.BuildAsOfUtc),
            profileVersion = s.ProfileVersion,
            pairSelectorVersion = s.PairSelectorVersion,
            modelVersion = s.ModelVersion,
            validUntil = Utc(s.ValidUntil),
            assumptions = Assumptions,
            calibrationStatus = "not-evaluated",
        };

    private static object Evidence(ItineraryEvidence e)
    {
        Text(e.RightsEvidence, 500);
        return new
        {
            id = e.Id,
            kind = Kind(e.Kind),
            scopeId = e.ScopeId,
            method = Method(e.Method),
            rightsBasis = Rights(e.RightsBasis),
            rightsEvidence = e.RightsEvidence,
            sourceLabel = new Uri(Url(e.SourceUrl)).Host,
            sourceUrl = e.SourceUrl,
            retrievedAtUtc = Utc(e.RetrievedAtUtc),
            processedAtUtc = Utc(e.ProcessedAtUtc),
            observedAt = e.ObservedAt is { } at ? Utc(at) : null,
            validUntil = Utc(e.ValidUntil),
            permittedFields = e.PermittedFields.Select(Field).ToArray(),
        };
    }

    public static string Utc(DateTimeOffset value)
    {
        Require(value != default && value.Offset == TimeSpan.Zero);
        return value.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);
    }

    private static string Date(DateOnly value) =>
        value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Digits(long value) => value.ToString(CultureInfo.InvariantCulture);

    private static void Destination(DestinationSelection value)
    {
        Id(value.DestinationId);
        Text(value.TimeZone, 256);
        Text(value.CatalogueVersion, 64);
    }

    private static void Id(string value) =>
        Require(
            value.Length is >= 1 and <= 64
                && value[0] is >= 'a' and <= 'z'
                && value.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-')
        );

    private static void Version(string value) =>
        Require(
            value.Length is >= 1 and <= 64
                && value.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '.')
        );

    private static void Hash(string value) =>
        Require(value.Length == 64 && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f'));

    private static void Text(string value, int max)
    {
        StrictUtf8.GetByteCount(value);
        Require(value.EnumerateRunes().Count() <= max && !value.Any(char.IsControl));
    }

    private static string Url(string value)
    {
        Text(value, 256);
        Require(
            value.Length <= 256
                && Uri.TryCreate(value, UriKind.Absolute, out var uri)
                && uri.Scheme == "https"
                && uri.UserInfo.Length == 0
                && uri.Fragment.Length == 0
        );
        return value;
    }

    private static string[] Strings(IReadOnlyList<string> value)
    {
        Require(value.Count <= 16 && value.Distinct(StringComparer.Ordinal).Count() == value.Count);
        foreach (var item in value)
            Text(item, 64);
        return value.ToArray();
    }

    private static void Require(bool valid)
    {
        if (!valid)
            throw new InvalidOperationException("Invalid itinerary projection.");
    }

    private static string Kind(ItineraryFactKind value) =>
        value switch
        {
            ItineraryFactKind.IdentityLocation => "identity-location",
            ItineraryFactKind.Access => "access",
            ItineraryFactKind.Walking => "walking",
            _ => throw new InvalidOperationException(),
        };

    private static string Method(ItineraryFactMethod value) =>
        value switch
        {
            ItineraryFactMethod.SourceRecord => "source-record",
            ItineraryFactMethod.Observed => "observed",
            ItineraryFactMethod.Modelled => "modelled",
            _ => throw new InvalidOperationException(),
        };

    private static string Rights(ItineraryRightsBasis value) =>
        value switch
        {
            ItineraryRightsBasis.Cc0 => "cc0",
            ItineraryRightsBasis.OriginalObservation => "original-observation",
            ItineraryRightsBasis.ExplicitPermission => "explicit-permission",
            ItineraryRightsBasis.Odbl => "odbl",
            _ => throw new InvalidOperationException(),
        };

    private static string Field(ItineraryFactField value) =>
        value switch
        {
            ItineraryFactField.Identity => "identity",
            ItineraryFactField.Coordinates => "coordinates",
            ItineraryFactField.OpeningHours => "opening-hours",
            ItineraryFactField.WalkingDuration => "walking-duration",
            ItineraryFactField.Restrictions => "restrictions",
            _ => throw new InvalidOperationException(),
        };
}
