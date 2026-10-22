using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ErrorOr;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Travel.Modules.Trips.Application;
using Travel.Modules.Trips.Application.Itineraries;
using Travel.Modules.Trips.Core;
using Travel.Modules.Trips.Core.Itineraries;
using Travel.Modules.Trips.Infrastructure.Itineraries;
using Xunit;

namespace Travel.Modules.Trips.Tests.Integration;

// Ordinary deterministic regression. The first evaluation is orchestrated build/list -> bind -> run.
// Discovery only constructs data; no package loading, HTTP or generator calls occur before this Fact.
public sealed class TripsItineraryDemoIntegrityTests(ITestOutputHelper output)
{
    internal const string Prefix = "TRIPS_M2_DEMO_RECORD_V1:";
    internal const string ClockText = "2026-10-21T14:16:33.0000000Z";
    internal const string CaseHash =
        "f50b7511fc12642b20df7ca207c780740382eeb4a482258929551ce29aa5756c";
    private static readonly string[] Ids =
    [
        "Q01",
        "Q02",
        "Q03",
        "Q04",
        "Q05",
        "Q06",
        "Q07",
        "Q08",
        "Q09",
        "Q10",
        "Q11",
        "Q12",
        "S01",
        "S02",
        "S03",
        "S04",
        "S05",
        "S06",
        "S07",
        "S08",
        "S09",
        "S10",
        "S11",
        "S12",
    ];
    private static DateTimeOffset Now =>
        DateTimeOffset.Parse(ClockText, CultureInfo.InvariantCulture);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    [Fact]
    public async Task Frozen_real_demo_has_all_24_outcomes_and_at_least_11_filled_Q_targets()
    {
        var records = new List<CaseRecord>();
        foreach (var id in Ids)
        {
            var record = new CaseRecord(
                id,
                id[0] == 'Q' ? "real-supported"
                    : int.Parse(id[1..]) <= 6 ? "structural-edge"
                    : "structural-negative"
            );
            try
            {
                if (Ct.IsCancellationRequested)
                {
                    record.Execution = "unrun";
                    record.Failures.Add(
                        "Run cancellation prevented this case; denominator retained."
                    );
                }
                else
                    await RunCase(record);
            }
            catch (Exception error)
            {
                record.Failures.Add(error.ToString());
                record.Execution = "failed";
            }
            if (record.Execution == "running")
                record.Execution = record.Failures.Count == 0 ? "completed" : "failed";
            records.Add(record);
            output.WriteLine(Prefix + JsonSerializer.Serialize(record, JsonOptions));
        }
        var filled = records.Count(r => r.Id[0] == 'Q' && r.FilledTarget);
        output.WriteLine(
            "TRIPS_M2_DEMO_SUMMARY_V1:"
                + JsonSerializer.Serialize(
                    new
                    {
                        schemaVersion = 1,
                        records = records.Count,
                        q = 12,
                        sEdge = 6,
                        sNegative = 6,
                        filledQTargets = filled,
                        requiredFilledQTargets = 11,
                        failedCases = records.Count(r => r.Failures.Count != 0),
                        unrunCases = records.Count(r => r.Execution == "unrun"),
                        humanScores = (object?)null,
                        humanQuality = "not-evaluated",
                        asOfUtc = ClockText,
                    }
                )
        );
        records.Count.ShouldBe(24);
        records.SelectMany(r => r.Failures.Select(f => r.Id + ": " + f)).ShouldBeEmpty();
        filled.ShouldBeGreaterThanOrEqualTo(11);
    }

