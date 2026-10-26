using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Shouldly;
using Travel.Modules.Trips.Infrastructure.Itineraries;
using Xunit;

namespace Travel.Modules.Trips.Tests.Integration;

public sealed class CuratedSaintPetersburgFactsTests
{
    private const string ManifestHash =
        "3417fad216eaa25f2e0f85c8abd932b080220347cf85134f9f5a763900542a4d";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Exact_petersburg_release_loads_without_moscow_fallback_or_claimed_access()
    {
        using var fixture = new Package();
        var result = await fixture.Source().ReadAsync(Ct);
        result.IsError.ShouldBeFalse();
        result.Value.DatasetId.ShouldBe("saint-petersburg-outdoor");
        result.Value.Destination.DestinationId.ShouldBe("saint-petersburg");
        result.Value.Destination.CatalogueVersion.ShouldBe("1");
        result.Value.Pois.Count.ShouldBe(20);
        result.Value.Edges.Count.ShouldBe(62);
        result.Value.ModelSource.ProfileVersion.ShouldBe("spb-outdoor-profile-v1");
        result.Value.DatasetHash.ShouldBe(
            "dd128fa989936720b2e5cd0893a658b4878c49b4be99ba6aad5c89dfe91a3d12"
        );
        result.Value.ReleaseValidUntil.ShouldBe(DateTimeOffset.Parse("2026-11-24T04:08:52Z"));
        result.Value.Pois.ShouldAllBe(p => !p.Access.IsKnown);
        result.Value.HumanQuality.ShouldBe("not-evaluated");
        result.Value.HumanCurator.ShouldBeNull();
        (
            await new CuratedItineraryFacts(fixture.Directory, fixture.Clock).ReadAsync(Ct)
        ).IsError.ShouldBeTrue();
    }

    [Fact]
    public async Task Explicit_semantic_fixture_reaches_mapping_with_a_distinct_test_version()
    {
        using var fixture = new Package(semantic: true);
        (await fixture.Source().ReadAsync(Ct)).IsError.ShouldBeFalse();
    }

    [Theory]
    [InlineData("destination")]
    [InlineData("profile")]
    [InlineData("source")]
    [InlineData("bbox")]
    [InlineData("excluded-way")]
    [InlineData("profile-input")]
    [InlineData("duplicate-way")]
    [InlineData("unknown-profile-field")]
    public async Task Rehashed_mixed_city_or_inconsistent_preparation_is_rejected(string mutation)
    {
        using var fixture = new Package(semantic: true);
        switch (mutation)
        {
            case "destination":
                fixture.Mutate("saint-petersburg-v1.json", p => p["destinationId"] = "moscow");
                break;
            case "profile":
                fixture.Mutate(
                    "saint-petersburg-v1.json",
                    p => p["source"]!["profileVersion"] = "profile-v1"
                );
                break;
            case "source":
                fixture.Mutate(
                    "saint-petersburg-v1.json",
                    p =>
                        p["source"]!["sourceUrl"] =
                            "https://download.bbbike.org/osm/bbbike/Moscow/Moscow.osm.gz"
                );
                break;
            case "bbox":
                fixture.Mutate("walking-evidence.json", p => p["bbox"]![0] = 55.72);
                break;
            case "profile-input":
                fixture.Mutate(
                    "preparation-profile.json",
                    p => p["inputSha256"] = new string('a', 64)
                );
                break;
            case "duplicate-way":
                fixture.Mutate(
                    "preparation-profile.json",
                    p =>
                        p["exclusions"]![0]!["wayIds"]!
                            .AsArray()
                            .Add(p["exclusions"]![0]!["wayIds"]![0]!.GetValue<long>())
                );
                break;
            case "unknown-profile-field":
                fixture.Mutate("preparation-profile.json", p => p["allowIndoor"] = true);
                break;
            case "excluded-way":
                var walk = JsonNode.Parse(
                    File.ReadAllText(Path.Combine(fixture.Directory, "walking-evidence.json"))
                )!;
                var used = walk["paths"]![0]!["wayIds"]![0]!.GetValue<long>();
                fixture.Mutate(
                    "preparation-profile.json",
                    p =>
                        p["exclusions"]!
                            .AsArray()
                            .Add(
                                new JsonObject
                                {
                                    ["reason"] = "courtyard",
                                    ["sourceUrl"] = "https://fictional.example.test/blocked",
                                    ["wayIds"] = new JsonArray(JsonValue.Create(used)),
                                }
                            )
                );
                break;
        }
        var result = await fixture.Source().ReadAsync(Ct);
        result.IsError.ShouldBeTrue();
        result.FirstError.Code.ShouldBe("Trips.ItineraryUnavailable");
    }

