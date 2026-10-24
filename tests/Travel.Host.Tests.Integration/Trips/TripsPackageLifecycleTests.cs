using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using ErrorOr;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Travel.Modules.Trips.Application.Itineraries;
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
        private CuratedMoscowFacts source;

        public PackageSelection()
        {
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root is not null && !File.Exists(Path.Combine(root.FullName, "Travel.slnx")))
                root = root.Parent;
            var canonical = Path.Combine(
                root!.FullName,
                "modules/trips/Travel.Modules.Trips.Infrastructure/Itineraries/Data/moscow-outdoor/1"
            );
            System.IO.Directory.CreateDirectory(Directory);
            foreach (var file in System.IO.Directory.GetFiles(canonical))
                File.Copy(file, Path.Combine(Directory, Path.GetFileName(file)));
            source = new(Directory, Clock);
        }

        public void SelectSyntheticNext()
        {
            // Synthetic identity only: this is not a refreshed map or a reviewed v2 release.
            var payloadFile = Path.Combine(Directory, "moscow-v1.json");
            var payload = JsonNode.Parse(File.ReadAllBytes(payloadFile))!;
            payload["version"] = "test-lifecycle-next";
            var bytes = Encoding.UTF8.GetBytes(payload.ToJsonString() + "\n");
            File.WriteAllBytes(payloadFile, bytes);
            var manifestFile = Path.Combine(Directory, "moscow-v1.manifest.json");
            var manifest = JsonNode.Parse(File.ReadAllBytes(manifestFile))!;
            manifest["version"] = "test-lifecycle-next";
            manifest["files"]!["moscow-v1.json"] = Convert.ToHexStringLower(SHA256.HashData(bytes));
            File.WriteAllText(
                manifestFile,
                manifest.ToJsonString() + "\n",
                new UTF8Encoding(false)
            );
            source = new(
                Directory,
                Clock,
                expectedRelease: new(
                    "moscow-outdoor",
                    "test-lifecycle-next",
                    Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(manifestFile)))
                )
            );
        }

        public Task<ErrorOr<ValidatedItineraryFacts>> ReadAsync(CancellationToken ct) =>
            source.ReadAsync(ct);

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