    private static async Task RunCase(CaseRecord r)
    {
        var root = FindRoot();
        var change = Path.Combine(
            root,
            "openspec/changes/archive/2026-10-22-trips-m2-moscow-itinerary"
        );
        var casesBytes = await File.ReadAllBytesAsync(
            Path.Combine(change, "quality-cases.json"),
            Ct
        );
        Require(Hash(casesBytes) == CaseHash, "Frozen original cases changed.");
        using var cases = JsonDocument.Parse(casesBytes);
        using var recipes = JsonDocument.Parse(
            await File.ReadAllBytesAsync(
                Path.Combine(change, "quality-demo-instantiations-v1.json"),
                Ct
            )
        );
        Require(Text(recipes.RootElement, "asOfUtc") == ClockText, "Recipe clock differs.");
        var original = cases
            .RootElement.GetProperty("cases")
            .EnumerateArray()
            .Single(c => Text(c, "id") == r.Id);
        var isQ = r.Id[0] == 'Q';
        var item = isQ
            ? original
            : recipes
                .RootElement.GetProperty("cases")
                .EnumerateArray()
                .Single(c => Text(c, "id") == r.Id);
        var brief = item.GetProperty("brief");
        r.RequestBrief = brief.Clone();
        r.SourceMode = isQ ? "real-reviewed-v1" : Text(item, "fixture");
        r.TargetStops = isQ ? item.GetProperty("targetStops").GetInt32() : null;
        var clock = new FrozenClock();
        var package = Path.Combine(AppContext.BaseDirectory, "Trips/Itineraries/moscow-outdoor/1");
        IItineraryFactSource source =
            r.SourceMode == "real-reviewed-v1"
                ? new CuratedMoscowFacts(
                    package,
                    clock,
                    expectedRelease: CuratedMoscowRelease.ReviewedV1
                )
                : new FictionalItinerarySource { Facts = FictionalFacts(item) };
        var counted = new CountedSource(source);
        CountedStore? writes = null;
        await using var fixture = await TripsHttpFixture.Start(
            itinerary: true,
            configure: s =>
            {
                writes = new CountedStore(
                    (ITripPlanStore)
                        s.Last(d => d.ServiceType == typeof(ITripPlanStore)).ImplementationInstance!
                );
                s.AddSingleton<ITripPlanStore>(writes);
                s.AddSingleton<IItineraryFactSource>(counted);
                s.AddSingleton<TimeProvider>(clock);
            }
        );
        var createBody = new JsonObject
        {
            ["title"] = "FICTIONAL Q_demo " + r.Id,
            ["brief"] = JsonNode.Parse(brief.GetRawText()),
        };
        using var created = await fixture.Client.PostAsync(
            "/api/trips",
            TripsHttpTests.Body(createBody.ToJsonString()),
            Ct
        );
        await Capture(r, "create", created);
        Require((int)created.StatusCode == 201, "M1 create failed.");
        var path = created.Headers.Location!.ToString();
        var before = fixture.Store.Rows.Values.Single();
        if (!isQ)
        {
            fixture.Client.DefaultRequestHeaders.Remove("X-Trips-Owner");
            fixture.Client.DefaultRequestHeaders.Add("X-Trips-Owner", Text(item, "requestOwner"));
        }
        var reads = fixture.Protection.Reads;
        var writesBefore = writes!.Writes;
        using var response = await TripsItineraryHttpTests.Candidate(
            fixture,
            path,
            isQ ? "\"1\"" : Text(item, "ifMatch")
        );
        var body = await Capture(r, "candidate", response);
        // Raw candidate bytes/status are retained before even JSON parsing or a semantic assertion.
        Check(r, writes.Writes == writesBefore, "Generation must invoke zero store writes.");
        r.Status = (int)response.StatusCode;
        r.BodyUtf8Base64 = Convert.ToBase64String(body);
        r.BodyLength = body.Length;
        r.BodySha256 = Hash(body);
        Check(
            r,
            fixture.Store.Rows.Values.Single() == before,
            "Generation must not write/change revision, snapshot or timestamps."
        );
        Check(r, fixture.Store.Rows.Count == 1, "Generation must not create plans.");
        Check(
            r,
            r.Status == (isQ ? 200 : item.GetProperty("expectedStatus").GetInt32()),
            "HTTP status expectation."
        );
        using var parsed = JsonDocument.Parse(body);
        var value = parsed.RootElement;
        if (r.Status != 200)
        {
            r.Outcome = "problem";
            Check(
                r,
                !isQ && Text(value, "code") == Text(item, "expectedReason"),
                "Problem code expectation."
            );
            Check(r, counted.Calls == 0, "Owner/revision guard must precede source loading.");
            if (r.Id == "S07")
                Check(r, fixture.Protection.Reads == reads, "Foreign owner must not decrypt.");
            return;
        }
        r.Outcome = Text(value, "outcome");
        Check(
            r,
            r.Outcome == (isQ ? "candidate" : Text(item, "expectedOutcome")),
            "Outcome expectation."
        );
        Check(r, Text(value, "baseRevision") == "1", "Exact base revision.");
        Check(
            r,
            DateTimeOffset.Parse(Text(value, "generatedAt"), CultureInfo.InvariantCulture) == Now,
            "Frozen generatedAt."
        );
        if (r.Outcome == "unsupported")
        {
            Check(
                r,
                !isQ && Text(value, "reason") == Text(item, "expectedReason"),
                "Unsupported reason."
            );
            Check(
                r,
                counted.Calls == 0 && !value.TryGetProperty("days", out _),
                "Unsupported must not load facts or return a candidate."
            );
            return;
        }
        var factsResult = await source.ReadAsync(Ct);
        Require(!factsResult.IsError, "Strict source read failed.");
        var facts = factsResult.Value;
        if (isQ)
            Check(
                r,
                facts.Pois.Count == 20 && facts.Edges.Count == 90,
                "Reviewed real 20 POI/90 paths."
            );
        VerifyComputed(r, value, facts, brief, isQ ? package : null);
        if (!isQ)
        {
            if (item.GetProperty("expectedUnmetFood").GetBoolean())
                Check(
                    r,
                    Strings(value, "unmetInterests").Contains("food")
                        && Text(value, "coverage") == "partial",
                    "Explicit unmet food/partial."
                );
            if (item.GetProperty("expectedUnknownHours").GetBoolean())
                Check(
                    r,
                    Strings(value, "uncertainties").Contains("openingHoursUnknown"),
                    "Explicit unknown hours."
                );
            if (item.GetProperty("maximumStops").ValueKind == JsonValueKind.Number)
                Check(
                    r,
                    r.StopCount <= item.GetProperty("maximumStops").GetInt32(),
                    "Predeclared eligible stop upper bound."
                );
            if (item.GetProperty("expectNoEdges").GetBoolean())
                Check(
                    r,
                    value
                        .GetProperty("days")
                        .EnumerateArray()
                        .All(d => d.GetProperty("edges").GetArrayLength() == 0)
                        && Text(value, "coverage") == "partial",
                    "Expired/disconnected/single eligible cannot invent walking adjacency."
                );
            var closed = Strings(item, "closedPoiIds");
            Check(
                r,
                !r.PoiIds.Intersect(closed).Any(),
                "Known closed POI must not appear as open."
            );
        }
        if (r.Id == "Q01")
            await SaveReload(r, fixture, path, createBody, value);
    }

