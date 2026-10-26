using System.Security.Cryptography;
using System.Text;
using ErrorOr;
using Travel.Modules.Trips.Api.Contracts;
using Travel.Modules.Trips.Core;
using Travel.Modules.Trips.Core.Itineraries;

namespace Travel.Modules.Trips.Tests.Integration;

// FICTIONAL structural fixtures: no actual survey, OSM path, rights review or human acceptance.
internal static class FictionalItineraryFacts
{
    internal static readonly DateTimeOffset Now = new(2026, 10, 21, 0, 0, 0, TimeSpan.Zero);
    internal static readonly DestinationSelection Destination = CityCatalogue.Find("moscow").Value;
    internal static readonly string[] Tags = ["architecture", "culture", "history", "nature"];
    internal static readonly string InputHash = new('b', 64);

    internal static ModelSourceInput SourceInput() =>
        new(
            "https://fictional.example.test/osm.gz",
            InputHash,
            InputHash,
            Now.AddDays(-3),
            Now.AddDays(-2),
            Now,
            "profile-v1",
            "pair-selector-v1",
            "model-v1"
        );

    internal static ItineraryModelSource Source() => SourceInput().Build().Value;

    internal static WalkInput PathInput(
        string pathId = "path-e0",
        string from = "p0",
        string to = "p1",
        ItineraryModelSource? source = null
    ) =>
        new(
            source ?? Source(),
            from,
            to,
            pathId,
            Hash("FICTIONAL " + pathId),
            900_000,
            2,
            Anchor(from),
            Anchor(to)
        );

    internal static long Anchor(string poi) => long.Parse(poi[1..]) + 1;

    internal static string Hash(string text) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    internal static EvidenceInput EvidenceParameters(
        string id,
        ItineraryFactKind kind,
        string scope,
        ItineraryModelledWalk? model = null
    )
    {
        var source = model?.Source ?? Source();
        return new(
            id,
            kind,
            scope,
            kind == ItineraryFactKind.Walking
                ? ItineraryFactMethod.Modelled
                : ItineraryFactMethod.SourceRecord,
            kind == ItineraryFactKind.IdentityLocation ? ItineraryRightsBasis.Cc0
                : kind == ItineraryFactKind.Access ? ItineraryRightsBasis.ExplicitPermission
                : ItineraryRightsBasis.Odbl,
            "FICTIONAL reference; no actual observation or acceptance",
            kind == ItineraryFactKind.IdentityLocation
                    ? "https://www.wikidata.org/w/index.php?title=Q1&oldid=1"
                : kind == ItineraryFactKind.Access ? "https://fictional.example.test/access"
                : source.SourceUrl,
            source.DownloadedAtUtc,
            source.BuildAsOfUtc,
            null,
            kind == ItineraryFactKind.IdentityLocation ? source.DownloadedAtUtc.AddDays(90)
                : kind == ItineraryFactKind.Access ? source.DownloadedAtUtc.AddDays(7)
                : source.ValidUntil,
            kind == ItineraryFactKind.IdentityLocation
                    ? [ItineraryFactField.Identity, ItineraryFactField.Coordinates]
                : kind == ItineraryFactKind.Access ? [ItineraryFactField.OpeningHours]
                : [ItineraryFactField.WalkingDuration, ItineraryFactField.Restrictions],
            model
        );
    }

    internal static ItineraryPoi Poi(
        string id,
        string? identity = null,
        string[]? tags = null,
        ItineraryAccess? access = null,
        decimal latitude = 55.75m,
        decimal longitude = 37.61m
    ) =>
        ItineraryPoi
            .Create(
                id,
                "FICTIONAL place " + id,
                tags ?? [Tags[int.Parse(id[1..]) % 4]],
                30,
                "Q1",
                "1",
                identity ?? "identity-" + id,
                latitude,
                longitude,
                access ?? ItineraryAccess.Create(id, false, null, [], []).Value
            )
            .Value;

