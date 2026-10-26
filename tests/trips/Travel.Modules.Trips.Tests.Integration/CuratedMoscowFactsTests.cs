using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Shouldly;
using Travel.Modules.Trips.Core.Itineraries;
using Travel.Modules.Trips.Infrastructure.Itineraries;
using Xunit;

namespace Travel.Modules.Trips.Tests.Integration;

public sealed class CuratedMoscowFactsTests
{
    private static readonly DateTimeOffset ReadyAt = DateTimeOffset.Parse("2026-10-21T09:00:00Z");

    [Fact]
    public async Task Reviewed_bytes_map_to_twenty_real_pois_ninety_modelled_edges_and_unknown_hours()
    {
        using var fixture = new Package();
        var result = await fixture.Source.ReadAsync(TestContext.Current.CancellationToken);
        result.IsError.ShouldBeFalse();
        var facts = result.Value;
        facts.DatasetHash.ShouldBe(
            "631fd5cd9650e3228af87ef8a31219d7ea59a2e4af42bc4328453f32268ffb08"
        );
        facts.Pois.Count.ShouldBe(20);
        facts.Edges.Count.ShouldBe(90);
        facts.Evidence.Count.ShouldBe(110);
        facts.Destination.CatalogueVersion.ShouldBe("1");
        facts.Destination.TimeZone.ShouldBe("Europe/Moscow");
        facts.ReleaseValidUntil.ShouldBe(DateTimeOffset.Parse("2026-11-17T03:55:13Z"));
        facts.HumanCurator.ShouldBeNull();
        facts.HumanReviewer.ShouldBeNull();
        facts.HumanQuality.ShouldBe("not-evaluated");
        facts.CalibrationStatus.ShouldBe("not-evaluated");
        facts.Pois.ShouldAllBe(p => !p.Access.IsKnown);
        facts
            .Evidence.Where(e => e.Kind == ItineraryFactKind.Walking)
            .ShouldAllBe(e =>
                e.Method == ItineraryFactMethod.Modelled
                && e.RightsBasis == ItineraryRightsBasis.Odbl
                && e.ObservedAt == null
                && e.ModelledWalk != null
            );
        facts
            .Evidence.Where(e => e.Kind == ItineraryFactKind.IdentityLocation)
            .ShouldAllBe(e =>
                e.Method == ItineraryFactMethod.SourceRecord
                && e.RightsBasis == ItineraryRightsBasis.Cc0
                && e.ObservedAt == null
            );
        facts.Pois.Single(p => p.Id == "alexander-garden").Title.ShouldBe("Александровский сад");
        facts
            .Evidence.Single(e => e.Id == "walk-alexander-garden-tomb-unknown-soldier")
            .ModelledWalk!.FromAnchorNodeId.ShouldBe(835671925);
        facts
            .Edges.Count(e => e.FromPoiId == "zaryadye-park" || e.ToPoiId == "zaryadye-park")
            .ShouldBe(0);
    }

    [Theory]
    [InlineData("2026-10-21T08:16:16.5024145Z")]
    [InlineData("2026-11-17T03:55:13Z")]
    [InlineData("2027-01-01T00:00:00Z")]
    public async Task Actual_clock_rejects_prebuild_and_expired_packages(string at)
    {
        using var fixture = new Package();
        fixture.Clock.Now = DateTimeOffset.Parse(at);
        await Unavailable(fixture);
    }

    [Fact]
    public async Task Every_read_observes_withdrawal_missing_corrupt_and_expired_packages()
    {
        using var fixture = new Package();
        (
            await fixture.Source.ReadAsync(TestContext.Current.CancellationToken)
        ).IsError.ShouldBeFalse();
        fixture.Withdrawn = true;
        await Unavailable(fixture);
        fixture.Withdrawn = false;
        (
            await fixture.Source.ReadAsync(TestContext.Current.CancellationToken)
        ).IsError.ShouldBeFalse();
        fixture.Clock.Now = DateTimeOffset.Parse("2026-11-17T03:55:13Z");
        await Unavailable(fixture);
        fixture.Clock.Now = ReadyAt;
        File.AppendAllText(Path.Combine(fixture.Directory, "moscow-v1.json"), " ");
        await Unavailable(fixture);
        File.Delete(Path.Combine(fixture.Directory, "moscow-v1.json"));
        await Unavailable(fixture);
    }

