using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using ErrorOr;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Travel.Modules.Trips.Application.Itineraries;
using Travel.Modules.Trips.Core;
using Travel.Modules.Trips.Core.Itineraries;
using Travel.Modules.Trips.Infrastructure.Itineraries;
using Xunit;

namespace Travel.Host.Tests.Integration.Trips;

[Trait("Category", "Integration")]
[Collection(HostIntegrationCollection.Name)]
public sealed class TripsPackageLifecycleTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Both_base_codec_plans_survive_SPb_release_failures_and_Host_restart_with_exact_keys()
    {
        using var packages = new PackageSelection("saint-petersburg");
        await using var fixture = new TripsHostFixture(packages, packages.Clock);
        await fixture.StartAsync(Ct);
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Travel.slnx")))
            root = root.Parent;
        var plans = new List<(Guid Id, string Text, Row Row)>();
        foreach (var city in new[] { "moscow", "saint-petersburg" })
        {
            var text = await File.ReadAllTextAsync(
                Path.Combine(
                    root!.FullName,
                    "tests/fixtures/trips-m1-legacy-codec",
                    "legacy-" + city + ".detail.json"
                ),
                Ct
            );
            var id = await fixture.SeedLegacyAsync(text, Ct);
            plans.Add((id, text, await Snapshot(fixture, id)));
        }
        var keys = fixture.KeyHashes();
        await Unchanged();
        await Availability(HttpStatusCode.OK);
        packages.Clock.Now = DateTimeOffset.Parse("2026-11-24T04:08:52Z");
        await Availability(HttpStatusCode.ServiceUnavailable);
        await Unchanged();
        packages.Clock.Now = DateTimeOffset.Parse("2026-10-27T12:00:00Z");
        var file = Path.Combine(packages.Directory, "saint-petersburg-v1.json");
        var original = await File.ReadAllBytesAsync(file, Ct);
        File.AppendAllText(file, " ");
        await Availability(HttpStatusCode.ServiceUnavailable);
        await Unchanged();
        await File.WriteAllBytesAsync(file, original, Ct);
        packages.Withdrawn = true;
        await Availability(HttpStatusCode.ServiceUnavailable);
        await Unchanged();
        packages.Withdrawn = false;
        packages.SelectSyntheticNext();
        // V2 refuses an unreviewed replacement tuple even if its own file hashes are valid.
        await Availability(HttpStatusCode.ServiceUnavailable);
        await fixture.RestartAsync();
        await Unchanged();
        foreach (var old in plans)
        {
            fixture.Client.DefaultRequestHeaders.Remove("X-Trips-Owner");
            fixture.Client.DefaultRequestHeaders.Add("X-Trips-Owner", Guid.NewGuid().ToString());
            using var foreign = await fixture.Client.GetAsync("/api/trips/" + old.Id, Ct);
            foreign.StatusCode.ShouldBe(HttpStatusCode.NotFound);
            fixture.Client.DefaultRequestHeaders.Remove("X-Trips-Owner");
            fixture.Client.DefaultRequestHeaders.Add(
                "X-Trips-Owner",
                TripsHostFixture.Owner.ToString()
            );
            var detail = JsonNode.Parse(old.Text)!;
            var replacement = new JsonObject
            {
                ["title"] = "FICTIONAL explicit manual update",
                ["brief"] = detail["brief"]!.DeepClone(),
                ["days"] = detail["days"]!.DeepClone(),
            };
            using var staleRequest = Write(old.Id, "6", replacement);
            using var stale = await fixture.Client.SendAsync(staleRequest, Ct);
            stale.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
            (await Snapshot(fixture, old.Id)).ShouldBe(old.Row);
            using var editRequest = Write(old.Id, "7", replacement);
            using var edit = await fixture.Client.SendAsync(editRequest, Ct);
            edit.StatusCode.ShouldBe(HttpStatusCode.OK);
            var edited = JsonNode.Parse(await edit.Content.ReadAsStringAsync(Ct))!;
            edited["revision"]!.GetValue<string>().ShouldBe("8");
            edited["days"]!.ToJsonString().ShouldBe(detail["days"]!.ToJsonString());
            edited["destination"]!.ToJsonString().ShouldBe(detail["destination"]!.ToJsonString());
        }
        fixture.KeyHashes().ShouldBe(keys);

        async Task Availability(HttpStatusCode status)
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                "/api/trips/itinerary-availability?destinationId=saint-petersburg"
            );
            request.Headers.Add("X-Trips-Itinerary-Version", "2");
            using var response = await fixture.Client.SendAsync(request, Ct);
            response.StatusCode.ShouldBe(status);
        }
        async Task Unchanged()
        {
            foreach (var old in plans)
            {
                using var read = await fixture.Client.GetAsync("/api/trips/" + old.Id, Ct);
                read.StatusCode.ShouldBe(HttpStatusCode.OK);
                (await read.Content.ReadAsStringAsync(Ct)).ShouldBe(old.Text);
                (await Snapshot(fixture, old.Id)).ShouldBe(old.Row);
            }
            fixture.KeyHashes().ShouldBe(keys);
        }
    }

    [Fact]
    public async Task Saved_v1_itinerary_survives_source_replacement_expiry_corruption_and_Host_restart()
    {
        using var packages = new PackageSelection();
        await using var fixture = new TripsHostFixture(packages, packages.Clock);
        await fixture.StartAsync(Ct);
        var original = await Create(fixture.Client);
        var candidate = await Candidate(fixture.Client, original);
        candidate["datasetVersion"]!.GetValue<string>().ShouldBe("1");
        var replacement = new JsonObject
        {
            ["title"] = "FICTIONAL saved v1 itinerary",
            ["brief"] = original["brief"]!.DeepClone(),
            ["days"] = new JsonArray(
                candidate["days"]!
                    .AsArray()
                    .Select(day =>
                        (JsonNode)
                            new JsonObject
                            {
                                ["date"] = day!["date"]!.DeepClone(),
                                ["blocks"] = new JsonArray(
                                    day["blocks"]!
                                        .AsArray()
                                        .Select(block =>
                                            (JsonNode)
                                                new JsonObject
                                                {
                                                    ["blockId"] = block![
                                                        "proposedBlockId"
                                                    ]!.DeepClone(),
                                                    ["title"] = block["title"]!.DeepClone(),
                                                    ["note"] = block["note"]!.DeepClone(),
                                                    ["startTime"] = null,
                                                    ["endTime"] = null,
                                                    ["estimate"] = null,
                                                }
                                        )
                                        .ToArray()
                                ),
                            }
                    )
                    .ToArray()
            ),
        };
        var id = original["tripId"]!.GetValue<Guid>();
        using var save = Write(id, "1", replacement);
        using var saved = await fixture.Client.SendAsync(save, Ct);
        saved.StatusCode.ShouldBe(HttpStatusCode.OK);
        var expected = await saved.Content.ReadAsStringAsync(Ct);
        var before = await Snapshot(fixture, id);
        before.Revision.ShouldBe(2);
        replacement["days"]![0]!["blocks"]!.AsArray().Count.ShouldBeGreaterThan(0);
        await AssertUnchanged();

        packages.SelectSyntheticNext();
        await fixture.RestartAsync();
        using (
            var available = await fixture.Client.GetAsync("/api/trips/itinerary-availability", Ct)
        )
        {
            available.StatusCode.ShouldBe(HttpStatusCode.OK);
            JsonNode.Parse(await available.Content.ReadAsStringAsync(Ct))!["datasetVersion"]!
                .GetValue<string>()
                .ShouldBe("test-lifecycle-next");
        }
        var fresh = await Create(fixture.Client);
        (await Candidate(fixture.Client, fresh))["datasetVersion"]!
            .GetValue<string>()
            .ShouldBe("test-lifecycle-next");
        await AssertUnchanged();

        packages.Clock.Now = DateTimeOffset.Parse("2026-11-17T03:55:13Z");
        var expiryBrief = await Create(fixture.Client, "2026-11-18");
        await AssertUnavailable(expiryBrief);
        await AssertUnchanged();
        packages.Clock.Now = DateTimeOffset.Parse("2026-10-24T12:00:00Z");
        File.AppendAllText(Path.Combine(packages.Directory, "moscow-v1.json"), " ");
        await AssertUnavailable(fresh);
        await AssertUnchanged();
        await fixture.RestartAsync();
        await AssertUnchanged();

        replacement["title"] = "FICTIONAL manual edit after source failure";
        using var edit = Write(id, "2", replacement);
        using var edited = await fixture.Client.SendAsync(edit, Ct);
        edited.StatusCode.ShouldBe(HttpStatusCode.OK);
        edited.Headers.ETag!.Tag.ShouldBe("\"3\"");
        var after = JsonNode.Parse(await edited.Content.ReadAsStringAsync(Ct))!;
        after["days"]!.ToJsonString().ShouldBe(replacement["days"]!.ToJsonString());

        async Task AssertUnchanged()
        {
            using var read = await fixture.Client.GetAsync("/api/trips/" + id, Ct);
            read.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await read.Content.ReadAsStringAsync(Ct)).ShouldBe(expected);
            read.Headers.ETag!.Tag.ShouldBe("\"2\"");
            (await Snapshot(fixture, id)).ShouldBe(before);
        }
        async Task AssertUnavailable(JsonNode plan)
        {
            using var available = await fixture.Client.GetAsync(
                "/api/trips/itinerary-availability",
                Ct
            );
            available.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                "/api/trips/" + plan["tripId"]!.GetValue<string>() + "/itinerary-candidate"
            )
            {
                Content = JsonContent.Create(new { }),
            };
            request.Headers.TryAddWithoutValidation("If-Match", "\"1\"");
            using var result = await fixture.Client.SendAsync(request, Ct);
            result.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        }
    }

    private static async Task<JsonNode> Create(HttpClient client, string date = "2026-10-25")
    {
        using var response = await client.PostAsJsonAsync(
            "/api/trips",
            new
            {
                title = "FICTIONAL package lifecycle",
                brief = new
                {
                    destinationId = "moscow",
                    originId = (string?)null,
                    startDate = date,
                    endDate = date,
                    adults = 2,
                    currency = "RUB",
                    totalBudget = (string?)null,
                    interests = new[] { "history", "nature" },
                    pace = "balanced",
                },
            },
            Ct
        );
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return JsonNode.Parse(await response.Content.ReadAsStringAsync(Ct))!;
    }

    private static async Task<JsonNode> Candidate(HttpClient client, JsonNode plan)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/trips/" + plan["tripId"]!.GetValue<string>() + "/itinerary-candidate"
        )
        {
            Content = JsonContent.Create(new { }),
        };
        request.Headers.TryAddWithoutValidation("If-Match", "\"1\"");
        using var response = await client.SendAsync(request, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return JsonNode.Parse(await response.Content.ReadAsStringAsync(Ct))!;
    }

    private static HttpRequestMessage Write(Guid id, string revision, JsonNode body)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, "/api/trips/" + id)
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.TryAddWithoutValidation("If-Match", "\"" + revision + "\"");
        return request;
    }

    private static async Task<Row> Snapshot(TripsHostFixture fixture, Guid id)
    {
        await using var db = fixture.Db();
        var row = await db.Plans.AsNoTracking().SingleAsync(p => p.TripId == id, Ct);
        return new(
            row.TripId,
            row.OwnerIssuer,
            row.OwnerSubject,
            row.Revision,
            row.CreatedAt,
            row.UpdatedAt,
            row.ProtectedPayload
        );
    }

    private sealed record Row(
        Guid Id,
        string Issuer,
        Guid Subject,
        long Revision,
        DateTimeOffset Created,
        DateTimeOffset Updated,
        string Payload
    );

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.Parse("2026-10-24T12:00:00Z");

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class PackageSelection : IItineraryFactSource, IDisposable
    {
        public string Directory { get; } =
            Path.Combine(
                Path.GetTempPath(),
                "travel-trips-package-lifecycle-" + Guid.NewGuid().ToString("N")
            );
        public Clock Clock { get; } = new();
        private CuratedItineraryFacts source;
        public bool Withdrawn { get; set; }
        public string City { get; }
        private CuratedItineraryRelease Release =>
            City == "moscow"
                ? CuratedItineraryRelease.ReviewedV1
                : CuratedItineraryRelease.ReviewedSaintPetersburgV1;

        public PackageSelection(string city = "moscow")
        {
            City = city;
            if (city == "saint-petersburg")
                Clock.Now = DateTimeOffset.Parse("2026-10-27T12:00:00Z");
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root is not null && !File.Exists(Path.Combine(root.FullName, "Travel.slnx")))
                root = root.Parent;
            var canonical = Path.Combine(
                root!.FullName,
                $"modules/trips/Travel.Modules.Trips.Infrastructure/Itineraries/Data/{city}-outdoor/1"
            );
            System.IO.Directory.CreateDirectory(Directory);
            foreach (var file in System.IO.Directory.GetFiles(canonical))
                File.Copy(file, Path.Combine(Directory, Path.GetFileName(file)));
            source = new(Directory, Clock, expectedRelease: Release);
        }

        public void SelectSyntheticNext()
        {
            // Synthetic identity only: this is not a refreshed map or a reviewed v2 release.
            var payloadFile = Path.Combine(Directory, Release.PayloadFile);
            var payload = JsonNode.Parse(File.ReadAllBytes(payloadFile))!;
            payload["version"] = "test-lifecycle-next";
            var bytes = Encoding.UTF8.GetBytes(payload.ToJsonString() + "\n");
            File.WriteAllBytes(payloadFile, bytes);
            var manifestFile = Path.Combine(Directory, Release.ManifestFile);
            var manifest = JsonNode.Parse(File.ReadAllBytes(manifestFile))!;
            manifest["version"] = "test-lifecycle-next";
            manifest["files"]![Release.PayloadFile] = Convert.ToHexStringLower(
                SHA256.HashData(bytes)
            );
            File.WriteAllText(
                manifestFile,
                manifest.ToJsonString() + "\n",
                new UTF8Encoding(false)
            );
            source = new(
                Directory,
                Clock,
                expectedRelease: new(
                    Release.DatasetId,
                    "test-lifecycle-next",
                    Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(manifestFile)))
                )
            );
        }

        public Task<ErrorOr<ValidatedItineraryFacts>> ReadAsync(
            DestinationSelection destination,
            CancellationToken ct
        ) =>
            Withdrawn
                ? Task.FromResult<ErrorOr<ValidatedItineraryFacts>>(
                    Error.Failure("Trips.ItineraryUnavailable", "Fictional withdrawal")
                )
                : source.ReadAsync(destination, ct);

        public void Dispose()
        {
            if (
                Path.GetDirectoryName(Path.GetFullPath(Directory))
                    != Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()))
                || !Path.GetFileName(Directory)
                    .StartsWith("travel-trips-package-lifecycle-", StringComparison.Ordinal)
            )
                throw new InvalidOperationException("Unexpected fixture path.");
            System.IO.Directory.Delete(Directory, true);
        }
    }
}