    internal static FactsInput Input(string destinationId = "moscow", bool v2 = false)
    {
        // v2 cases reuse known public wire identities but all records remain FICTIONAL.
        var petersburg = destinationId == "saint-petersburg";
        var sourceInput = SourceInput() with
        {
            ProfileVersion = petersburg ? "spb-outdoor-profile-v1" : "profile-v1",
        };
        if (v2)
            sourceInput = sourceInput with
            {
                SourceUrl = petersburg
                    ? "https://download.bbbike.org/osm/bbbike/SanktPetersburg/SanktPetersburg.osm.gz"
                    : "https://download.bbbike.org/osm/bbbike/Moscow/Moscow.osm.gz",
            };
        var source = sourceInput.Build().Value;
        var pois = Enumerable
            .Range(0, 18)
            .Select(i =>
                Poi(
                    "p" + i,
                    latitude: petersburg ? 59.935m : 55.75m,
                    longitude: petersburg ? 30.32m : 37.61m
                )
            )
            .ToArray();
        var paths = Enumerable
            .Range(0, 60)
            .Select(i =>
                PathInput("path-e" + i, "p" + (i % 18), "p" + ((i % 18 + i / 18 + 1) % 18), source)
                    .Build()
                    .Value
            )
            .ToArray();
        var edges = paths
            .Select(
                (p, i) =>
                    ItineraryWalkEdge
                        .Create(
                            "e" + i,
                            p.FromPoiId,
                            p.ToPoiId,
                            p.MinimumMinutes,
                            p.MaximumMinutes,
                            "walk-e" + i,
                            "FICTIONAL mapped eligibility; no field verification"
                        )
                        .Value
            )
            .ToArray();
        var evidence = pois.Select(p =>
                EvidenceParameters(p.IdentityEvidenceId, ItineraryFactKind.IdentityLocation, p.Id)
                    .Build()
                    .Value
            )
            .Concat(
                paths.Select(
                    (p, i) =>
                        EvidenceParameters("walk-e" + i, ItineraryFactKind.Walking, "e" + i, p)
                            .Build()
                            .Value
                )
            )
            .ToArray();
        return new(
            v2 ? destinationId + "-outdoor" : "fictional-" + destinationId,
            v2 ? "1" : "fictional-v2",
            v2
                ? (
                    petersburg
                        ? TripsItineraryProtocol.SaintPetersburgHash
                        : TripsItineraryProtocol.MoscowHash
                )
                : new string('a', 64),
            CityCatalogue.Find(destinationId).Value,
            "FICTIONAL structural dataset; no production data",
            Preparation(),
            source,
            Now.AddDays(20),
            pois,
            edges,
            evidence
        );
    }

    internal static ItineraryDemoPreparation Preparation() =>
        ItineraryDemoPreparation
            .Create(
                "osm-modelled-demo",
                "fictional-builder-v1",
                null,
                null,
                "not-evaluated",
                "not-evaluated",
                Now
            )
            .Value;

    internal static ValidatedItineraryFacts Create()
    {
        var result = Input().Build();
        if (result.IsError)
            throw new InvalidOperationException("Invalid fictional facts.");
        return result.Value;
    }

    internal sealed record ModelSourceInput(
        string SourceUrl,
        string InputSha256,
        string PublicationInputSha256,
        DateTimeOffset SnapshotPublishedAtUtc,
        DateTimeOffset DownloadedAtUtc,
        DateTimeOffset BuildAsOfUtc,
        string ProfileVersion,
        string PairSelectorVersion,
        string ModelVersion
    )
    {
        internal ErrorOr<ItineraryModelSource> Build() =>
            ItineraryModelSource.Create(
                SourceUrl,
                InputSha256,
                PublicationInputSha256,
                SnapshotPublishedAtUtc,
                DownloadedAtUtc,
                BuildAsOfUtc,
                ProfileVersion,
                PairSelectorVersion,
                ModelVersion
            );
    }

    internal sealed record WalkInput(
        ItineraryModelSource Source,
        string FromPoiId,
        string ToPoiId,
        string PathId,
        string PathHash,
        long PathLengthMillimetres,
        int CrossingCount,
        long FromAnchorNodeId,
        long ToAnchorNodeId
    )
    {
        internal ErrorOr<ItineraryModelledWalk> Build() =>
            ItineraryModelledWalk.Create(
                Source,
                FromPoiId,
                ToPoiId,
                PathId,
                PathHash,
                PathLengthMillimetres,
                CrossingCount,
                FromAnchorNodeId,
                ToAnchorNodeId
            );
    }

    internal sealed record EvidenceInput(
        string Id,
        ItineraryFactKind Kind,
        string ScopeId,
        ItineraryFactMethod Method,
        ItineraryRightsBasis RightsBasis,
        string RightsEvidence,
        string SourceUrl,
        DateTimeOffset RetrievedAtUtc,
        DateTimeOffset ProcessedAtUtc,
        DateTimeOffset? ObservedAt,
        DateTimeOffset ValidUntil,
        ItineraryFactField[] PermittedFields,
        ItineraryModelledWalk? ModelledWalk
    )
    {
        internal ErrorOr<ItineraryEvidence> Build() =>
            ItineraryEvidence.Create(
                Id,
                Kind,
                ScopeId,
                Method,
                RightsBasis,
                RightsEvidence,
                SourceUrl,
                RetrievedAtUtc,
                ProcessedAtUtc,
                ObservedAt,
                ValidUntil,
                PermittedFields,
                ModelledWalk
            );
    }

    internal sealed record FactsInput(
        string DatasetId,
        string Version,
        string DatasetHash,
        DestinationSelection Destination,
        string Coverage,
        ItineraryDemoPreparation Preparation,
        ItineraryModelSource ModelSource,
        DateTimeOffset ReleaseValidUntil,
        ItineraryPoi[] Pois,
        ItineraryWalkEdge[] Edges,
        ItineraryEvidence[] Evidence
    )
    {
        internal ErrorOr<ValidatedItineraryFacts> Build() =>
            ValidatedItineraryFacts.Create(
                DatasetId,
                Version,
                DatasetHash,
                Destination,
                Coverage,
                Preparation,
                ModelSource,
                ReleaseValidUntil,
                Pois,
                Edges,
                Evidence
            );
    }
}
