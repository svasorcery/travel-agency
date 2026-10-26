using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Travel.Modules.Trips.Application.Itineraries;
using Travel.Modules.Trips.Infrastructure.Itineraries;
using Xunit;

namespace Travel.Modules.Trips.Tests.Integration;

// The first run is registered by build/list -> source binding -> one execution.
// Later executions are regression, never a fresh holdout or human evaluation.
public sealed class TripsSaintPetersburgQualityTests(ITestOutputHelper output)
{
    internal const string ClockText = "2026-10-27T12:00:00Z";
    internal const string CaseHash =
        "fc12bc22c49ebfa7498cd4f4a865be297c22aef73081aecd11eef1846fb9409b";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    [Fact]
    public async Task Frozen_twelve_real_cases_preserve_every_predeclared_expectation()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Travel.slnx")))
            root = root.Parent;
        var corpus = await File.ReadAllBytesAsync(
            Path.Combine(
                root!.FullName,
                "openspec/changes/trips-m22-saint-petersburg/quality-cases.json"
            ),
            Ct
        );
        Convert.ToHexStringLower(SHA256.HashData(corpus)).ShouldBe(CaseHash);
        using var cases = JsonDocument.Parse(corpus);
        var records = new List<TripsItineraryDemoIntegrityTests.CaseRecord>();
        foreach (var item in cases.RootElement.GetProperty("cases").EnumerateArray())
        {
            var r = new TripsItineraryDemoIntegrityTests.CaseRecord(
                Text(item, "id"),
                "real-supported",
                ClockText,
                CaseHash
            );
            records.Add(r);
            try
            {
                await Run(r, item);
            }
            catch (Exception error)
            {
                r.Failures.Add(error.ToString());
            }
            r.Execution = r.Failures.Count == 0 ? "completed" : "failed";
            output.WriteLine("TRIPS_M22_Q_RECORD_V1:" + JsonSerializer.Serialize(r, JsonOptions));
        }
        output.WriteLine(
            "TRIPS_M22_Q_SUMMARY_V1:"
                + JsonSerializer.Serialize(
                    new
                    {
                        total = records.Count,
                        passed = records.Count(r => r.Failures.Count == 0),
                        failed = records.Count(r => r.Failures.Count != 0),
                        humanScores = (object?)null,
                        humanQuality = "not-evaluated",
                        asOfUtc = ClockText,
                        caseSetSha256 = CaseHash,
                    },
                    JsonOptions
                )
        );
        records.Count.ShouldBe(12);
        records.SelectMany(r => r.Failures.Select(f => r.Id + ": " + f)).ShouldBeEmpty();
    }

    private static async Task Run(TripsItineraryDemoIntegrityTests.CaseRecord r, JsonElement item)
    {
        var clock = new ItineraryHttpClock
        {
            Now = DateTimeOffset.Parse(ClockText, CultureInfo.InvariantCulture),
        };
        var release = CuratedItineraryRelease.ReviewedSaintPetersburgV1;
        var package = Path.Combine(
            AppContext.BaseDirectory,
            "Trips/Itineraries/saint-petersburg-outdoor/1"
        );
        var source = new CuratedItineraryFacts(package, clock, expectedRelease: release);
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Moscow");
        var start = DateOnly
            .FromDateTime(TimeZoneInfo.ConvertTime(clock.Now, zone).DateTime)
            .AddDays(item.GetProperty("startLocalDayOffset").GetInt32());
        var brief = new JsonObject
        {
            ["destinationId"] = "saint-petersburg",
            ["originId"] = "moscow",
            ["startDate"] = start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["endDate"] = start
                .AddDays(item.GetProperty("days").GetInt32() - 1)
                .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["adults"] = item.GetProperty("adults").GetInt32(),
            ["currency"] = Text(item, "currency"),
            ["totalBudget"] = null,
            ["interests"] = JsonNode.Parse(item.GetProperty("interests").GetRawText()),
            ["pace"] = Text(item, "pace"),
        };
        r.RequestBrief = JsonSerializer.SerializeToElement(brief);
        r.SourceMode = "real-reviewed-spb-v1";
        r.TargetStops = item.GetProperty("targetStops").GetInt32();
        await using var f = await TripsHttpFixture.Start(
            itinerary: true,
            configure: services =>
            {
                services.AddSingleton<TimeProvider>(clock);
                services.AddSingleton<IItineraryFactSource>(source);
            }
        );
        using var created = await f.Client.PostAsync(
            "/api/trips",
            TripsHttpTests.Body(
                new JsonObject
                {
                    ["title"] = "FICTIONAL SPb Q_demo " + r.Id,
                    ["brief"] = brief,
                }.ToJsonString()
            ),
            Ct
        );
        ((int)created.StatusCode).ShouldBe(201);
        var before = f.Store.Rows.Values.Single();
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            created.Headers.Location + "/itinerary-candidate"
        )
        {
            Content = TripsHttpTests.Body("{}"),
        };
        request.Headers.Add("X-Trips-Itinerary-Version", "2");
        request.Headers.Add("If-Match", "\"1\"");
        using var response = await f.Client.SendAsync(request, Ct);
        // Record the actual HTTP body before any parsing or assertions; never regenerate.
        var bytes = await response.Content.ReadAsByteArrayAsync(Ct);
        r.Status = (int)response.StatusCode;
        r.BodyLength = bytes.Length;
        r.BodyUtf8Base64 = Convert.ToBase64String(bytes);
        r.BodySha256 = Convert.ToHexStringLower(SHA256.HashData(bytes));
        f.Store.Rows.Values.Single().ShouldBe(before);
        f.Store.Rows.Count.ShouldBe(1);
        r.Status.ShouldBe(200);
        bytes.Length.ShouldBeLessThanOrEqualTo(128 * 1024);
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
        using var parsed = JsonDocument.Parse(bytes);
        var body = parsed.RootElement;
        r.Outcome = Text(body, "outcome");
        body.GetProperty("candidateVersion").GetInt32().ShouldBe(2);
        Text(body, "destinationId").ShouldBe("saint-petersburg");
        Text(body, "baseRevision").ShouldBe("1");
        DateTimeOffset
            .Parse(Text(body, "generatedAt"), CultureInfo.InvariantCulture)
            .ShouldBe(clock.Now);
        var loaded = await source.ReadAsync(Ct);
        loaded.IsError.ShouldBeFalse();
        var facts = loaded.Value;
        facts.Pois.Count.ShouldBe(20);
        facts.Edges.Count.ShouldBe(62);
        TripsItineraryDemoIntegrityTests.VerifyComputed(
            r,
            body,
            facts,
            r.RequestBrief.Value,
            package,
            clock.Now,
            release
        );
        r.Outcome.ShouldBe(Text(item, "expectedOutcome"));
        Text(body, "coverage").ShouldBe(Text(item, "expectedCoverage"));
        Strings(body, "unmetInterests")
            .Order()
            .ShouldBe(Strings(item, "expectedUnmetInterests").Order());
        r.FilledTarget.ShouldBeTrue(
            "Every predeclared target remains required, including food partial cases."
        );
        var requested = Strings(item, "interests").ToHashSet(StringComparer.Ordinal);
        var selectedTags = body.GetProperty("days")
            .EnumerateArray()
            .SelectMany(d => d.GetProperty("blocks").EnumerateArray())
            .SelectMany(b => facts.Pois.Single(p => p.Id == Text(b, "poiId")).Tags)
            .ToHashSet(StringComparer.Ordinal);
        var covered = requested.Intersect(selectedTags).Order().ToArray();
        var unmet = requested.Except(selectedTags).Order().ToArray();
        Strings(body, "coveredInterests").Order().ShouldBe(covered);
        Strings(body, "unmetInterests").Order().ShouldBe(unmet);
        body.GetProperty("expansionCount").GetInt32().ShouldBeLessThanOrEqualTo(20_000);
        Text(body, "coverage")
            .ShouldBe(
                r.FilledTarget
                && unmet.Length == 0
                && !body.GetProperty("searchLimited").GetBoolean()
                    ? "filled"
                    : "partial"
            );
        foreach (var day in body.GetProperty("days").EnumerateArray())
        {
            foreach (var block in day.GetProperty("blocks").EnumerateArray())
            {
                var poi = facts.Pois.Single(p => p.Id == Text(block, "poiId"));
                Strings(block, "tags").Order().ShouldBe(poi.Tags.Order());
                block.GetProperty("dwellMinutes").GetInt32().ShouldBe(poi.DwellMinutes);
            }
            var walk = day.GetProperty("edges")
                .EnumerateArray()
                .Sum(e => e.GetProperty("maximumMinutes").GetInt32());
            var dwell = day.GetProperty("blocks")
                .EnumerateArray()
                .Sum(b => facts.Pois.Single(p => p.Id == Text(b, "poiId")).DwellMinutes);
            var (walkCap, durationCap) = Text(item, "pace") switch
            {
                "relaxed" => (60, 240),
                "balanced" => (90, 360),
                _ => (120, 480),
            };
            walk.ShouldBeLessThanOrEqualTo(walkCap);
            (walk + dwell).ShouldBeLessThanOrEqualTo(durationCap);
        }
        Strings(body, "uncertainties").ShouldContain("openingHoursUnknown");
    }

    private static string Text(JsonElement value, string name) =>
        value.GetProperty(name).GetString()!;

    private static string[] Strings(JsonElement value, string name) =>
        value.GetProperty(name).EnumerateArray().Select(v => v.GetString()!).ToArray();
}