    [Fact]
    public async Task Cancellation_propagates_without_error_translation()
    {
        using var fixture = new Package();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(() =>
            fixture.Source.ReadAsync(cancellation.Token)
        );
    }

    [Theory]
    [InlineData("method")]
    [InlineData("rights")]
    [InlineData("observed")]
    [InlineData("calibration")]
    [InlineData("humans")]
    [InlineData("quality")]
    [InlineData("destination")]
    [InlineData("zone")]
    [InlineData("version")]
    [InlineData("formula")]
    [InlineData("reversed")]
    [InlineData("anchor")]
    [InlineData("anchor-distance")]
    [InlineData("anchor-claim")]
    [InlineData("known-access")]
    [InlineData("source-hash")]
    [InlineData("source-url")]
    [InlineData("source-clock")]
    [InlineData("future-identity")]
    [InlineData("path-digest")]
    [InlineData("unknown-field")]
    [InlineData("missing-field")]
    [InlineData("wrong-type")]
    [InlineData("id-digit")]
    [InlineData("id-too-long")]
    [InlineData("id-space")]
    [InlineData("title-space")]
    [InlineData("title-too-long")]
    [InlineData("title-control")]
    [InlineData("unsafe-url")]
    public async Task Rehashed_payload_semantic_corruption_rejects_entire_package(string mutation)
    {
        using var fixture = new Package(semanticRelease: true);
        fixture.Mutate(
            "moscow-v1.json",
            root =>
            {
                var poi = root["pois"]![0]!;
                var edge = root["edges"]![0]!;
                switch (mutation)
                {
                    case "method":
                        edge["method"] = "observed";
                        break;
                    case "rights":
                        edge["rightsBasis"] = "cc0";
                        break;
                    case "observed":
                        edge["observedAt"] = "2026-10-21T08:16:16Z";
                        break;
                    case "calibration":
                        edge["calibrationStatus"] = "evaluated";
                        break;
                    case "humans":
                        root["humanReviewer"] = "person";
                        break;
                    case "quality":
                        root["humanQuality"] = "approved";
                        break;
                    case "destination":
                        root["destinationId"] = "london";
                        break;
                    case "zone":
                        root["timeZone"] = "UTC";
                        break;
                    case "version":
                        root["version"] = "2";
                        break;
                    case "formula":
                        edge["maximumMinutes"] = 6;
                        break;
                    case "reversed":
                        edge["fromPoiId"] = "tomb-unknown-soldier";
                        edge["toPoiId"] = "alexander-garden";
                        break;
                    case "anchor":
                        edge["fromAnchorNodeId"] = 42;
                        break;
                    case "anchor-distance":
                        poi["anchorDistanceMillimetres"] = 50001;
                        break;
                    case "anchor-claim":
                        poi["approximateAnchor"] = false;
                        break;
                    case "known-access":
                        poi["access"]!["isKnown"] = true;
                        break;
                    case "source-hash":
                        edge["sourceSha256"] = new string('a', 64);
                        break;
                    case "source-url":
                        root["source"]!["sourceUrl"] = "https://example.com/map";
                        break;
                    case "source-clock":
                        root["source"]!["snapshotPublishedAtUtc"] = "2026-10-22T00:00:00Z";
                        break;
                    case "future-identity":
                        poi["processedAtUtc"] = "2026-10-22T00:00:00Z";
                        break;
                    case "path-digest":
                        edge["pathHash"] = new string('a', 64);
                        break;
                    case "unknown-field":
                        poi["humanCurator"] = "alias";
                        break;
                    case "missing-field":
                        poi.AsObject().Remove("title");
                        break;
                    case "wrong-type":
                        poi["dwellMinutes"] = "40";
                        break;
                    case "id-digit":
                        poi["id"] = "1garden";
                        break;
                    case "id-too-long":
                        edge["id"] = new string('a', 65);
                        break;
                    case "id-space":
                        poi["id"] = " alexander-garden";
                        break;
                    case "title-space":
                        poi["title"] = " Александровский сад";
                        break;
                    case "title-too-long":
                        poi["title"] = new string('я', 101);
                        break;
                    case "title-control":
                        poi["title"] = "Сад\u0000";
                        break;
                    case "unsafe-url":
                        poi["sourceUrl"] = "file:///etc/passwd";
                        break;
                }
            }
        );
        await Unavailable(fixture);
    }