    private static void VerifyComputed(
        CaseRecord r,
        JsonElement v,
        ValidatedItineraryFacts facts,
        JsonElement brief,
        string? package
    )
    {
        Check(
            r,
            Text(v, "datasetId") == facts.DatasetId
                && Text(v, "datasetVersion") == facts.Version
                && Text(v, "datasetHash") == facts.DatasetHash,
            "Bound dataset identity."
        );
        Check(
            r,
            Text(v, "policyVersion") == ItineraryPolicy.PolicyVersion
                && Text(v, "dataMode") == "osm-modelled-demo"
                && Text(v, "humanQuality") == "not-evaluated",
            "Policy/demo/human quality labels."
        );
        var prep = v.GetProperty("preparation");
        Check(
            r,
            prep.GetProperty("humanCurator").ValueKind == JsonValueKind.Null
                && prep.GetProperty("humanReviewer").ValueKind == JsonValueKind.Null
                && Text(prep, "calibrationStatus") == "not-evaluated",
            "No fabricated human actors/calibration."
        );
        var model = v.GetProperty("modelSource");
        Check(
            r,
            Text(model, "sourceUrl") == facts.ModelSource.SourceUrl
                && Text(model, "inputSha256") == facts.ModelSource.InputSha256
                && Text(model, "publicationInputSha256")
                    == facts.ModelSource.PublicationInputSha256,
            "Model source/input identities."
        );
        foreach (
            var name in new[]
            {
                "snapshotPublishedAtUtc",
                "downloadedAtUtc",
                "buildAsOfUtc",
                "validUntil",
            }
        )
        {
            var expected = name switch
            {
                "snapshotPublishedAtUtc" => facts.ModelSource.SnapshotPublishedAtUtc,
                "downloadedAtUtc" => facts.ModelSource.DownloadedAtUtc,
                "buildAsOfUtc" => facts.ModelSource.BuildAsOfUtc,
                _ => facts.ModelSource.ValidUntil,
            };
            Check(
                r,
                DateTimeOffset.Parse(Text(model, name), CultureInfo.InvariantCulture) == expected,
                "Model " + name
            );
        }
        Check(
            r,
            Text(model, "profileVersion") == "profile-v1"
                && Text(model, "pairSelectorVersion") == "pair-selector-v1"
                && Text(model, "modelVersion") == "model-v1"
                && Text(model, "calibrationStatus") == "not-evaluated",
            "Profile/selector/model/calibration."
        );
        Check(
            r,
            Text(model, "assumptions")
                == Travel.Modules.Trips.Api.Contracts.ItineraryCandidateResponseEncoder.Assumptions,
            "Uncalibrated model assumptions."
        );
        Check(
            r,
            Text(v, "attribution").Contains("OpenStreetMap contributors")
                && Text(v, "attribution").Contains("https://opendatacommons.org/licenses/odbl/1-0/")
                && Text(v, "derivedDataOffer")
                    == "/trips-data/moscow-outdoor/1/moscow-v1.manifest.json",
            "Rights/attribution/reproduction offer."
        );
        Check(
            r,
            Now >= facts.BuildAsOfUtc
                && Now < facts.ReleaseValidUntil
                && DateTimeOffset.Parse(Text(v, "expiresAt"), CultureInfo.InvariantCulture)
                    <= Now.AddMinutes(15),
            "Fresh generation/expiry."
        );
        Check(
            r,
            new[] { "walkingModelled", "calibrationNotEvaluated", "humanQualityNotEvaluated" }.All(
                Strings(v, "uncertainties").Contains
            ),
            "Uncertainties are visible."
        );
        Check(
            r,
            new[]
            {
                "indoorReservationsNotPlanned",
                "interdayTransfersNotPlanned",
                "arrivalScheduleNotPlanned",
            }.All(Strings(v, "exclusions").Contains),
            "Scope exclusions visible."
        );
        var days = v.GetProperty("days").EnumerateArray().ToArray();
        var start = DateOnly.Parse(Text(brief, "startDate"), CultureInfo.InvariantCulture);
        var dayCount =
            DateOnly.Parse(Text(brief, "endDate"), CultureInfo.InvariantCulture).DayNumber
            - start.DayNumber
            + 1;
        Check(r, days.Length == dayCount, "Exact requested day count.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var blockIds = new HashSet<Guid>();
        var evidence = v.GetProperty("evidence").EnumerateArray().ToDictionary(e => Text(e, "id"));
        var usedEvidence = new HashSet<string>();
        using var pathsDocument = package is null
            ? null
            : JsonDocument.Parse(File.ReadAllBytes(Path.Combine(package, "walking-evidence.json")));
        var paths = pathsDocument
            ?.RootElement.GetProperty("paths")
            .EnumerateArray()
            .ToDictionary(p => Text(p, "pathId"));
        for (var dayIndex = 0; dayIndex < days.Length; dayIndex++)
        {
            var day = days[dayIndex];
            var date = DateOnly.Parse(Text(day, "date"), CultureInfo.InvariantCulture);
            Check(r, date == start.AddDays(dayIndex), "Consecutive requested dates.");
            var blocks = day.GetProperty("blocks").EnumerateArray().ToArray();
            r.StopsPerDay.Add(blocks.Length);
            foreach (var block in blocks)
            {
                var poiId = Text(block, "poiId");
                r.PoiIds.Add(poiId);
                var poi = facts.Pois.Single(p => p.Id == poiId);
                Check(
                    r,
                    ids.Add(poiId) && blockIds.Add(Guid.Parse(Text(block, "proposedBlockId"))),
                    "Distinct POIs/stable UUID proposals."
                );
                Check(
                    r,
                    Text(block, "title") == poi.Title
                        && block.GetProperty("latitude").GetDecimal() == poi.Latitude
                        && block.GetProperty("longitude").GetDecimal() == poi.Longitude
                        && Text(block, "wikidataEntityId") == poi.WikidataEntityId
                        && Text(block, "wikidataRevision") == poi.WikidataRevision,
                    "Exact sourced identity/coordinates."
                );
                Check(
                    r,
                    Text(block, "date") == Text(day, "date")
                        && new[] { "startTime", "endTime", "estimate" }.All(n =>
                            block.GetProperty(n).ValueKind == JsonValueKind.Null
                        ),
                    "No invented times/prices."
                );
                Check(
                    r,
                    Text(block, "note")
                        .StartsWith("Historical proposal:", StringComparison.Ordinal),
                    "Explicit historical note."
                );
                foreach (var fact in block.GetProperty("facts").EnumerateArray())
                {
                    if (Text(fact, "state") == "known")
                    {
                        var eid = Text(fact, "evidenceId");
                        Check(
                            r,
                            Text(fact, "kind") == "identity-location"
                                ? eid == poi.IdentityEvidenceId
                                : eid == poi.Access.EvidenceId,
                            "Fact evidence belongs to POI."
                        );
                        VerifyEvidence(eid, poiId, date);
                    }
                    else
                        Check(
                            r,
                            Text(fact, "kind") == "access"
                                && fact.GetProperty("evidenceId").ValueKind == JsonValueKind.Null
                                && block.GetProperty("openingIntervals").GetArrayLength() == 0
                                && Text(fact, "unknownReason")
                                    .StartsWith("openingHours", StringComparison.Ordinal),
                            "Access remains unknown, never open."
                        );
                }
            }
            var edges = day.GetProperty("edges").EnumerateArray().ToArray();
            Check(
                r,
                edges.Length == Math.Max(0, blocks.Length - 1),
                "One directed edge per consecutive stop pair."
            );
            for (var index = 0; index < edges.Length; index++)
            {
                var edge = edges[index];
                var source = facts.Edges.Single(e => e.Id == Text(edge, "edgeId"));
                r.EdgeIds.Add(source.Id);
                var fact = facts.Evidence.Single(e => e.Id == source.EvidenceId);
                var walk = fact.ModelledWalk!;
                var p = edge.GetProperty("modelledWalk");
                Check(
                    r,
                    Text(edge, "fromPoiId") == source.FromPoiId
                        && Text(edge, "toPoiId") == source.ToPoiId
                        && source.FromPoiId == Text(blocks[index], "poiId")
                        && source.ToPoiId == Text(blocks[index + 1], "poiId"),
                    "Directed consecutive source adjacency."
                );
                Check(
                    r,
                    Text(edge, "evidenceId") == source.EvidenceId
                        && Text(edge, "method") == "modelled"
                        && Text(edge, "rightsBasis") == "odbl"
                        && Text(edge, "restrictions") == source.Restrictions,
                    "Walking evidence/rights/restrictions."
                );
                VerifyEvidence(source.EvidenceId, source.Id, date);
                var length = long.Parse(
                    Text(p, "pathLengthMillimetres"),
                    CultureInfo.InvariantCulture
                );
                var crossings = p.GetProperty("crossingCount").GetInt32();
                var minimum = Math.Max(1L, (length + 74999) / 75000);
                var maximum = Math.Max(minimum, (length + 44999) / 45000 + crossings);
                Check(
                    r,
                    edge.GetProperty("minimumMinutes").GetInt64() == minimum
                        && edge.GetProperty("maximumMinutes").GetInt64() == maximum
                        && length == walk.PathLengthMillimetres
                        && crossings == walk.CrossingCount,
                    "Independent integer 75/45 metres formula."
                );
                Check(
                    r,
                    Text(p, "pathId") == walk.PathId
                        && Text(p, "pathHash") == walk.PathHash
                        && Text(p, "fromAnchorNodeId")
                            == walk.FromAnchorNodeId.ToString(CultureInfo.InvariantCulture)
                        && Text(p, "toAnchorNodeId")
                            == walk.ToAnchorNodeId.ToString(CultureInfo.InvariantCulture)
                        && Text(p, "modelVersion") == "model-v1"
                        && Text(p, "calibrationStatus") == "not-evaluated",
                    "Path/anchors/model identity."
                );
                if (paths is not null)
                {
                    var path = paths[walk.PathId];
                    var nodes = path.GetProperty("nodeIds")
                        .EnumerateArray()
                        .Select(n => n.GetInt64())
                        .ToArray();
                    var ways = path.GetProperty("wayIds")
                        .EnumerateArray()
                        .Select(n => n.GetInt64())
                        .ToArray();
                    Check(
                        r,
                        Hash(Encoding.UTF8.GetBytes(path.GetRawText() + "\n")) == walk.PathHash
                            && Text(path, "fromPoiId") == source.FromPoiId
                            && Text(path, "toPoiId") == source.ToPoiId,
                        "Exact retained directed path bytes/hash."
                    );
                    Check(
                        r,
                        nodes.Length > 0
                            && ways.Length == nodes.Length - 1
                            && nodes.All(n => n > 0)
                            && ways.All(n => n > 0)
                            && nodes.Distinct().Count() == nodes.Length
                            && nodes[0] == walk.FromAnchorNodeId
                            && nodes[^1] == walk.ToAnchorNodeId,
                        "Retained ordered node/way sequence and anchors; offline eligibility is bound D_demo evidence."
                    );
                }
            }
        }
        r.StopCount = r.StopsPerDay.Sum();
        Check(
            r,
            usedEvidence.SetEquals(evidence.Keys),
            "Exactly all referenced evidence, no invented extra facts."
        );
        if (r.TargetStops is { } target)
            r.FilledTarget =
                days.Length == dayCount
                && r.StopsPerDay.All(n => n == target / dayCount)
                && r.StopCount == target
                && ids.Count == target;

        void VerifyEvidence(string id, string scope, DateOnly date)
        {
            usedEvidence.Add(id);
            var actual = evidence[id];
            var original = facts.Evidence.Single(e => e.Id == id);
            Check(
                r,
                original.ScopeId == scope
                    && Text(actual, "scopeId") == scope
                    && Text(actual, "sourceUrl") == original.SourceUrl
                    && Text(actual, "rightsEvidence") == original.RightsEvidence,
                "Evidence source/scope/rights reference."
            );
            Check(
                r,
                original.IsKnownAt(Now)
                    && original.IsKnownOn(date, facts.Destination)
                    && actual.GetProperty("observedAt").ValueKind == JsonValueKind.Null
                    && original.ObservedAt is null,
                "Evidence valid at frozen clock/on local date; no invented observation."
            );
            Check(
                r,
                DateTimeOffset.Parse(Text(actual, "validUntil"), CultureInfo.InvariantCulture)
                    == original.ValidUntil
                    && DateTimeOffset.Parse(
                        Text(actual, "retrievedAtUtc"),
                        CultureInfo.InvariantCulture
                    ) == original.RetrievedAtUtc
                    && DateTimeOffset.Parse(
                        Text(actual, "processedAtUtc"),
                        CultureInfo.InvariantCulture
                    ) == original.ProcessedAtUtc,
                "Exact evidence freshness timestamps."
            );
        }
    }

    private static ValidatedItineraryFacts FictionalFacts(JsonElement recipe)
    {
        var input = FictionalItineraryFacts.Input();
        var closed = Strings(recipe, "closedPoiIds").ToHashSet(StringComparer.Ordinal);
        if (closed.Count > 0)
        {
            var extra = input
                .Pois.Where(p => closed.Contains(p.Id))
                .Select(p =>
                    FictionalItineraryFacts
                        .EvidenceParameters("access-" + p.Id, ItineraryFactKind.Access, p.Id)
                        .Build()
                        .Value
                );
            input = input with
            {
                Pois = input
                    .Pois.Select(p =>
                        closed.Contains(p.Id)
                            ? FictionalItineraryFacts.Poi(
                                p.Id,
                                access: ItineraryAccess
                                    .Create(p.Id, true, "access-" + p.Id, [], [])
                                    .Value
                            )
                            : p
                    )
                    .ToArray(),
                Evidence = input.Evidence.Concat(extra).ToArray(),
            };
        }
        var built = input.Build();
        Require(!built.IsError, "Invalid predeclared fictional S fixture.");
        return built.Value;
    }

    private static async Task SaveReload(
        CaseRecord r,
        TripsHttpFixture fixture,
        string path,
        JsonObject create,
        JsonElement candidate
    )
    {
        var edit = (JsonObject)create.DeepClone();
        var days = new JsonArray();
        foreach (var day in candidate.GetProperty("days").EnumerateArray())
        {
            var blocks = new JsonArray();
            foreach (var b in day.GetProperty("blocks").EnumerateArray())
                blocks.Add(
                    new JsonObject
                    {
                        ["blockId"] = Text(b, "proposedBlockId"),
                        ["title"] = Text(b, "title"),
                        ["note"] = Text(b, "note") + " Local edit.",
                        ["startTime"] = null,
                        ["endTime"] = null,
                        ["estimate"] = null,
                    }
                );
            days.Add(new JsonObject { ["date"] = Text(day, "date"), ["blocks"] = blocks });
        }
        edit["days"] = days;
        using var saved = await TripsHttpTests.Put(
            fixture.Client,
            path,
            edit.ToJsonString(),
            "\"1\""
        );
        await Capture(r, "explicit-save", saved);
        Check(r, (int)saved.StatusCode == 200, "Explicit ordinary M1 PUT accepted.");
        using var reload = await fixture.Client.GetAsync(path, Ct);
        var bytes = await Capture(r, "reload", reload);
        Check(r, (int)reload.StatusCode == 200, "Ordinary M1 GET reload accepted.");
        var value = JsonNode.Parse(bytes)!.AsObject();
        Check(
            r,
            JsonNode.DeepEquals(value["brief"], create["brief"])
                && value["title"]!.GetValue<string>() == create["title"]!.GetValue<string>(),
            "Save preserves brief/title."
        );
        Check(
            r,
            value["revision"]!.GetValue<string>() == "2",
            "Explicit save changes revision exactly once."
        );
        var persisted = value["days"]!.AsArray().SelectMany(d => d!["blocks"]!.AsArray()).ToArray();
        var proposed = days.SelectMany(d => d!["blocks"]!.AsArray()).ToArray();
        Check(r, persisted.Length == proposed.Length, "Reload preserves block count.");
        for (var i = 0; i < persisted.Length; i++)
        {
            var b = persisted[i]!;
            Check(
                r,
                b["blockId"]!.GetValue<string>() == proposed[i]!["blockId"]!.GetValue<string>()
                    && b["note"]!.GetValue<string>() == proposed[i]!["note"]!.GetValue<string>(),
                "Stable UUID and locally edited historical note survive M1 codec."
            );
            Check(
                r,
                b["startTime"] is null
                    && b["endTime"] is null
                    && b["estimate"] is null
                    && b["facts"] is null
                    && b["poiId"] is null,
                "Reload retains intentions/null defaults, no trusted preview facts."
            );
        }
        r.SaveReloadChecked = true;
    }

    private static async Task<byte[]> Capture(
        CaseRecord r,
        string stage,
        HttpResponseMessage response
    )
    {
        var bytes = await response.Content.ReadAsByteArrayAsync(Ct);
        r.Exchanges.Add(
            new(
                stage,
                (int)response.StatusCode,
                Convert.ToBase64String(bytes),
                bytes.Length,
                Hash(bytes)
            )
        );
        return bytes;
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Travel.slnx")))
            directory = directory.Parent;
        return directory?.FullName
            ?? throw new InvalidOperationException("No repository input root.");
    }

    private static string Text(JsonElement v, string name) =>
        v.GetProperty(name).GetString()
        ?? throw new InvalidOperationException(name + " must be text.");

    private static string[] Strings(JsonElement v, string name) =>
        v.GetProperty(name).EnumerateArray().Select(x => x.GetString()!).ToArray();

    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static void Check(CaseRecord r, bool condition, string message)
    {
        r.Checks++;
        if (!condition)
            r.Failures.Add(message);
    }

    private sealed class FrozenClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class CountedSource(IItineraryFactSource inner) : IItineraryFactSource
    {
        internal int Calls { get; private set; }

        public Task<ErrorOr<ValidatedItineraryFacts>> ReadAsync(
            CancellationToken cancellationToken = default
        )
        {
            Calls++;
            return inner.ReadAsync(cancellationToken);
        }
    }

    private sealed class CountedStore(ITripPlanStore inner) : ITripPlanStore
    {
        internal int Writes { get; private set; }

        public Task<ErrorOr<TripPlanRecord?>> LoadAsync(
            OwnerIdentity owner,
            Guid id,
            CancellationToken ct
        ) => inner.LoadAsync(owner, id, ct);

        public Task<ErrorOr<TripPlanRecordPage>> ListAsync(
            OwnerIdentity owner,
            TripPlanPosition? after,
            int limit,
            CancellationToken ct
        ) => inner.ListAsync(owner, after, limit, ct);

        public Task<ErrorOr<Success>> TryCreateAsync(
            TripPlanRecord row,
            int cap,
            CancellationToken ct
        )
        {
            Writes++;
            return inner.TryCreateAsync(row, cap, ct);
        }

        public Task<ErrorOr<Success>> TryReplaceAsync(
            long expected,
            TripPlanRecord row,
            CancellationToken ct
        )
        {
            Writes++;
            return inner.TryReplaceAsync(expected, row, ct);
        }

        public Task<ErrorOr<Success>> TryDeleteAsync(
            OwnerIdentity owner,
            Guid id,
            long expected,
            CancellationToken ct
        )
        {
            Writes++;
            return inner.TryDeleteAsync(owner, id, expected, ct);
        }
    }

    private sealed record Exchange(
        string Stage,
        int Status,
        string BodyUtf8Base64,
        int BodyLength,
        string BodySha256
    );

    private sealed class CaseRecord(string id, string family)
    {
        public int SchemaVersion => 1;
        public string Id { get; } = id;
        public string Family { get; } = family;
        public string AsOfUtc => ClockText;
        public string CaseSetSha256 => CaseHash;
        public string Execution { get; set; } = "running";
        public JsonElement? RequestBrief { get; set; }
        public string? SourceMode { get; set; }
        public int? Status { get; set; }
        public string? BodyUtf8Base64 { get; set; }
        public int? BodyLength { get; set; }
        public string? BodySha256 { get; set; }
        public string? Outcome { get; set; }
        public int? TargetStops { get; set; }
        public int StopCount { get; set; }
        public List<int> StopsPerDay { get; } = [];
        public bool FilledTarget { get; set; }
        public List<string> PoiIds { get; } = [];
        public List<string> EdgeIds { get; } = [];
        public List<Exchange> Exchanges { get; } = [];
        public List<string> Failures { get; } = [];
        public int Checks { get; set; }
        public bool SaveReloadChecked { get; set; }
        public object? HumanScores => null;
        public string HumanQuality => "not-evaluated";
    }
}