    [Theory]
    [InlineData("2026-10-24T12:00:00Z")]
    [InlineData("2026-11-24T04:08:52Z")]
    public async Task City_specific_clock_enforces_build_and_expiry(string now)
    {
        using var fixture = new Package();
        fixture.Clock.Now = DateTimeOffset.Parse(now);
        (await fixture.Source().ReadAsync(Ct)).IsError.ShouldBeTrue();
    }

    internal sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.Parse("2026-10-27T12:00:00Z");

        public override DateTimeOffset GetUtcNow() => Now;
    }

    internal sealed class Package : IDisposable
    {
        public string Directory { get; } =
            Path.Combine(Path.GetTempPath(), "trips-spb-facts-" + Guid.NewGuid().ToString("N"));
        public Clock Clock { get; } = new();
        private readonly bool semantic;

        public Package(bool semantic = false)
        {
            this.semantic = semantic;
            System.IO.Directory.CreateDirectory(Directory);
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root != null && !File.Exists(Path.Combine(root.FullName, "Travel.slnx")))
                root = root.Parent;
            var canonical = Path.Combine(
                root!.FullName,
                "modules/trips/Travel.Modules.Trips.Infrastructure/Itineraries/Data/saint-petersburg-outdoor/1"
            );
            foreach (var file in System.IO.Directory.GetFiles(canonical))
                File.Copy(file, Path.Combine(Directory, Path.GetFileName(file)));
            if (semantic)
            {
                Mutate("saint-petersburg-v1.json", p => p["version"] = "test-spb-semantic-v1");
                Mutate(
                    "saint-petersburg-v1.manifest.json",
                    p => p["version"] = "test-spb-semantic-v1"
                );
            }
        }

        public CuratedItineraryFacts Source()
        {
            var hash = semantic
                ? Convert.ToHexStringLower(
                    SHA256.HashData(
                        File.ReadAllBytes(
                            Path.Combine(Directory, "saint-petersburg-v1.manifest.json")
                        )
                    )
                )
                : ManifestHash;
            return new CuratedItineraryFacts(
                Directory,
                Clock,
                expectedRelease: new CuratedItineraryRelease(
                    "saint-petersburg-outdoor",
                    semantic ? "test-spb-semantic-v1" : "1",
                    hash
                )
            );
        }

        public void Mutate(string file, Action<JsonNode> mutation)
        {
            var path = Path.Combine(Directory, file);
            var node = JsonNode.Parse(File.ReadAllBytes(path))!;
            mutation(node);
            File.WriteAllText(path, node.ToJsonString() + "\n", new UTF8Encoding(false));
            if (file == "saint-petersburg-v1.manifest.json")
                return;
            var manifestPath = Path.Combine(Directory, "saint-petersburg-v1.manifest.json");
            var manifest = JsonNode.Parse(File.ReadAllBytes(manifestPath))!;
            manifest["files"]![file] = Convert.ToHexStringLower(
                SHA256.HashData(File.ReadAllBytes(path))
            );
            File.WriteAllText(
                manifestPath,
                manifest.ToJsonString() + "\n",
                new UTF8Encoding(false)
            );
        }

        public void Dispose()
        {
            var full = Path.GetFullPath(Directory);
            if (
                Path.GetDirectoryName(full)
                    != Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()))
                || !Path.GetFileName(full).StartsWith("trips-spb-facts-", StringComparison.Ordinal)
            )
                throw new InvalidOperationException("Unexpected fixture cleanup path");
            System.IO.Directory.Delete(full, true);
        }
    }
}