    [Theory]
    [InlineData("nodes")]
    [InlineData("ways")]
    [InlineData("crossings")]
    [InlineData("extra-path")]
    [InlineData("missing-path")]
    [InlineData("source")]
    [InlineData("bbox")]
    [InlineData("assumptions")]
    [InlineData("encoding")]
    public async Task Rehashed_evidence_must_match_immutable_path_and_model_contract(
        string mutation
    )
    {
        using var fixture = new Package(semanticRelease: true);
        fixture.Mutate(
            "walking-evidence.json",
            root =>
            {
                var path = root["paths"]![0]!;
                switch (mutation)
                {
                    case "nodes":
                        path["nodeIds"]![0] = -1;
                        break;
                    case "ways":
                        path["wayIds"]!.AsArray().RemoveAt(0);
                        break;
                    case "crossings":
                        path["crossingCount"] = 1000000;
                        break;
                    case "extra-path":
                        root["paths"]!.AsArray().Add(path.DeepClone());
                        break;
                    case "missing-path":
                        root["paths"]!.AsArray().RemoveAt(0);
                        break;
                    case "source":
                        root["source"]!["modelVersion"] = "model-v2";
                        break;
                    case "bbox":
                        root["bbox"]![0] = 54;
                        break;
                    case "assumptions":
                        root["modelAssumptions"] = "measured";
                        break;
                    case "encoding":
                        root["pathHashEncoding"] = "normalised";
                        break;
                }
            }
        );
        await Unavailable(fixture);
    }

    [Theory]
    [InlineData("inventory.json")]
    [InlineData("input-manifest.json")]
    [InlineData("ATTRIBUTION.txt")]
    [InlineData("walking-evidence.json")]
    [InlineData("moscow-v1.json")]
    public async Task Every_offered_file_requires_its_exact_manifest_digest(string file)
    {
        using var fixture = new Package(semanticRelease: true);
        File.AppendAllText(Path.Combine(fixture.Directory, file), " ");
        await Unavailable(fixture);
    }

    [Theory]
    [InlineData("title")]
    [InlineData("coordinate")]
    [InlineData("revision")]
    [InlineData("missing-poi")]
    [InlineData("input-hash")]
    [InlineData("input-clock")]
    [InlineData("input-bytes")]
    [InlineData("attribution")]
    public async Task Rehashed_reproduction_inputs_must_bind_the_payload(string mutation)
    {
        using var fixture = new Package(semanticRelease: true);
        if (mutation == "attribution")
            fixture.Write("ATTRIBUTION.txt", Encoding.UTF8.GetBytes("No credit\n"));
        else if (mutation.StartsWith("input-", StringComparison.Ordinal))
            fixture.Mutate(
                "input-manifest.json",
                root =>
                {
                    if (mutation == "input-hash")
                        root["sha256"] = new string('b', 64);
                    if (mutation == "input-clock")
                        root["downloadedAtUtc"] = "2026-10-21T08:00:00Z";
                    if (mutation == "input-bytes")
                        root["bytes"] = 209715201;
                }
            );
        else
            fixture.Mutate(
                "inventory.json",
                root =>
                {
                    if (mutation == "title")
                        root[0]!["title"] = "Another title";
                    if (mutation == "coordinate")
                        root[0]!["latitude"] = 55.74;
                    if (mutation == "revision")
                        root[0]!["wikidataRevision"] = 42;
                    if (mutation == "missing-poi")
                        root.AsArray().RemoveAt(0);
                }
            );
        await Unavailable(fixture);
    }

