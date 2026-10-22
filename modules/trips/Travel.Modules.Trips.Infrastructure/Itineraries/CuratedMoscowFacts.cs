using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ErrorOr;
using Travel.Modules.Trips.Application.Itineraries;
using Travel.Modules.Trips.Core;
using Travel.Modules.Trips.Core.Itineraries;
using Travel.Shared.Abstractions;

namespace Travel.Modules.Trips.Infrastructure.Itineraries;

// A bounded, read-only demo package reader. No routing, refreshing or source API access.
[TestOnly]
public sealed class CuratedMoscowFacts(
    string packageDirectory,
    TimeProvider timeProvider,
    Func<bool>? isWithdrawn = null,
    CuratedMoscowRelease? expectedRelease = null
) : IItineraryFactSource
{
    private readonly CuratedMoscowRelease _release =
        expectedRelease ?? CuratedMoscowRelease.ReviewedV1;
    private const string SourceUrl = "https://download.bbbike.org/osm/bbbike/Moscow/Moscow.osm.gz";
    private const string Assumptions =
        "75/45 metres per minute plus one modelled minute per mapped crossing: author scenarios, not observations, confidence or a guaranteed upper bound.";
    private const string HashEncoding =
        "SHA256 of exact UTF8 LF JSON of each path object, property order as here, no BOM";
    private static readonly string[] FileNames =
    [
        "ATTRIBUTION.txt",
        "input-manifest.json",
        "inventory.json",
        "moscow-v1.json",
        "walking-evidence.json",
    ];

    // Small reproduction records are independently capped, even if the manifest hash is valid.
    private static readonly int[] FileCaps =
    [
        8 * 1024,
        4 * 1024,
        128 * 1024,
        256 * 1024,
        4 * 1024 * 1024,
    ];
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public async Task<ErrorOr<ValidatedItineraryFacts>> ReadAsync(
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            Require(isWithdrawn?.Invoke() != true);
            Require(
                !new DirectoryInfo(packageDirectory).Attributes.HasFlag(FileAttributes.ReparsePoint)
            );
            var children = new DirectoryInfo(packageDirectory)
                .EnumerateFileSystemInfos()
                .Take(7)
                .ToArray();
            Require(
                children.Length == 6
                    && children.All(child =>
                        (
                            child.Attributes
                            & (FileAttributes.Directory | FileAttributes.ReparsePoint)
                        ) == 0
                        && (
                            child.Name == "moscow-v1.manifest.json"
                            || FileNames.Contains(child.Name, StringComparer.Ordinal)
                        )
                    )
            );
            var manifestBytes = await ReadFile("moscow-v1.manifest.json", 2048, cancellationToken);
            Require(Digest(manifestBytes) == _release.ManifestSha256);
            using var manifest = Parse(manifestBytes);
            var header = Shape(manifest.RootElement, "datasetId", "version", "files");
            Require(
                Text(header, "datasetId") == _release.DatasetId
                    && Text(header, "version") == _release.Version
            );
            var hashes = Shape(header.GetProperty("files"), FileNames);
            var bytes = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            for (var index = 0; index < FileNames.Length; index++)
            {
                var name = FileNames[index];
                var expected = Text(hashes, name);
                Require(
                    expected.Length == 64
                        && expected.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f')
                );
                var content = await ReadFile(name, FileCaps[index], cancellationToken);
                Require(Digest(content) == expected);
                bytes.Add(name, content);
            }
            // All offered byte identities have been verified before interpreting any source facts.
            using var payload = Parse(bytes["moscow-v1.json"]);
            using var paths = Parse(bytes["walking-evidence.json"]);
            using var inventory = Parse(bytes["inventory.json"]);
            using var input = Parse(bytes["input-manifest.json"]);
            var facts = Map(
                payload.RootElement,
                paths.RootElement,
                inventory.RootElement,
                input.RootElement,
                StrictUtf8.GetString(bytes["ATTRIBUTION.txt"]),
                Digest(bytes["moscow-v1.json"]),
                _release,
                cancellationToken
            );
            cancellationToken.ThrowIfCancellationRequested();
            var now = timeProvider.GetUtcNow();
            Require(
                isWithdrawn?.Invoke() != true
                    && now >= facts.BuildAsOfUtc
                    && now < facts.ReleaseValidUntil
            );
            Require(
                facts
                    .Evidence.Where(e => e.Kind == ItineraryFactKind.IdentityLocation)
                    .All(e => e.IsKnownAt(now))
            );
            return facts;
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or UnauthorizedAccessException
                        or JsonException
                        or DecoderFallbackException
                        or FormatException
                        or InvalidDataException
                        or ArgumentException
                        or InvalidOperationException
                        or OverflowException
            )
        {
            return Error.Failure("Trips.ItineraryUnavailable", "Itinerary facts are unavailable.");
        }
    }

    private async Task<byte[]> ReadFile(string name, int cap, CancellationToken cancellationToken)
    {
        // Names are constants, never paths or URLs from a package. Reparse-point children are not public artifacts.
        var path = Path.Combine(packageDirectory, name);
        Require(!File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint));
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            8192,
            FileOptions.Asynchronous | FileOptions.SequentialScan
        );
        Require(stream.Length <= cap);
        using var result = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var count = await stream.ReadAsync(buffer, cancellationToken);
            if (count == 0)
                break;
            Require(result.Length + count <= cap);
            result.Write(buffer, 0, count);
        }
        var bytes = result.ToArray();
        Require(
            bytes.Length > 0
                && !bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf })
                && !bytes.Contains((byte)'\r')
        );
        _ = StrictUtf8.GetString(bytes);
        return bytes;
    }

    private static JsonDocument Parse(byte[] bytes)
    {
        var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 });
        try
        {
            RejectDuplicates(document.RootElement);
            return document;
        }
        catch
        {
            document.Dispose();
            throw;
        }
    }

    private static void RejectDuplicates(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                Require(names.Add(property.Name));
                RejectDuplicates(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var child in element.EnumerateArray())
                RejectDuplicates(child);
    }

    private static ValidatedItineraryFacts Map(
        JsonElement payload,
        JsonElement walking,
        JsonElement inventory,
        JsonElement input,
        string attribution,
        string hash,
        CuratedMoscowRelease release,
        CancellationToken cancellationToken
    )
    {
        Shape(
            payload,
            "datasetId",
            "version",
            "destinationId",
            "timeZone",
            "coverage",
            "dataMode",
            "preparedBy",
            "humanCurator",
            "humanReviewer",
            "humanQuality",
            "calibrationStatus",
            "buildAsOfUtc",
            "releaseValidUntil",
            "source",
            "pois",
            "edges"
        );
        Require(
            Text(payload, "datasetId") == release.DatasetId
                && Text(payload, "version") == release.Version
                && Text(payload, "destinationId") == "moscow"
                && Text(payload, "timeZone") == "Europe/Moscow"
        );
        Null(payload, "humanCurator");
        Null(payload, "humanReviewer");
        var source = MapSource(payload.GetProperty("source"));
        var preparation = Valid(
            ItineraryDemoPreparation.Create(
                Text(payload, "dataMode"),
                Text(payload, "preparedBy"),
                null,
                null,
                Text(payload, "humanQuality"),
                Text(payload, "calibrationStatus"),
                Time(payload, "buildAsOfUtc")
            )
        );
        Require(preparation.BuildAsOfUtc == source.BuildAsOfUtc);
        Shape(input, "sourceUrl", "sha256", "bytes", "snapshotPublishedAtUtc", "downloadedAtUtc");
        Require(
            Text(input, "sourceUrl") == source.SourceUrl
                && Text(input, "sha256") == source.InputSha256
                && Number(input, "bytes") is > 0 and <= 200 * 1024 * 1024
                && Time(input, "snapshotPublishedAtUtc") == source.SnapshotPublishedAtUtc
                && Time(input, "downloadedAtUtc") == source.DownloadedAtUtc
        );
        ValidateAttribution(attribution, source);
        Shape(walking, "source", "bbox", "modelAssumptions", "pathHashEncoding", "paths");
        var walkingSource = MapSource(walking.GetProperty("source"));
        Require(
            walkingSource.SourceUrl == source.SourceUrl
                && walkingSource.InputSha256 == source.InputSha256
                && walkingSource.PublicationInputSha256 == source.PublicationInputSha256
                && walkingSource.SnapshotPublishedAtUtc == source.SnapshotPublishedAtUtc
                && walkingSource.DownloadedAtUtc == source.DownloadedAtUtc
                && walkingSource.BuildAsOfUtc == source.BuildAsOfUtc
                && walkingSource.ProfileVersion == source.ProfileVersion
                && walkingSource.PairSelectorVersion == source.PairSelectorVersion
                && walkingSource.ModelVersion == source.ModelVersion
        );
        Require(
            Text(walking, "modelAssumptions") == Assumptions
                && Text(walking, "pathHashEncoding") == HashEncoding
        );
        var bbox = Array(walking.GetProperty("bbox"), 4, 4);
        Require(
            bbox.Select(n => n.GetDecimal())
                .SequenceEqual(new decimal[] { 55.72m, 37.57m, 55.78m, 37.66m })
        );
        var pathById = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var path in Array(walking.GetProperty("paths"), 60, 256))
        {
            Shape(
                path,
                "pathId",
                "fromPoiId",
                "toPoiId",
                "nodeIds",
                "wayIds",
                "pathLengthMillimetres",
                "crossingCount"
            );
            Require(
                path.EnumerateObject()
                    .Select(p => p.Name)
                    .SequenceEqual(
                        new[]
                        {
                            "pathId",
                            "fromPoiId",
                            "toPoiId",
                            "nodeIds",
                            "wayIds",
                            "pathLengthMillimetres",
                            "crossingCount",
                        }
                    )
            );
            Require(pathById.TryAdd(Text(path, "pathId"), path));
        }
        var inventoryById = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var poi in Array(inventory, 18, 48))
        {
            Shape(
                poi,
                "id",
                "title",
                "tags",
                "dwellMinutes",
                "wikidataEntityId",
                "wikidataRevision",
                "retrievedAtUtc",
                "latitude",
                "longitude",
                "anchorNodeId"
            );
            Require(inventoryById.TryAdd(Text(poi, "id"), poi));
        }
        var pois = new List<ItineraryPoi>();
        var evidence = new List<ItineraryEvidence>();
        var anchors = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var poi in Array(payload.GetProperty("pois"), 18, 48))
        {
            cancellationToken.ThrowIfCancellationRequested();
            Shape(
                poi,
                "id",
                "title",
                "tags",
                "dwellMinutes",
                "wikidataEntityId",
                "wikidataRevision",
                "sourceUrl",
                "retrievedAtUtc",
                "processedAtUtc",
                "validUntil",
                "latitude",
                "longitude",
                "anchorNodeId",
                "approximateAnchor",
                "anchorDistanceMillimetres",
                "access"
            );
            var id = Text(poi, "id");
            var tags = Strings(poi.GetProperty("tags"), 1, 4);
            var retrieved = Time(poi, "retrievedAtUtc");
            var processed = Time(poi, "processedAtUtc");
            var validUntil = Time(poi, "validUntil");
            var anchor = Number(poi, "anchorNodeId");
            Require(
                anchor > 0
                    && anchors.TryAdd(id, anchor)
                    && poi.GetProperty("approximateAnchor").GetBoolean()
                    && Number(poi, "anchorDistanceMillimetres") is >= 0 and <= 50000
                    && processed == source.BuildAsOfUtc
                    && validUntil == retrieved.AddDays(90)
            );
            var access = Shape(poi.GetProperty("access"), "isKnown");
            Require(!access.GetProperty("isKnown").GetBoolean());
            Require(inventoryById.Remove(id, out var original));
            Require(
                Text(original, "title") == Text(poi, "title")
                    && Text(original, "wikidataEntityId") == Text(poi, "wikidataEntityId")
                    && Number(original, "wikidataRevision").ToString(CultureInfo.InvariantCulture)
                        == Text(poi, "wikidataRevision")
                    && Number(original, "dwellMinutes") == Number(poi, "dwellMinutes")
                    && Time(original, "retrievedAtUtc") == retrieved
                    && original.GetProperty("latitude").GetDecimal()
                        == poi.GetProperty("latitude").GetDecimal()
                    && original.GetProperty("longitude").GetDecimal()
                        == poi.GetProperty("longitude").GetDecimal()
                    && Strings(original.GetProperty("tags"), 1, 4)
                        .Order(StringComparer.Ordinal)
                        .SequenceEqual(tags.Order(StringComparer.Ordinal))
                    && (
                        original.GetProperty("anchorNodeId").ValueKind == JsonValueKind.Null
                        || Number(original, "anchorNodeId") == anchor
                    )
            );
            var latitude = poi.GetProperty("latitude").GetDecimal();
            var longitude = poi.GetProperty("longitude").GetDecimal();
            Require(latitude is >= 55.72m and <= 55.78m && longitude is >= 37.57m and <= 37.66m);
            var identityId = "identity-" + id;
            evidence.Add(
                Valid(
                    ItineraryEvidence.Create(
                        identityId,
                        ItineraryFactKind.IdentityLocation,
                        id,
                        ItineraryFactMethod.SourceRecord,
                        ItineraryRightsBasis.Cc0,
                        "https://www.wikidata.org/wiki/Wikidata:Licensing",
                        Text(poi, "sourceUrl"),
                        retrieved,
                        processed,
                        null,
                        validUntil,
                        [ItineraryFactField.Identity, ItineraryFactField.Coordinates],
                        null
                    )
                )
            );
            pois.Add(
                Valid(
                    ItineraryPoi.Create(
                        id,
                        Text(poi, "title"),
                        tags,
                        checked((int)Number(poi, "dwellMinutes")),
                        Text(poi, "wikidataEntityId"),
                        Text(poi, "wikidataRevision"),
                        identityId,
                        latitude,
                        longitude,
                        Valid(ItineraryAccess.Create(id, false, null, [], []))
                    )
                )
            );
        }
        Require(inventoryById.Count == 0);
        var edges = new List<ItineraryWalkEdge>();
        foreach (var edge in Array(payload.GetProperty("edges"), 60, 256))
        {
            cancellationToken.ThrowIfCancellationRequested();
            Shape(
                edge,
                "id",
                "fromPoiId",
                "toPoiId",
                "minimumMinutes",
                "maximumMinutes",
                "pathId",
                "pathHash",
                "pathLengthMillimetres",
                "crossingCount",
                "fromAnchorNodeId",
                "toAnchorNodeId",
                "sourceSha256",
                "method",
                "rightsBasis",
                "observedAt",
                "calibrationStatus",
                "restrictions"
            );
            Null(edge, "observedAt");
            Require(
                Text(edge, "method") == "modelled"
                    && Text(edge, "rightsBasis") == "odbl"
                    && Text(edge, "calibrationStatus") == "not-evaluated"
                    && Text(edge, "sourceSha256") == source.InputSha256
            );
            var id = Text(edge, "id");
            var from = Text(edge, "fromPoiId");
            var to = Text(edge, "toPoiId");
            Require(
                anchors.TryGetValue(from, out var fromAnchor)
                    && anchors.TryGetValue(to, out var toAnchor)
                    && Number(edge, "fromAnchorNodeId") == fromAnchor
                    && Number(edge, "toAnchorNodeId") == toAnchor
            );
            var model = Valid(
                ItineraryModelledWalk.Create(
                    source,
                    from,
                    to,
                    Text(edge, "pathId"),
                    Text(edge, "pathHash"),
                    Number(edge, "pathLengthMillimetres"),
                    checked((int)Number(edge, "crossingCount")),
                    Number(edge, "fromAnchorNodeId"),
                    Number(edge, "toAnchorNodeId")
                )
            );
            Require(pathById.Remove(model.PathId, out var path));
            ValidatePath(path, model);
            evidence.Add(
                Valid(
                    ItineraryEvidence.Create(
                        id,
                        ItineraryFactKind.Walking,
                        id,
                        ItineraryFactMethod.Modelled,
                        ItineraryRightsBasis.Odbl,
                        "https://opendatacommons.org/licenses/odbl/1-0/",
                        source.SourceUrl,
                        source.DownloadedAtUtc,
                        source.BuildAsOfUtc,
                        null,
                        source.ValidUntil,
                        [ItineraryFactField.WalkingDuration, ItineraryFactField.Restrictions],
                        model
                    )
                )
            );
            edges.Add(
                Valid(
                    ItineraryWalkEdge.Create(
                        id,
                        from,
                        to,
                        checked((int)Number(edge, "minimumMinutes")),
                        checked((int)Number(edge, "maximumMinutes")),
                        id,
                        Text(edge, "restrictions")
                    )
                )
            );
        }
        Require(
            pathById.Count == 0
                && edges.GroupBy(e => e.FromPoiId, StringComparer.Ordinal).All(g => g.Count() <= 5)
        );
        return Valid(
            ValidatedItineraryFacts.Create(
                Text(payload, "datasetId"),
                Text(payload, "version"),
                hash,
                new DestinationSelection("moscow", "Москва", "Europe/Moscow", "1"),
                Text(payload, "coverage"),
                preparation,
                source,
                Time(payload, "releaseValidUntil"),
                pois,
                edges,
                evidence
            )
        );
    }

    private static void ValidatePath(JsonElement path, ItineraryModelledWalk model)
    {
        Require(
            Text(path, "fromPoiId") == model.FromPoiId
                && Text(path, "toPoiId") == model.ToPoiId
                && Number(path, "pathLengthMillimetres") == model.PathLengthMillimetres
                && Number(path, "crossingCount") == model.CrossingCount
                && Digest(StrictUtf8.GetBytes(path.GetRawText() + "\n")) == model.PathHash
        );
        var nodes = Array(path.GetProperty("nodeIds"), 1, 100000)
            .Select(n => n.GetInt64())
            .ToArray();
        var ways = Array(path.GetProperty("wayIds"), 0, 99999).Select(n => n.GetInt64()).ToArray();
        Require(
            nodes.All(n => n > 0)
                && ways.All(n => n > 0)
                && nodes.Distinct().Count() == nodes.Length
                && ways.Length == nodes.Length - 1
                && nodes[0] == model.FromAnchorNodeId
                && nodes[^1] == model.ToAnchorNodeId
                && model.CrossingCount <= nodes.Length
                && (model.PathLengthMillimetres == 0 ? nodes.Length == 1 : nodes.Length > 1)
        );
    }

    private static ItineraryModelSource MapSource(JsonElement source)
    {
        Shape(
            source,
            "sourceUrl",
            "inputSha256",
            "publicationInputSha256",
            "snapshotPublishedAtUtc",
            "downloadedAtUtc",
            "buildAsOfUtc",
            "profileVersion",
            "pairSelectorVersion",
            "modelVersion"
        );
        Require(Text(source, "sourceUrl") == SourceUrl);
        return Valid(
            ItineraryModelSource.Create(
                Text(source, "sourceUrl"),
                Text(source, "inputSha256"),
                Text(source, "publicationInputSha256"),
                Time(source, "snapshotPublishedAtUtc"),
                Time(source, "downloadedAtUtc"),
                Time(source, "buildAsOfUtc"),
                Text(source, "profileVersion"),
                Text(source, "pairSelectorVersion"),
                Text(source, "modelVersion")
            )
        );
    }

    private static void ValidateAttribution(string text, ItineraryModelSource source)
    {
        var expected =
            "© OpenStreetMap contributors — ODbL 1.0\n"
            + "https://www.openstreetmap.org/copyright\n"
            + "https://opendatacommons.org/licenses/odbl/1-0/\n"
            + "OSM-derived paths are offered under ODbL. Identity/coordinates: Wikidata CC0, exact revisions in payload. Editorial titles/tags/dwell: original recommendations.\n"
            + "Walking minutes are uncalibrated modelled scenarios; no field observations or human route approval.\n"
            + "Reproduce: dotnet run --file tools/trips/build-moscow.cs -- --osm LOCAL_OSM --inventory inventory.json --input-manifest input-manifest.json --as-of FROZEN_UTC --dataset-id DATASET --version VERSION --output NEW_DIRECTORY\n"
            + $"Original extract download: {source.SourceUrl}\nSource SHA256: {source.InputSha256}\n"
            + $"Publication: {source.SnapshotPublishedAtUtc.ToString("O", CultureInfo.InvariantCulture)} (extract publication, not physical survey)\n";
        Require(text == expected);
    }

    private static JsonElement Shape(JsonElement element, params string[] names)
    {
        Require(element.ValueKind == JsonValueKind.Object);
        var actual = element.EnumerateObject().Select(p => p.Name).ToArray();
        Require(
            actual.Length == names.Length
                && actual.All(n => names.Contains(n, StringComparer.Ordinal))
        );
        return element;
    }

    private static JsonElement[] Array(JsonElement element, int minimum, int maximum)
    {
        Require(
            element.ValueKind == JsonValueKind.Array
                && element.GetArrayLength() >= minimum
                && element.GetArrayLength() <= maximum
        );
        return element.EnumerateArray().ToArray();
    }

    private static string[] Strings(JsonElement element, int minimum, int maximum) =>
        Array(element, minimum, maximum)
            .Select(e =>
            {
                Require(e.ValueKind == JsonValueKind.String);
                return e.GetString()!;
            })
            .ToArray();

    private static string Text(JsonElement element, string name)
    {
        var value = element.GetProperty(name);
        Require(value.ValueKind == JsonValueKind.String);
        return value.GetString()!;
    }

    private static long Number(JsonElement element, string name) =>
        element.GetProperty(name).GetInt64();

    private static DateTimeOffset Time(JsonElement element, string name)
    {
        var text = Text(element, name);
        Require(text.EndsWith('Z') || text.EndsWith("+00:00", StringComparison.Ordinal));
        var value = element.GetProperty(name).GetDateTimeOffset();
        Require(value != default && value.Offset == TimeSpan.Zero);
        return value;
    }

    private static void Null(JsonElement element, string name) =>
        Require(element.GetProperty(name).ValueKind == JsonValueKind.Null);

    private static string Digest(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static T Valid<T>(ErrorOr<T> result)
    {
        Require(!result.IsError);
        return result.Value;
    }

    private static void Require(bool condition)
    {
        if (!condition)
            throw new InvalidDataException();
    }
}
