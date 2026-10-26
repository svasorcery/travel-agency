#:property PublishAot=false
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Xml;

// Offline build tool only. No network, production service registration, or runtime routing.
return MoscowBuilder.Run(args);

static class MoscowBuilder
{
    const long CompressedCap = 200L * 1024 * 1024;
    const long ExpandedCap = 2L * 1024 * 1024 * 1024;
    static bool SaintPetersburg;
    static string SourceUrl =>
        SaintPetersburg
            ? "https://download.bbbike.org/osm/bbbike/SanktPetersburg/SanktPetersburg.osm.gz"
            : "https://download.bbbike.org/osm/bbbike/Moscow/Moscow.osm.gz";
    static string Profile => SaintPetersburg ? "spb-outdoor-profile-v1" : "profile-v1";
    static string Destination => SaintPetersburg ? "saint-petersburg" : "moscow";
    static string PayloadFile => Destination + "-v1.json";
    static string ManifestFile => Destination + "-v1.manifest.json";
    static readonly double[] MoscowBounds = [55.72, 37.57, 55.78, 37.66];
    static readonly double[] PetersburgBounds = [59.923, 30.290, 59.947, 30.346];
    static double[] Bounds => SaintPetersburg ? PetersburgBounds : MoscowBounds;
    static readonly HashSet<long> ExcludedWays = [];
    const string Selector = "pair-selector-v1",
        Model = "model-v1";
    static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = false,
    };
    static readonly Stopwatch Wall = Stopwatch.StartNew();
    static readonly Process ProcessInfo = Process.GetCurrentProcess();
    static long lastMemoryCheck;
    static long wallLimitMilliseconds = 300000;
    static long memoryLimitBytes = 1024L * 1024 * 1024;
    static readonly HashSet<string> ProfileKeys =
    [
        "highway",
        "access",
        "foot",
        "sidewalk",
        "barrier",
        "oneway:foot",
        "crossing",
        "area",
        "construction",
        "proposed",
        "access:conditional",
        "foot:conditional",
        "oneway:foot:conditional",
        "foot:forward",
        "foot:backward",
    ];
    static readonly string[] Tags = ["architecture", "culture", "history", "nature"];
    static readonly HashSet<string> Allowed = ["yes", "designated", "permissive"];
    static readonly string[] Conditional =
    [
        "construction",
        "proposed",
        "access:conditional",
        "foot:conditional",
        "oneway:foot:conditional",
        "foot:forward",
        "foot:backward",
    ];

    public static int Run(string[] args)
    {
        try
        {
            Build(args);
            return 0;
        }
        catch (Exception error)
            when (error
                    is IOException
                        or InvalidDataException
                        or JsonException
                        or XmlException
                        or ArgumentException
                        or OverflowException
                        or InvalidOperationException
                        or UnauthorizedAccessException
            )
        {
            Console.Error.WriteLine($"Build rejected: {error.Message}");
            return 1;
        }
    }

    static void Build(string[] args)
    {
        var names = new[]
        {
            "--osm",
            "--inventory",
            "--input-manifest",
            "--as-of",
            "--dataset-id",
            "--version",
            "--output",
            "--max-wall-ms",
            "--max-memory-mib",
            "--destination",
            "--preparation-profile",
        };
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < args.Length; index += 2)
        {
            Need(
                index + 1 < args.Length
                    && names.Contains(args[index])
                    && options.TryAdd(args[index], args[index + 1]),
                "Unknown/duplicate/incomplete arguments"
            );
        }
        Need(
            names.Take(7).All(options.ContainsKey),
            "Required arguments: " + string.Join(" ", names.Take(7))
        );
        var destination = options.GetValueOrDefault("--destination", "moscow");
        Need(destination is "moscow" or "saint-petersburg", "Unsupported destination");
        SaintPetersburg = destination == "saint-petersburg";
        Need(
            SaintPetersburg == options.ContainsKey("--preparation-profile"),
            "Saint Petersburg requires its preparation profile; Moscow forbids a preparation profile"
        );
        Need(
            options["--dataset-id"] == Destination + "-outdoor",
            "Dataset identity does not match destination"
        );
        if (options.TryGetValue("--max-wall-ms", out var wallOption))
        {
            Need(
                long.TryParse(
                    wallOption,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var requested
                ) && requested is >= 1 and <= 300000,
                "Wall budget limit must be 1..300000 ms"
            );
            wallLimitMilliseconds = requested;
        }
        if (options.TryGetValue("--max-memory-mib", out var memoryOption))
        {
            Need(
                long.TryParse(
                    memoryOption,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var requested
                ) && requested is >= 1 and <= 1024,
                "Memory budget limit must be 1..1024 MiB"
            );
            memoryLimitBytes = requested * 1024 * 1024;
        }
        Deadline(true);
        var inputPath = LocalPath(options["--osm"]);
        var outputPath = LocalPath(options["--output"]);
        Need(
            inputPath.EndsWith(".osm", StringComparison.OrdinalIgnoreCase)
                || inputPath.EndsWith(".osm.gz", StringComparison.OrdinalIgnoreCase),
            "Input must be local .osm or .osm.gz"
        );
        Need(
            !Directory.Exists(outputPath) && !File.Exists(outputPath),
            "Output already exists; immutable releases are never overwritten"
        );
        var inputInfo = new FileInfo(inputPath);
        Need(
            inputInfo.Length > 0 && inputInfo.Length <= CompressedCap,
            "Compressed input size must be 1..200 MiB"
        );
        var metadata = Read<InputManifest>(options["--input-manifest"], 4096);
        var inventory = Read<PoiInput[]>(options["--inventory"], 64 * 1024);
        var asOf = Utc(options["--as-of"]);
        Need(
            Token(options["--dataset-id"], 64)
                && Regex.IsMatch(options["--version"], "^[a-z0-9][a-z0-9.-]{0,31}$"),
            "Invalid dataset identity/version"
        );
        var published = Utc(metadata.SnapshotPublishedAtUtc);
        var downloaded = Utc(metadata.DownloadedAtUtc);
        Need(
            metadata.SourceUrl == SourceUrl && Hex(metadata.Sha256),
            "Unrecognized source/hash metadata"
        );
        Need(
            published <= downloaded && downloaded <= asOf,
            "Publication/download/build timestamp order invalid"
        );
        Need(asOf <= DateTimeOffset.UtcNow, "Future publication/download/build timestamp invalid");
        Need(
            published.AddDays(30) > asOf && published.AddDays(30) > DateTimeOffset.UtcNow,
            "Source snapshot expired; rebuild cannot renew stale source"
        );
        Need(metadata.Bytes == inputInfo.Length, "Input size binding mismatch");
        PreparationProfile? preparation = null;
        if (SaintPetersburg)
        {
            preparation = Read<PreparationProfile>(options["--preparation-profile"], 64 * 1024);
            Need(
                preparation.DestinationId == Destination
                    && preparation.ProfileVersion == Profile
                    && preparation.InputSha256 == metadata.Sha256,
                "Preparation profile input/hash or city mismatch"
            );
            Need(
                preparation.Exclusions is { Length: <= 4096 },
                "Preparation profile exclusion cap exceeded"
            );
            var groups = new HashSet<(string, string)>();
            foreach (var excluded in preparation.Exclusions)
            {
                Need(
                    excluded is not null
                        && excluded.WayIds is { Length: > 0 and <= 4096 }
                        && excluded.Reason
                            is "museum-garden"
                                or "courtyard"
                                or "indoor"
                                or "neva-crossing"
                        && excluded.SourceUrl is { Length: <= 256 }
                        && Uri.TryCreate(excluded.SourceUrl, UriKind.Absolute, out var sourceUri)
                        && sourceUri.Scheme == "https"
                        && sourceUri.UserInfo.Length == 0
                        && sourceUri.Fragment.Length == 0,
                    "Invalid or duplicate preparation profile exclusion"
                );
                Need(
                    groups.Add((excluded.Reason, excluded.SourceUrl)),
                    "Duplicate preparation profile exclusion group"
                );
                foreach (var wayId in excluded.WayIds)
                    Need(
                        wayId > 0 && ExcludedWays.Add(wayId) && ExcludedWays.Count <= 4096,
                        "Invalid, duplicate or excessive preparation profile way IDs"
                    );
            }
            preparation = preparation with
            {
                Exclusions = preparation
                    .Exclusions.OrderBy(x => x.Reason, StringComparer.Ordinal)
                    .ThenBy(x => x.SourceUrl, StringComparer.Ordinal)
                    .Select(x => x with { WayIds = x.WayIds.Order().ToArray() })
                    .ToArray(),
            };
        }
        using (var bytes = File.OpenRead(inputPath))
            Need(Hash(bytes) == metadata.Sha256, "Source SHA256 hash binding mismatch");
        ValidateInventory(inventory, asOf);
        var graph = ReadGraph(inputPath);
        var ordered = inventory.OrderBy(poi => poi.Id, StringComparer.Ordinal).ToArray();
        var anchors = new Dictionary<string, long>(StringComparer.Ordinal);
        var anchorDistances = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var poi in ordered)
        {
            var nearest = poi.AnchorNodeId is { } selected
                ? graph.Nodes.Values.Where(node =>
                    node.Id == selected && graph.Adjacency.ContainsKey(selected)
                )
                : graph.Nodes.Values.Where(node => graph.Adjacency.ContainsKey(node.Id));
            var candidate = nearest
                .Select(node =>
                    (node.Id, Distance(poi.Latitude, poi.Longitude, node.Latitude, node.Longitude))
                )
                .Where(item => item.Item2 <= 50)
                .OrderBy(item => item.Item2)
                .ThenBy(item => item.Id)
                .FirstOrDefault();
            Need(candidate.Id > 0, $"No eligible mapped anchor within 50m for {poi.Id}");
            anchors.Add(poi.Id, candidate.Id);
            anchorDistances.Add(poi.Id, candidate.Item2);
        }
        var paths = new List<WalkPath>();
        foreach (var from in ordered)
        {
            Deadline();
            var shortest = Shortest(graph, anchors[from.Id], anchors.Values.ToHashSet());
            var candidates = new List<WalkPath>();
            foreach (var to in ordered.Where(to => to.Id != from.Id))
            {
                if (!shortest.Distances.TryGetValue(anchors[to.Id], out var length))
                    continue;
                var nodeIds = new List<long> { anchors[to.Id] };
                var wayIds = new List<long>();
                var nodeId = anchors[to.Id];
                while (nodeId != anchors[from.Id])
                {
                    var step = shortest.Previous[nodeId];
                    wayIds.Add(step.WayId);
                    nodeIds.Add(step.From);
                    nodeId = step.From;
                }
                nodeIds.Reverse();
                wayIds.Reverse();
                var crossings = nodeIds
                    .Distinct()
                    .Count(id => graph.Nodes[id].Tags.GetValueOrDefault("highway") == "crossing");
                var lower = Math.Max(1, (length + 74999) / 75000);
                var upper = Math.Max(lower, (length + 44999) / 45000 + crossings);
                if (upper > 120)
                    continue;
                candidates.Add(
                    new WalkPath(
                        $"path-{from.Id}-{to.Id}",
                        from.Id,
                        to.Id,
                        nodeIds.ToArray(),
                        wayIds.ToArray(),
                        length,
                        crossings
                    )
                );
            }
            paths.AddRange(
                candidates
                    .OrderBy(path => path.PathLengthMillimetres)
                    .ThenBy(path => path.ToPoiId, StringComparer.Ordinal)
                    .Take(5)
            );
        }
        Need(
            paths.Count >= 60 && paths.Count <= (SaintPetersburg ? 100 : 256),
            $"Selected path coverage requires 60..256; actual {paths.Count}"
        );
        var expiry = ordered
            .Select(poi => Utc(poi.RetrievedAtUtc).AddDays(90))
            .Append(published.AddDays(30))
            .Min();
        var source = new
        {
            sourceUrl = SourceUrl,
            inputSha256 = metadata.Sha256,
            publicationInputSha256 = metadata.Sha256,
            snapshotPublishedAtUtc = published,
            downloadedAtUtc = downloaded,
            buildAsOfUtc = asOf,
            profileVersion = Profile,
            pairSelectorVersion = Selector,
            modelVersion = Model,
        };
        var pois = ordered
            .Select(poi => new
            {
                poi.Id,
                poi.Title,
                tags = poi.Tags.Order(StringComparer.Ordinal).ToArray(),
                poi.DwellMinutes,
                poi.WikidataEntityId,
                wikidataRevision = poi.WikidataRevision.ToString(CultureInfo.InvariantCulture),
                sourceUrl = $"https://www.wikidata.org/w/index.php?title={poi.WikidataEntityId}&oldid={poi.WikidataRevision}",
                retrievedAtUtc = Utc(poi.RetrievedAtUtc),
                processedAtUtc = asOf,
                validUntil = Utc(poi.RetrievedAtUtc).AddDays(90),
                poi.Latitude,
                poi.Longitude,
                anchorNodeId = anchors[poi.Id],
                approximateAnchor = true,
                anchorDistanceMillimetres = (long)
                    Math.Round(anchorDistances[poi.Id] * 1000, MidpointRounding.AwayFromZero),
                access = new { isKnown = false },
            })
            .ToArray();
        var edges = paths
            .Select(path => new
            {
                id = $"walk-{path.FromPoiId}-{path.ToPoiId}",
                path.FromPoiId,
                path.ToPoiId,
                minimumMinutes = (int)Math.Max(1, (path.PathLengthMillimetres + 74999) / 75000),
                maximumMinutes = (int)
                    Math.Max(
                        Math.Max(1, (path.PathLengthMillimetres + 74999) / 75000),
                        (path.PathLengthMillimetres + 44999) / 45000 + path.CrossingCount
                    ),
                path.PathId,
                pathHash = Hash(Encode(path)),
                path.PathLengthMillimetres,
                path.CrossingCount,
                fromAnchorNodeId = anchors[path.FromPoiId],
                toAnchorNodeId = anchors[path.ToPoiId],
                sourceSha256 = metadata.Sha256,
                method = "modelled",
                rightsBasis = "odbl",
                observedAt = (string?)null,
                calibrationStatus = "not-evaluated",
                restrictions = "Оценка по карте; без полевой проверки. Доступ и безопасность не подтверждены.",
            })
            .ToArray();
        var payload = new
        {
            datasetId = options["--dataset-id"],
            version = options["--version"],
            destinationId = Destination,
            timeZone = "Europe/Moscow",
            coverage = SaintPetersburg
                ? "Центр Санкт-Петербурга на левом берегу Невы: только перечисленные наружные места и отобранные связи по снимку OSM."
                : "Центр Москвы: только перечисленные места и отобранные пешеходные связи по снимку OSM.",
            dataMode = "osm-modelled-demo",
            preparedBy = SaintPetersburg
                ? "trips-saint-petersburg-builder-v1"
                : "trips-moscow-builder-v1",
            humanCurator = (string?)null,
            humanReviewer = (string?)null,
            humanQuality = "not-evaluated",
            calibrationStatus = "not-evaluated",
            buildAsOfUtc = asOf,
            releaseValidUntil = expiry,
            source,
            pois,
            edges,
        };
        var evidence = new
        {
            source,
            bbox = Bounds,
            modelAssumptions = "75/45 metres per minute plus one modelled minute per mapped crossing: author scenarios, not observations, confidence or a guaranteed upper bound.",
            pathHashEncoding = "SHA256 of exact UTF8 LF JSON of each path object, property order as here, no BOM",
            paths,
        };
        var attribution =
            "© OpenStreetMap contributors — ODbL 1.0\nhttps://www.openstreetmap.org/copyright\nhttps://opendatacommons.org/licenses/odbl/1-0/\nOSM-derived paths are offered under ODbL. Identity/coordinates: Wikidata CC0, exact revisions in payload. Editorial titles/tags/dwell: original recommendations.\nWalking minutes are uncalibrated modelled scenarios; no field observations or human route approval.\nReproduce: dotnet run --file tools/trips/build-moscow.cs -- --osm LOCAL_OSM --inventory inventory.json --input-manifest input-manifest.json --as-of FROZEN_UTC --dataset-id DATASET --version VERSION --output NEW_DIRECTORY\nOriginal extract download: "
            + SourceUrl
            + "\nSource SHA256: "
            + metadata.Sha256
            + "\nPublication: "
            + published.ToString("O")
            + " (extract publication, not physical survey)\n";
        if (SaintPetersburg)
            attribution = attribution.Replace(
                "-- --osm LOCAL_OSM",
                "-- --destination saint-petersburg --preparation-profile preparation-profile.json --osm LOCAL_OSM",
                StringComparison.Ordinal
            );
        var files = new SortedDictionary<string, byte[]>(StringComparer.Ordinal)
        {
            [PayloadFile] = Encode(payload),
            ["walking-evidence.json"] = Encode(evidence),
            ["ATTRIBUTION.txt"] = Encoding.UTF8.GetBytes(attribution),
            ["inventory.json"] = Encode(ordered),
            ["input-manifest.json"] = Encode(metadata),
        };
        if (preparation is not null)
        {
            files["preparation-profile.json"] = Encode(preparation);
            Need(
                files["preparation-profile.json"].Length <= 64 * 1024,
                "Preparation profile output exceeds 64 KiB"
            );
        }
        Need(files[PayloadFile].Length <= 256 * 1024, "Payload exceeds 256 KiB");
        Need(
            files["walking-evidence.json"].Length <= 4 * 1024 * 1024,
            "Walking evidence exceeds 4 MiB"
        );
        var hashes = files.ToDictionary(
            pair => pair.Key,
            pair => Hash(pair.Value),
            StringComparer.Ordinal
        );
        var manifestBytes = Encode(
            new
            {
                datasetId = options["--dataset-id"],
                version = options["--version"],
                files = hashes,
            }
        );
        Need(manifestBytes.Length <= 2048, "Manifest exceeds 2 KiB");
        Deadline();
        // Build all bounded bytes before publishing an otherwise absent output directory.
        var stagingPath = outputPath + ".partial-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(stagingPath);
        foreach (var pair in files)
            File.WriteAllBytes(Path.Combine(stagingPath, pair.Key), pair.Value);
        File.WriteAllBytes(Path.Combine(stagingPath, ManifestFile), manifestBytes);
        Deadline(true);
        Directory.Move(stagingPath, outputPath);
        Console.WriteLine(
            JsonSerializer.Serialize(
                new
                {
                    status = "built",
                    pois = ordered.Length,
                    paths = paths.Count,
                    rawCoordinateIndexCount = graph.RawCoordinateIndexCount,
                    retainedGraphNodeCount = graph.Nodes.Count,
                    retainedWayCount = graph.WayCount,
                    elapsedMilliseconds = Wall.ElapsedMilliseconds,
                    peakWorkingSetBytes = Process.GetCurrentProcess().PeakWorkingSet64,
                    files = hashes,
                },
                Json
            )
        );
    }

    static void ValidateInventory(PoiInput[] pois, DateTimeOffset asOf)
    {
        Need(pois.Length is >= 18 and <= 48, "Inventory requires 18..48 POIs");
        Need(
            !SaintPetersburg || pois.Length <= 20,
            "Saint Petersburg inventory requires 18..20 POIs"
        );
        Need(pois.All(poi => poi is not null), "Null inventory POI");
        Need(
            pois.Select(poi => poi.Id).Distinct(StringComparer.Ordinal).Count() == pois.Length,
            "Duplicate POI identity"
        );
        foreach (var poi in pois)
        {
            if (poi.Title is null || poi.Tags is null || poi.WikidataEntityId is null)
                throw new InvalidDataException("Missing inventory field");
            Need(
                Token(poi.Id, 48)
                    && poi.Title.Length > 0
                    && poi.Title.EnumerateRunes().Count() <= 100
                    && !poi.Title.Any(char.IsControl),
                "Invalid POI identity/title"
            );
            Need(
                poi.Tags.Length is >= 1 and <= 4
                    && poi.Tags.Distinct().Count() == poi.Tags.Length
                    && poi.Tags.All(Tags.Contains),
                "Invalid editorial tags"
            );
            Need(
                poi.DwellMinutes is >= 15 and <= 120
                    && Regex.IsMatch(poi.WikidataEntityId, "^Q[1-9][0-9]*$")
                    && poi.WikidataRevision > 0,
                "Invalid Wikidata revision/dwell"
            );
            Need(
                Inside(poi.Latitude, poi.Longitude)
                    && (poi.AnchorNodeId is null || poi.AnchorNodeId > 0),
                "POI coordinate/anchor outside supported bbox"
            );
            var retrieved = Utc(poi.RetrievedAtUtc);
            Need(
                retrieved <= asOf && retrieved.AddDays(90) > asOf,
                "Identity retrieval expired or future"
            );
        }
        Need(
            Tags.All(tag => pois.Count(poi => poi.Tags.Contains(tag)) >= 3),
            "Coverage requires at least three POIs per supported tag"
        );
    }

    static Graph ReadGraph(string path)
    {
        var nodes = new Dictionary<long, MapNode>();
        var adjacency = new Dictionary<long, List<Segment>>();
        var seenWays = new HashSet<long>();
        using var file = File.OpenRead(path);
        using Stream expanded = path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)
            ? new GZipStream(file, CompressionMode.Decompress)
            : file;
        using var limited = new LimitedStream(expanded, ExpandedCap);
        using var reader = XmlReader.Create(
            limited,
            new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                IgnoreComments = true,
                IgnoreWhitespace = true,
                MaxCharactersInDocument = ExpandedCap,
            }
        );
        var root = false;
        var ways = false;
        while (reader.Read())
        {
            Deadline();
            if (reader.NodeType != XmlNodeType.Element)
                continue;
            if (reader.Depth == 0)
            {
                Need(reader.Name == "osm", "Invalid OSM XML root");
                root = true;
                continue;
            }
            if (reader.Depth != 1)
                throw new InvalidDataException("Unexpected nested OSM XML");
            if (reader.Name == "node")
            {
                Need(!ways, "OSM nodes must precede ways");
                var id = PositiveLong(reader.GetAttribute("id"));
                var lat = Coordinate(reader.GetAttribute("lat"));
                var lon = Coordinate(reader.GetAttribute("lon"));
                var inside = Inside(lat, lon);
                var tags = ReadTags(reader, out _, inside);
                if (!inside)
                    continue;
                Need(nodes.TryAdd(id, new MapNode(id, lat, lon, tags)), "Duplicate OSM node");
                Need(nodes.Count <= 750000, "Temporary raw-coordinate index exceeds 750000 cap");
            }
            else if (reader.Name == "way")
            {
                ways = true;
                var id = PositiveLong(reader.GetAttribute("id"));
                var tags = ReadTags(reader, out var references);
                if (
                    !WayAllowed(tags, out var forward, out var reverse) || ExcludedWays.Contains(id)
                )
                    continue;
                var segments = new List<(long From, long To, long Millimetres)>();
                for (var index = 1; index < references.Count; index++)
                {
                    if (
                        !nodes.TryGetValue(references[index - 1], out var from)
                        || !nodes.TryGetValue(references[index], out var to)
                        || !NodeAllowed(from.Tags)
                        || !NodeAllowed(to.Tags)
                        || from.Id == to.Id
                    )
                        continue;
                    var mm = Math.Max(
                        1L,
                        checked(
                            (long)
                                Math.Round(
                                    Distance(
                                        from.Latitude,
                                        from.Longitude,
                                        to.Latitude,
                                        to.Longitude
                                    ) * 1000,
                                    MidpointRounding.AwayFromZero
                                )
                        )
                    );
                    segments.Add((from.Id, to.Id, mm));
                }
                if (segments.Count == 0)
                    continue;
                Need(seenWays.Add(id), "Duplicate retained OSM way");
                Need(seenWays.Count <= 50000, "Retained ways exceed 50000 cap");
                foreach (var segment in segments)
                {
                    // Keep empty adjacency for valid one-way terminals so they remain eligible anchors.
                    adjacency.TryAdd(segment.From, []);
                    adjacency.TryAdd(segment.To, []);
                    Need(
                        adjacency.Count <= 100000,
                        "Retained eligible graph nodes exceed 100000 cap"
                    );
                    if (forward)
                        adjacency[segment.From].Add(new(segment.To, id, segment.Millimetres));
                    if (reverse)
                        adjacency[segment.To].Add(new(segment.From, id, segment.Millimetres));
                }
            }
            else if (reader.Name is "relation" or "bounds" or "note" or "meta")
            {
                using var ignored = reader.ReadSubtree();
                while (ignored.Read()) { }
            }
            else
                throw new InvalidDataException("Unsupported OSM XML element");
        }
        Need(root && nodes.Count > 0, "Empty/corrupt OSM XML");
        var rawCoordinateIndexCount = nodes.Count;
        foreach (var id in nodes.Keys.Where(id => !adjacency.ContainsKey(id)).ToArray())
            nodes.Remove(id);
        foreach (var pair in adjacency)
            pair.Value.Sort(
                (left, right) =>
                    left.WayId != right.WayId ? left.WayId.CompareTo(right.WayId)
                    : left.To != right.To ? left.To.CompareTo(right.To)
                    : left.Millimetres.CompareTo(right.Millimetres)
            );
        return new Graph(nodes, adjacency, seenWays.Count, rawCoordinateIndexCount);
    }

    static Dictionary<string, string> ReadTags(
        XmlReader outer,
        out List<long> references,
        bool retain = true
    )
    {
        var tags = new Dictionary<string, string>(StringComparer.Ordinal);
        references = [];
        using var child = outer.ReadSubtree();
        while (child.Read())
        {
            if (child.NodeType != XmlNodeType.Element || child.Depth == 0)
                continue;
            Need(child.Depth == 1, "Invalid nested OSM XML");
            if (child.Name == "tag")
            {
                var key = child.GetAttribute("k");
                var value = child.GetAttribute("v");
                Need(key is { Length: > 0 } && value is not null, "Invalid OSM tag");
                if (!retain || !ProfileKeys.Contains(key!))
                    continue;
                Need(value is { Length: <= 8192 }, "Oversized profile tag");
                Need(tags.TryAdd(key!, value!), "Duplicate OSM tag");
                Need(tags.Count <= 256, "Too many OSM tags");
            }
            else if (child.Name == "nd")
            {
                references.Add(PositiveLong(child.GetAttribute("ref")));
                Need(references.Count <= 65536, "Oversized OSM way");
            }
            else
                throw new InvalidDataException("Unsupported OSM node/way element");
        }
        return tags;
    }

    static bool BasicAllowed(Dictionary<string, string> tags)
    {
        if (Conditional.Any(tags.ContainsKey) || tags.GetValueOrDefault("area") == "yes")
            return false;
        return new[] { "access", "foot" }.All(key =>
            !tags.TryGetValue(key, out var value) || Allowed.Contains(value)
        );
    }

    static bool NodeAllowed(Dictionary<string, string> tags)
    {
        if (!BasicAllowed(tags) || tags.GetValueOrDefault("crossing") == "no")
            return false;
        if (!tags.TryGetValue("barrier", out var barrier) || barrier == "bollard")
            return true;
        return new[] { "gate", "lift_gate", "kissing_gate", "cycle_barrier", "stile" }.Contains(
                barrier
            ) && Allowed.Contains(tags.GetValueOrDefault("foot") ?? "");
    }

    static bool WayAllowed(Dictionary<string, string> tags, out bool forward, out bool reverse)
    {
        forward = reverse = false;
        if (!BasicAllowed(tags))
            return false;
        var highway = tags.GetValueOrDefault("highway");
        if (!new[] { "footway", "pedestrian", "path", "steps" }.Contains(highway))
        {
            if (
                !new[]
                {
                    "residential",
                    "living_street",
                    "service",
                    "tertiary",
                    "unclassified",
                    "cycleway",
                    "track",
                }.Contains(highway)
            )
                return false;
            if (
                !Allowed.Contains(tags.GetValueOrDefault("foot") ?? "")
                && !new[] { "yes", "both", "left", "right" }.Contains(
                    tags.GetValueOrDefault("sidewalk")
                )
            )
                return false;
        }
        switch (tags.GetValueOrDefault("oneway:foot"))
        {
            case null:
            case "no":
            case "0":
                forward = reverse = true;
                return true;
            case "yes":
            case "1":
            case "true":
                forward = true;
                return true;
            case "-1":
                reverse = true;
                return true;
            default:
                return false;
        }
    }

    static (Dictionary<long, long> Distances, Dictionary<long, Step> Previous) Shortest(
        Graph graph,
        long start,
        HashSet<long> targets
    )
    {
        var distances = new Dictionary<long, long> { [start] = 0 };
        var previous = new Dictionary<long, Step>();
        var queue = new PriorityQueue<long, (long Distance, long Node)>();
        queue.Enqueue(start, (0, start));
        while (queue.TryDequeue(out var node, out var priority))
        {
            Deadline();
            if (distances[node] != priority.Distance)
                continue;
            targets.Remove(node);
            if (targets.Count == 0)
                break;
            foreach (var edge in graph.Adjacency.GetValueOrDefault(node) ?? [])
            {
                var length = checked(priority.Distance + edge.Millimetres);
                if (
                    length > 5400000
                    || distances.TryGetValue(edge.To, out var known) && known <= length
                )
                    continue;
                distances[edge.To] = length;
                previous[edge.To] = new(node, edge.WayId);
                queue.Enqueue(edge.To, (length, edge.To));
            }
        }
        return (distances, previous);
    }

    static T Read<T>(string path, int cap)
    {
        var local = LocalPath(path);
        Need(new FileInfo(local).Length <= cap, "JSON input exceeds bound");
        var bytes = File.ReadAllBytes(local);
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 12 });
        Unique(document.RootElement);
        return JsonSerializer.Deserialize<T>(bytes, Json)
            ?? throw new InvalidDataException("Missing JSON input");
    }

    static void Unique(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                Need(names.Add(property.Name), "Duplicate JSON property");
                Unique(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray())
                Unique(item);
    }

    static string LocalPath(string path)
    {
        var full = Path.GetFullPath(path);
        Need(
            !full.StartsWith("\\\\", StringComparison.Ordinal),
            "Only local file inputs/outputs supported"
        );
        return full;
    }

    static bool Token(string? value, int maximum) =>
        value is not null
        && value.Length <= maximum
        && Regex.IsMatch(value, "^[a-z0-9]+(?:-[a-z0-9]+)*$");

    static bool Hex(string value) => Regex.IsMatch(value, "^[a-f0-9]{64}$");

    static DateTimeOffset Utc(string? value)
    {
        Need(
            DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var result
            )
                && result.Offset == TimeSpan.Zero,
            "Invalid UTC publication/timestamp"
        );
        return result;
    }

    static double Coordinate(string? value)
    {
        Need(
            double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result)
                && double.IsFinite(result)
                && result is >= -180 and <= 180,
            "Invalid OSM coordinate"
        );
        return result;
    }

    static long PositiveLong(string? value)
    {
        Need(
            long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var result)
                && result > 0,
            "Invalid OSM ID"
        );
        return result;
    }

    static bool Inside(double lat, double lon) =>
        double.IsFinite(lat)
        && double.IsFinite(lon)
        && lat >= Bounds[0]
        && lat <= Bounds[2]
        && lon >= Bounds[1]
        && lon <= Bounds[3];

    static double Distance(double latA, double lonA, double latB, double lonB)
    {
        const double radians = Math.PI / 180;
        var h =
            Math.Pow(Math.Sin((latB - latA) * radians / 2), 2)
            + Math.Cos(latA * radians)
                * Math.Cos(latB * radians)
                * Math.Pow(Math.Sin((lonB - lonA) * radians / 2), 2);
        return 2 * 6371008.8 * Math.Asin(Math.Sqrt(Math.Clamp(h, 0, 1)));
    }

    static byte[] Encode<T>(T value) =>
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, Json) + "\n");

    static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    static string Hash(Stream bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    static void Need([DoesNotReturnIf(false)] bool condition, string message)
    {
        if (!condition)
            throw new InvalidDataException(message);
    }

    static void Deadline(bool forceMemoryCheck = false)
    {
        var milliseconds = Wall.ElapsedMilliseconds;
        Need(
            milliseconds < wallLimitMilliseconds,
            "Offline build exceeded wall time budget (maximum five minutes)"
        );
        if (!forceMemoryCheck && milliseconds < lastMemoryCheck + 250)
            return;
        lastMemoryCheck = milliseconds;
        ProcessInfo.Refresh();
        Need(
            ProcessInfo.WorkingSet64 <= memoryLimitBytes
                && ProcessInfo.PeakWorkingSet64 <= memoryLimitBytes,
            "Offline process exceeded memory working-set budget (maximum 1 GiB, sampled guard)"
        );
    }

    sealed record InputManifest(
        string SourceUrl,
        string Sha256,
        long Bytes,
        string SnapshotPublishedAtUtc,
        string DownloadedAtUtc
    );

    sealed record PreparationProfile(
        string DestinationId,
        string ProfileVersion,
        string InputSha256,
        ExclusionGroup[] Exclusions
    );

    sealed record ExclusionGroup(string Reason, string SourceUrl, long[] WayIds);

    sealed record PoiInput(
        string Id,
        string Title,
        string[] Tags,
        int DwellMinutes,
        string WikidataEntityId,
        long WikidataRevision,
        string RetrievedAtUtc,
        double Latitude,
        double Longitude,
        long? AnchorNodeId
    );

    sealed record MapNode(
        long Id,
        double Latitude,
        double Longitude,
        Dictionary<string, string> Tags
    );

    sealed record Segment(long To, long WayId, long Millimetres);

    sealed record Step(long From, long WayId);

    sealed record Graph(
        Dictionary<long, MapNode> Nodes,
        Dictionary<long, List<Segment>> Adjacency,
        int WayCount,
        int RawCoordinateIndexCount
    );

    sealed record WalkPath(
        string PathId,
        string FromPoiId,
        string ToPoiId,
        long[] NodeIds,
        long[] WayIds,
        long PathLengthMillimetres,
        int CrossingCount
    );

    sealed class LimitedStream(Stream inner, long limit) : Stream
    {
        long consumed;

        public override int Read(byte[] buffer, int offset, int count)
        {
            Deadline();
            var read = inner.Read(buffer, offset, count);
            consumed = checked(consumed + read);
            Need(consumed <= limit, "Expanded input exceeds 2 GiB cap");
            return read;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => consumed;
            set => throw new NotSupportedException();
        }

        public override void Flush() => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }
}