    [Theory]
    [InlineData("bom")]
    [InlineData("crlf")]
    [InlineData("utf8")]
    [InlineData("duplicate")]
    [InlineData("deep")]
    [InlineData("payload-cap")]
    [InlineData("evidence-cap")]
    [InlineData("manifest-cap")]
    public async Task Strict_byte_encoding_schema_and_actual_size_are_required(string mutation)
    {
        using var fixture = new Package(semanticRelease: true);
        var original = File.ReadAllBytes(Path.Combine(fixture.Directory, "moscow-v1.json"));
        switch (mutation)
        {
            case "bom":
                fixture.Write(
                    "moscow-v1.json",
                    new byte[] { 0xef, 0xbb, 0xbf }
                        .Concat(original)
                        .ToArray()
                );
                break;
            case "crlf":
                fixture.Write("moscow-v1.json", original.Concat(new byte[] { 13, 10 }).ToArray());
                break;
            case "utf8":
                fixture.Write("moscow-v1.json", original.Concat(new byte[] { 0xff }).ToArray());
                break;
            case "duplicate":
                fixture.Write(
                    "moscow-v1.json",
                    Encoding.UTF8.GetBytes(
                        Encoding
                            .UTF8.GetString(original)
                            .Replace(
                                "{\"datasetId\":",
                                "{\"datasetId\":\"moscow-outdoor\",\"datasetId\":"
                            )
                    )
                );
                break;
            case "deep":
                fixture.Write(
                    "moscow-v1.json",
                    Encoding.UTF8.GetBytes(new string('[', 17) + "0" + new string(']', 17))
                );
                break;
            case "payload-cap":
                fixture.Write("moscow-v1.json", new byte[262145]);
                break;
            case "evidence-cap":
                fixture.Write("walking-evidence.json", new byte[4194305]);
                break;
            case "manifest-cap":
                File.WriteAllBytes(
                    Path.Combine(fixture.Directory, "moscow-v1.manifest.json"),
                    new byte[2049]
                );
                break;
        }
        await Unavailable(fixture);
    }

    [Theory]
    [InlineData("../moscow-v1.json")]
    [InlineData("C:/moscow-v1.json")]
    [InlineData("https://example.com/moscow-v1.json")]
    [InlineData("MOSCOW-v1.json")]
    public async Task Manifest_never_controls_paths_or_aliases(string name)
    {
        using var fixture = new Package(semanticRelease: true);
        fixture.Mutate(
            "moscow-v1.manifest.json",
            root =>
            {
                var hash = root["files"]!["moscow-v1.json"]!.DeepClone();
                root["files"]!.AsObject().Remove("moscow-v1.json");
                root["files"]![name] = hash;
            }
        );
        await Unavailable(fixture);
    }

    [Fact]
    public async Task Built_content_and_public_asset_input_offer_the_same_six_canonical_byte_files()
    {
        using var fixture = new Package();
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "Travel.slnx")))
            root = root.Parent;
        var canonicalInput = "modules/trips/Travel.Modules.Trips.Infrastructure/Itineraries/Data";
        using var project = JsonDocument.Parse(
            File.ReadAllBytes(Path.Combine(root!.FullName, "apps/web/project.json"))
        );
        var asset = project
            .RootElement.GetProperty("targets")
            .GetProperty("build")
            .GetProperty("options")
            .GetProperty("assets")
            .EnumerateArray()
            .SingleOrDefault(a => a.GetProperty("input").GetString() == canonicalInput);
        asset.ValueKind.ShouldBe(JsonValueKind.Object);
        asset.GetProperty("glob").GetString().ShouldBe("**/*");
        asset.GetProperty("output").GetString().ShouldBe("trips-data");
        var built = Path.Combine(AppContext.BaseDirectory, "Trips/Itineraries/moscow-outdoor/1");
        foreach (var file in Directory.GetFiles(fixture.Directory))
        {
            var name = Path.GetFileName(file);
            var builtBytes = File.ReadAllBytes(Path.Combine(built, name));
            builtBytes.ShouldBe(File.ReadAllBytes(file));
            var publicOfferRelative = Path.Combine("trips-data", "moscow-outdoor", "1", name)
                .Replace('\\', '/');
            publicOfferRelative.ShouldStartWith("trips-data/moscow-outdoor/1/");
            Convert
                .ToHexStringLower(SHA256.HashData(builtBytes))
                .ShouldBe(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file))));
        }
        Directory.GetFiles(built).Length.ShouldBe(6);
        var source = new CuratedItineraryFacts(built, fixture.Clock);
        (await source.ReadAsync(TestContext.Current.CancellationToken)).IsError.ShouldBeFalse();
    }

    [Theory]
    [InlineData("leading-title")]
    [InlineData("trailing-title")]
    [InlineData("long-title")]
    [InlineData("digit-id")]
    [InlineData("long-id")]
    [InlineData("space-id")]
    public async Task Matching_inventory_and_payload_cannot_bypass_Core_id_and_text_factories(
        string mutation
    )
    {
        using var fixture = new Package(semanticRelease: true);
        var originalId = "morozovsky-garden"; // An explicitly isolated real POI: no edge/path reference changes needed.
        var title = mutation switch
        {
            "leading-title" => " Морозовский сад",
            "trailing-title" => "Морозовский сад ",
            "long-title" => new string('я', 101),
            _ => "Морозовский сад",
        };
        var id = mutation switch
        {
            "digit-id" => "1garden",
            "long-id" => new string('a', 65),
            "space-id" => "morozovsky-garden ",
            _ => originalId,
        };
        fixture.Mutate(
            "inventory.json",
            root =>
            {
                var poi = root.AsArray().Single(p => p!["id"]!.GetValue<string>() == originalId)!;
                poi["id"] = id;
                poi["title"] = title;
            }
        );
        fixture.Mutate(
            "moscow-v1.json",
            root =>
            {
                var poi = root["pois"]!
                    .AsArray()
                    .Single(p => p!["id"]!.GetValue<string>() == originalId)!;
                poi["id"] = id;
                poi["title"] = title;
            }
        );
        await Unavailable(fixture);
    }

    [Theory]
    [InlineData("negative-node")]
    [InlineData("missing-way")]
    [InlineData("wrong-endpoint")]
    [InlineData("repeated-node")]
    [InlineData("long-path-id")]
    public async Task Valid_path_digests_do_not_bypass_shape_endpoints_or_Core_path_id_limits(
        string mutation
    )
    {
        using var fixture = new Package(semanticRelease: true);
        fixture.Mutate(
            "walking-evidence.json",
            root =>
            {
                var path = root["paths"]![0]!;
                if (mutation == "negative-node")
                    path["nodeIds"]![1] = -1;
                if (mutation == "missing-way")
                    path["wayIds"]!.AsArray().RemoveAt(0);
                if (mutation == "wrong-endpoint")
                    path["nodeIds"]![0] = 42;
                if (mutation == "repeated-node")
                    path["nodeIds"]![1] = path["nodeIds"]![0]!.DeepClone();
                if (mutation == "long-path-id")
                    path["pathId"] = new string('a', 65);
            }
        );
        using var paths = JsonDocument.Parse(
            File.ReadAllBytes(Path.Combine(fixture.Directory, "walking-evidence.json"))
        );
        var first = paths.RootElement.GetProperty("paths")[0];
        var digest = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(first.GetRawText() + "\n"))
        );
        fixture.Mutate(
            "moscow-v1.json",
            root =>
            {
                root["edges"]![0]!["pathHash"] = digest;
                root["edges"]![0]!["pathId"] = first.GetProperty("pathId").GetString();
            }
        );
        await Unavailable(fixture);
    }

    [Theory]
    [InlineData("ATTRIBUTION.txt", 8193)]
    [InlineData("input-manifest.json", 4097)]
    [InlineData("inventory.json", 131073)]
    public async Task Reproduction_and_credit_files_have_independent_read_caps(
        string file,
        int bytes
    )
    {
        using var fixture = new Package(semanticRelease: true);
        fixture.Write(file, new byte[bytes]);
        await Unavailable(fixture);
    }

    [Theory]
    [InlineData("input-manifest.json")]
    [InlineData("inventory.json")]
    public async Task Reproduction_metadata_requires_explicit_UTC_without_offset_normalisation(
        string file
    )
    {
        using var fixture = new Package(semanticRelease: true);
        fixture.Mutate(
            file,
            root =>
            {
                if (file == "input-manifest.json")
                    root["snapshotPublishedAtUtc"] = "2026-10-18T06:55:13+03:00";
                else
                    root[0]!["retrievedAtUtc"] = "2026-10-21T11:16:16.5024146+03:00";
            }
        );
        await Unavailable(fixture);
    }

    [Theory]
    [InlineData("case-alias")]
    [InlineData("unoffered-file")]
    [InlineData("unoffered-directory")]
    public async Task Package_directory_contains_only_the_exact_six_public_filenames(
        string mutation
    )
    {
        using var fixture = new Package(semanticRelease: true);
        if (mutation == "case-alias")
        {
            var original = Path.Combine(fixture.Directory, "moscow-v1.json");
            var temporary = Path.Combine(fixture.Directory, "case-rename.tmp");
            File.Move(original, temporary);
            File.Move(temporary, Path.Combine(fixture.Directory, "MOSCOW-v1.json"));
        }
        else if (mutation == "unoffered-file")
            File.WriteAllText(
                Path.Combine(fixture.Directory, "private.txt"),
                "not part of the manifest"
            );
        else
            Directory.CreateDirectory(Path.Combine(fixture.Directory, "unoffered"));
        await Unavailable(fixture);
    }

    [Theory]
    [InlineData("matching-valid-title")]
    [InlineData("valid-coverage")]
    [InlineData("manifest-bytes")]
    public async Task Reviewed_v1_rejects_rehashed_semantically_valid_replacement_bytes(
        string mutation
    )
    {
        using var fixture = new Package();
        (
            await fixture.Source.ReadAsync(TestContext.Current.CancellationToken)
        ).IsError.ShouldBeFalse();
        if (mutation == "matching-valid-title")
        {
            fixture.Mutate(
                "inventory.json",
                root => root[0]!["title"] = "Александровский сад: прогулка"
            );
            fixture.Mutate(
                "moscow-v1.json",
                root => root["pois"]![0]!["title"] = "Александровский сад: прогулка"
            );
        }
        else if (mutation == "valid-coverage")
            fixture.Mutate(
                "moscow-v1.json",
                root => root["coverage"] = "Центр Москвы: изменённое описание покрытия."
            );
        else
            File.AppendAllText(Path.Combine(fixture.Directory, "moscow-v1.manifest.json"), " ");
        await Unavailable(fixture);
    }

    [Fact]
    public async Task Explicit_distinct_test_release_reaches_semantic_mapping_beyond_the_reviewed_v1_pin()
    {
        using var fixture = new Package(semanticRelease: true);
        fixture.Mutate(
            "inventory.json",
            root => root[0]!["title"] = "Александровский сад: прогулка"
        );
        fixture.Mutate(
            "moscow-v1.json",
            root => root["pois"]![0]!["title"] = "Александровский сад: прогулка"
        );
        fixture.BindKnownTestRelease();
        var result = await fixture.Source.ReadAsync(TestContext.Current.CancellationToken);
        result.IsError.ShouldBeFalse();
        result.Value.Version.ShouldBe("test-semantic-v1");
        result.Value.Pois.Count.ShouldBe(20);
        result.Value.Edges.Count.ShouldBe(90);
        result.Value.Pois[0].Title.ShouldBe("Александровский сад: прогулка");
        var productionDefault = new CuratedItineraryFacts(fixture.Directory, fixture.Clock);
        (
            await productionDefault.ReadAsync(TestContext.Current.CancellationToken)
        ).IsError.ShouldBeTrue();
    }

    [Theory]
    [InlineData(
        "moscow-outdoor",
        "1",
        "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
    )]
    [InlineData(
        "london",
        "test-semantic-v1",
        "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
    )]
    [InlineData(
        "moscow-outdoor",
        " test-semantic-v1",
        "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
    )]
    [InlineData(
        "moscow-outdoor",
        "test-semantic-v1",
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"
    )]
    public void Trusted_release_descriptor_cannot_repin_v1_expand_scope_or_normalise_inputs(
        string dataset,
        string version,
        string hash
    )
    {
        Should.Throw<ArgumentException>(() => new CuratedItineraryRelease(dataset, version, hash));
    }

    private static async Task Unavailable(Package fixture)
    {
        fixture.BindKnownTestRelease();
        var result = await fixture.Source.ReadAsync(TestContext.Current.CancellationToken);
        result.IsError.ShouldBeTrue();
        result.FirstError.Code.ShouldBe("Trips.ItineraryUnavailable");
        result.FirstError.Description.ShouldNotContain(fixture.Directory);
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = ReadyAt;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Package : IDisposable
    {
        public string Directory { get; } =
            Path.Combine(Path.GetTempPath(), "trips-facts-" + Guid.NewGuid().ToString("N"));
        public Clock Clock { get; } = new();
        public bool Withdrawn { get; set; }
        private readonly bool _semanticRelease;
        public CuratedItineraryFacts Source { get; private set; }

        public Package(bool semanticRelease = false)
        {
            _semanticRelease = semanticRelease;
            System.IO.Directory.CreateDirectory(Directory);
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root != null && !File.Exists(Path.Combine(root.FullName, "Travel.slnx")))
                root = root.Parent;
            var canonical = Path.Combine(
                root!.FullName,
                "modules/trips/Travel.Modules.Trips.Infrastructure/Itineraries/Data/moscow-outdoor/1"
            );
            foreach (var file in System.IO.Directory.GetFiles(canonical))
                File.Copy(file, Path.Combine(Directory, Path.GetFileName(file)));
            Source = new CuratedItineraryFacts(Directory, Clock, () => Withdrawn);
            if (_semanticRelease)
            {
                Mutate("moscow-v1.json", root => root["version"] = "test-semantic-v1");
                Mutate("moscow-v1.manifest.json", root => root["version"] = "test-semantic-v1");
                BindKnownTestRelease();
            }
        }

        // The test author explicitly binds a distinct fixture release, after preparing its bytes.
        // Production/default source construction never obtains its trusted pin from this directory.
        public void BindKnownTestRelease()
        {
            if (!_semanticRelease)
                return;
            var manifestHash = Convert.ToHexStringLower(
                SHA256.HashData(
                    File.ReadAllBytes(Path.Combine(Directory, "moscow-v1.manifest.json"))
                )
            );
            var expected = new CuratedItineraryRelease(
                "moscow-outdoor",
                "test-semantic-v1",
                manifestHash
            );
            Source = new CuratedItineraryFacts(Directory, Clock, () => Withdrawn, expected);
        }

        public void Mutate(string file, Action<JsonNode> mutation)
        {
            var node = JsonNode.Parse(File.ReadAllBytes(Path.Combine(Directory, file)))!;
            mutation(node);
            Write(file, Encoding.UTF8.GetBytes(node.ToJsonString() + "\n"));
        }

        public void Write(string file, byte[] bytes)
        {
            File.WriteAllBytes(Path.Combine(Directory, file), bytes);
            if (file == "moscow-v1.manifest.json")
                return;
            var path = Path.Combine(Directory, "moscow-v1.manifest.json");
            var manifest = JsonNode.Parse(File.ReadAllBytes(path))!;
            manifest["files"]![file] = Convert.ToHexStringLower(SHA256.HashData(bytes));
            File.WriteAllText(path, manifest.ToJsonString() + "\n", new UTF8Encoding(false));
        }

        public void Dispose()
        {
            var fullPath = Path.GetFullPath(Directory);
            var expectedParent = Path.GetFullPath(Path.GetTempPath());
            if (
                Path.GetDirectoryName(fullPath) != Path.TrimEndingDirectorySeparator(expectedParent)
                || !Path.GetFileName(fullPath).StartsWith("trips-facts-", StringComparison.Ordinal)
            )
                throw new InvalidOperationException("Unexpected fixture cleanup path.");
            System.IO.Directory.Delete(fullPath, true);
        }
    }
}
