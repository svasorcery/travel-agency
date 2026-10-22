using Shouldly;
using Travel.Modules.Trips.Core;
using Travel.Modules.Trips.Core.Itineraries;

namespace Travel.Modules.Trips.Tests.Unit;

// FICTIONAL structural fixtures only; no real package or frozen quality cases.
public sealed class ItineraryCandidateValidatorTests
{
    private static readonly DateTimeOffset Now = ItineraryTestFacts.Now;
    private static readonly DateOnly Today = DateOnly.FromDateTime(Now.DateTime);

    [Theory]
    [InlineData("relaxed", 1)]
    [InlineData("balanced", 2)]
    [InlineData("active", 3)]
    public void Valid_original_returns_the_same_whole_object(string pace, int days)
    {
        var brief = Brief(pace, days, ["history", "nature"]);
        var facts = ItineraryTestFacts.Create();
        var outcome = Generate(brief, facts);
        var result = Validate(brief, facts, outcome);
        result.IsError.ShouldBeFalse();
        result.Value.ShouldBeSameAs(outcome);
    }

    [Theory]
    [InlineData("kind")]
    [InlineData("coverage")]
    [InlineData("dataset")]
    [InlineData("version")]
    [InlineData("hash")]
    [InlineData("policy")]
    [InlineData("generated")]
    [InlineData("generatedOffset")]
    [InlineData("expiry")]
    [InlineData("destination")]
    [InlineData("label")]
    [InlineData("zone")]
    [InlineData("catalogue")]
    [InlineData("preparation")]
    [InlineData("model")]
    [InlineData("nullDestination")]
    [InlineData("nullPreparation")]
    [InlineData("nullModel")]
    [InlineData("nullDataset")]
    [InlineData("daysMissing")]
    [InlineData("extraDay")]
    [InlineData("wrongDate")]
    [InlineData("nullDay")]
    [InlineData("covered")]
    [InlineData("unmet")]
    [InlineData("duplicateCovered")]
    [InlineData("unknownReason")]
    [InlineData("hiddenUnknown")]
    [InlineData("duplicateUncertainty")]
    [InlineData("exclusion")]
    [InlineData("falseFilled")]
    [InlineData("emptyKind")]
    [InlineData("unsupportedKind")]
    [InlineData("negativeCounter")]
    [InlineData("oversizeCounter")]
    [InlineData("falseLimit")]
    [InlineData("counterAtCapWithoutLimit")]
    [InlineData("nullReason")]
    public void Rejects_forged_global_claims_as_unavailable(string mutation)
    {
        var brief = Brief(interests: ["food", "history"], origin: "kaliningrad", budget: "1000");
        var facts = ItineraryTestFacts.Create();
        var good = Generate(brief, facts);
        var edit = new Output(good);
        edit = mutation switch
        {
            "kind" => edit with { Kind = (ItineraryOutcomeKind)99 },
            "coverage" => edit with { Coverage = (ItineraryCoverage)99 },
            "dataset" => edit with { DatasetId = "other" },
            "version" => edit with { Version = "other" },
            "hash" => edit with { Hash = new string('b', 64) },
            "policy" => edit with { Policy = "other" },
            "generated" => edit with { Generated = Now.AddSeconds(1) },
            "generatedOffset" => edit with { Generated = Now.ToOffset(TimeSpan.FromHours(3)) },
            "expiry" => edit with { Expires = good.ExpiresAt.AddTicks(1) },
            "destination" => edit with
            {
                Destination = good.Destination with { DestinationId = "kaliningrad" },
            },
            "label" => edit with { Destination = good.Destination with { DisplayName = "Forged" } },
            "zone" => edit with
            {
                Destination = good.Destination with { TimeZone = "Europe/Kaliningrad" },
            },
            "catalogue" => edit with
            {
                Destination = good.Destination with { CatalogueVersion = "2" },
            },
            "preparation" => edit with
            {
                Preparation = ItineraryDemoPreparation
                    .Create(
                        "osm-modelled-demo",
                        "another-builder",
                        null,
                        null,
                        "not-evaluated",
                        "not-evaluated",
                        Now
                    )
                    .Value,
            },
            "model" => edit with
            {
                Model = (
                    ItineraryTestFacts.SourceInput() with
                    {
                        InputSha256 = new string('c', 64),
                        PublicationInputSha256 = new string('c', 64),
                    }
                )
                    .Build()
                    .Value,
            },
            "nullDestination" => edit with { Destination = null! },
            "nullPreparation" => edit with { Preparation = null! },
            "nullModel" => edit with { Model = null! },
            "nullDataset" => edit with { DatasetId = null! },
            "daysMissing" => edit with { Days = [] },
            "extraDay" => edit with { Days = [.. good.Days, good.Days[0]] },
            "wrongDate" => edit with
            {
                Days = [new(Today.AddDays(1), good.Days[0].Blocks, good.Days[0].Edges)],
            },
            "nullDay" => edit with { Days = [null!] },
            "covered" => edit with { Covered = ["food", "history"] },
            "unmet" => edit with { Unmet = [] },
            "duplicateCovered" => edit with { Covered = ["history", "history"] },
            "unknownReason" => edit with { Reasons = ["invented"] },
            "hiddenUnknown" => edit with
            {
                Uncertainties = good.Uncertainties.Where(x => x != "openingHoursUnknown").ToArray(),
            },
            "duplicateUncertainty" => edit with
            {
                Uncertainties = [.. good.Uncertainties, "walkingModelled"],
            },
            "exclusion" => edit with
            {
                Exclusions = good.Exclusions.Where(x => x != "totalCostUnknown").ToArray(),
            },
            "falseFilled" => edit with { Coverage = ItineraryCoverage.Filled },
            "emptyKind" => edit with { Kind = ItineraryOutcomeKind.Empty },
            "unsupportedKind" => edit with { Kind = ItineraryOutcomeKind.Unsupported },
            "negativeCounter" => edit with { Counter = -1 },
            "oversizeCounter" => edit with { Counter = 20001 },
            "falseLimit" => edit with { Limited = true },
            "counterAtCapWithoutLimit" => edit with { Counter = 20000 },
            "nullReason" => edit with { Reasons = [null!] },
            _ => throw new InvalidOperationException(),
        };
        Reject(brief, facts, edit.Build());
    }

    [Theory]
    [InlineData("poi")]
    [InlineData("uuid")]
    [InlineData("duplicateUuid")]
    [InlineData("duplicatePoi")]
    [InlineData("title")]
    [InlineData("date")]
    [InlineData("start")]
    [InlineData("end")]
    [InlineData("money")]
    [InlineData("note")]
    [InlineData("oversizeNote")]
    [InlineData("nullNote")]
    [InlineData("nullTitle")]
    [InlineData("nullPoi")]
    [InlineData("nullBlock")]
    [InlineData("nullFact")]
    [InlineData("missingFact")]
    [InlineData("duplicateFact")]
    [InlineData("identityScope")]
    [InlineData("identityUnknown")]
    [InlineData("unknownEnum")]
    [InlineData("unknownState")]
    [InlineData("hiddenAccess")]
    [InlineData("unknownAccessReason")]
    [InlineData("unknownAccessEvidence")]
    [InlineData("inventedIntervals")]
    [InlineData("nullInterval")]
    [InlineData("tooManyStops")]
    public void Rejects_corrupt_stops_without_partial_repair(string mutation)
    {
        var brief = Brief();
        var facts = ItineraryTestFacts.Create();
        var good = Generate(brief, facts);
        var original = good.Days[0].Blocks[0];
        var edit = new Block(original);
        edit = mutation switch
        {
            "poi" => edit with { Poi = "fabricated" },
            "uuid" => edit with { Id = Guid.Empty },
            "duplicateUuid" => edit with { Id = good.Days[0].Blocks[1].ProposedBlockId },
            "duplicatePoi" => edit with { Poi = good.Days[0].Blocks[1].PoiId },
            "title" => edit with { Title = "Fabricated title" },
            "date" => edit with { Date = Today.AddDays(1) },
            "start" => edit with { Start = "10:00" },
            "end" => edit with { End = "11:00" },
            "money" => edit with { Estimate = PlanningMoney.Create("1", "RUB").Value },
            "note" => edit with
            {
                Note = original.Note + " Guaranteed feasible and within budget.",
            },
            "oversizeNote" => edit with { Note = new string('a', 501) },
            "nullNote" => edit with { Note = null! },
            "nullTitle" => edit with { Title = null! },
            "nullPoi" => edit with { Poi = null! },
            "nullFact" => edit with { Facts = [null!, original.Facts[1]] },
            "missingFact" => edit with { Facts = [original.Facts[0]] },
            "duplicateFact" => edit with { Facts = [original.Facts[0], original.Facts[0]] },
            "identityScope" => edit with
            {
                Facts =
                [
                    new(
                        ItineraryFactKind.IdentityLocation,
                        ItineraryFactState.Known,
                        good.Days[0].Blocks[1].Facts[0].EvidenceId,
                        null
                    ),
                    original.Facts[1],
                ],
            },
            "identityUnknown" => edit with
            {
                Facts =
                [
                    new(
                        ItineraryFactKind.IdentityLocation,
                        ItineraryFactState.Unknown,
                        null,
                        "unknown"
                    ),
                    original.Facts[1],
                ],
            },
            "unknownEnum" => edit with
            {
                Facts =
                [
                    new(
                        (ItineraryFactKind)99,
                        ItineraryFactState.Known,
                        original.Facts[0].EvidenceId,
                        null
                    ),
                    original.Facts[1],
                ],
            },
            "unknownState" => edit with
            {
                Facts =
                [
                    original.Facts[0],
                    new(
                        ItineraryFactKind.Access,
                        (ItineraryFactState)99,
                        null,
                        "openingHoursUnknown"
                    ),
                ],
            },
            "hiddenAccess" => edit with
            {
                Facts =
                [
                    original.Facts[0],
                    new(ItineraryFactKind.Access, ItineraryFactState.Known, null, null),
                ],
            },
            "unknownAccessReason" => edit with
            {
                Facts =
                [
                    original.Facts[0],
                    new(ItineraryFactKind.Access, ItineraryFactState.Unknown, null, "fabricated"),
                ],
            },
            "unknownAccessEvidence" => edit with
            {
                Facts =
                [
                    original.Facts[0],
                    new(
                        ItineraryFactKind.Access,
                        ItineraryFactState.Unknown,
                        "identity-p0",
                        "openingHoursUnknown"
                    ),
                ],
            },
            "inventedIntervals" => edit with
            {
                Intervals = [ItineraryWeeklyInterval.Create(Today.DayOfWeek, 0, 1440).Value],
            },
            "nullInterval" => edit with { Intervals = [null!] },
            _ => edit,
        };
        var blocks = good.Days[0].Blocks.ToArray();
        blocks[0] = mutation == "nullBlock" ? null! : edit.Build();
        if (mutation == "tooManyStops")
            blocks = [.. blocks, blocks[0]];
        Reject(
            brief,
            facts,
            new Output(good) { Days = [new(Today, blocks, good.Days[0].Edges)] }.Build()
        );
    }

    [Theory]
    [InlineData("id")]
    [InlineData("reverse")]
    [InlineData("min")]
    [InlineData("max")]
    [InlineData("evidence")]
    [InlineData("restriction")]
    [InlineData("method")]
    [InlineData("rights")]
    [InlineData("path")]
    [InlineData("hash")]
    [InlineData("length")]
    [InlineData("crossings")]
    [InlineData("anchor")]
    [InlineData("modelSource")]
    [InlineData("nullModel")]
    [InlineData("nullEdge")]
    [InlineData("missing")]
    [InlineData("duplicate")]
    public void Rejects_invented_directed_edges_and_model_provenance(string mutation)
    {
        var brief = Brief();
        var facts = ItineraryTestFacts.Create();
        var good = Generate(brief, facts);
        var e = good.Days[0].Edges[0];
        var m = e.ModelledWalk;
        var path = new ItineraryTestFacts.WalkInput(
            m.Source,
            m.FromPoiId,
            m.ToPoiId,
            m.PathId,
            m.PathHash,
            m.PathLengthMillimetres,
            m.CrossingCount,
            m.FromAnchorNodeId,
            m.ToAnchorNodeId
        );
        path = mutation switch
        {
            "path" => path with { PathId = "forged-path" },
            "hash" => path with { PathHash = new string('f', 64) },
            "length" => path with { PathLengthMillimetres = m.PathLengthMillimetres + 1 },
            "crossings" => path with { CrossingCount = m.CrossingCount + 1 },
            "anchor" => path with { FromAnchorNodeId = 1000 },
            "modelSource" => path with
            {
                Source = (
                    ItineraryTestFacts.SourceInput() with
                    {
                        SourceUrl = "https://other.example.test/osm.gz",
                    }
                )
                    .Build()
                    .Value,
            },
            _ => path,
        };
        var edge = new ItineraryCandidateEdge(
            mutation == "id" ? "invented" : e.EdgeId,
            mutation == "reverse" ? e.ToPoiId : e.FromPoiId,
            mutation == "reverse" ? e.FromPoiId : e.ToPoiId,
            mutation == "min" ? e.MinimumMinutes + 1 : e.MinimumMinutes,
            mutation == "max" ? e.MaximumMinutes - 1 : e.MaximumMinutes,
            mutation == "evidence" ? "identity-p0" : e.EvidenceId,
            mutation == "restriction" ? "Guaranteed accessible" : e.Restrictions,
            mutation == "method" ? ItineraryFactMethod.Observed : e.Method,
            mutation == "rights" ? ItineraryRightsBasis.OriginalObservation : e.RightsBasis,
            mutation == "nullModel" ? null! : path.Build().Value
        );
        var edges = good.Days[0].Edges.ToArray();
        edges[0] = mutation == "nullEdge" ? null! : edge;
        if (mutation == "missing")
            edges = edges.Skip(1).ToArray();
        if (mutation == "duplicate")
            edges = [.. edges, edge];
        Reject(
            brief,
            facts,
            new Output(good) { Days = [new(Today, good.Days[0].Blocks, edges)] }.Build()
        );
    }

    [Fact]
    public void Null_output_is_unavailable_instead_of_an_exception() =>
        Reject(Brief(), ItineraryTestFacts.Create(), null!);

    [Theory]
    [InlineData("open")]
    [InlineData("closed")]
    [InlineData("expired")]
    public void Source_access_is_recomputed_and_truthful_results_remain_valid(string state)
    {
        var input = WithAccess(state);
        var facts = input.Build().Value;
        var brief = Brief(start: state == "expired" ? Today.AddDays(6) : Today);
        var outcome = Generate(brief, facts);
        if (state == "closed")
            outcome.Kind.ShouldBe(ItineraryOutcomeKind.Empty);
        else if (state == "expired")
            outcome.Uncertainties.ShouldContain("openingHoursUnknown");
        else
            outcome
                .Days.SelectMany(x => x.Blocks)
                .ShouldAllBe(x => x.Facts[1].State == ItineraryFactState.Known);
        Validate(brief, facts, outcome).IsError.ShouldBeFalse();
    }

    [Theory]
    [InlineData(4, 0)]
    [InlineData(1, -1)]
    [InlineData(1, 31)]
    [InlineData(3, 29)]
    public void Defensive_core_unsupported_has_no_route_and_keeps_exact_scope_reason(
        int days,
        int offset
    )
    {
        var brief = Brief(days: days, start: Today.AddDays(offset));
        var facts = ItineraryTestFacts.Create();
        var outcome = Generate(brief, facts);
        outcome.Kind.ShouldBe(ItineraryOutcomeKind.Unsupported);
        Validate(brief, facts, outcome).IsError.ShouldBeFalse();
        Reject(brief, facts, new Output(outcome) { Reasons = ["invented"] }.Build());
    }

    [Fact]
    public void Equivalent_valid_metadata_and_shuffled_facts_are_compared_by_value()
    {
        var input = ItineraryTestFacts.Input();
        var facts = (
            input with
            {
                Pois = input.Pois.Reverse().ToArray(),
                Edges = input.Edges.Reverse().ToArray(),
                Evidence = input.Evidence.Reverse().ToArray(),
            }
        )
            .Build()
            .Value;
        var brief = Brief();
        var outcome = Generate(brief, facts);
        var equivalent = new Output(outcome)
        {
            Preparation = ItineraryTestFacts.Preparation(),
            Model = ItineraryTestFacts.Source(),
        }.Build();
        equivalent.ModelSource.ShouldNotBeSameAs(facts.ModelSource);
        Validate(brief, facts, equivalent).Value.ShouldBeSameAs(equivalent);
    }

    [Fact]
    public void Covered_interests_are_a_distinct_union_across_all_days_and_food_is_unmet()
    {
        var brief = Brief(
            days: 3,
            interests: ["food", "history", "nature", "architecture", "culture"]
        );
        var facts = ItineraryTestFacts.Create();
        var outcome = Generate(brief, facts);
        outcome.CoveredInterests.Count.ShouldBe(4);
        outcome.UnmetInterests.ShouldBe(["food"]);
        Validate(brief, facts, outcome).IsError.ShouldBeFalse();
        Reject(
            brief,
            facts,
            new Output(outcome)
            {
                Covered = ["architecture", "culture", "history", "history"],
                Unmet = ["food"],
            }.Build()
        );
    }

    [Fact]
    public void Expired_known_access_cannot_be_presented_as_known_or_full_day()
    {
        var facts = WithAccess("expired").Build().Value;
        var brief = Brief(start: Today.AddDays(6));
        var outcome = Generate(brief, facts);
        var first = outcome.Days[0].Blocks[0];
        var poi = facts.Pois.Single(x => x.Id == first.PoiId);
        var forged = new Block(first)
        {
            Facts =
            [
                first.Facts[0],
                new(
                    ItineraryFactKind.Access,
                    ItineraryFactState.Known,
                    poi.Access.EvidenceId,
                    null
                ),
            ],
            Intervals = [ItineraryWeeklyInterval.Create(first.Date.DayOfWeek, 0, 1440).Value],
        }.Build();
        Reject(brief, facts, ReplaceFirst(outcome, forged));
    }

    [Theory]
    [InlineData("identity")]
    [InlineData("walking")]
    public void Source_valid_now_but_not_for_the_whole_trip_day_cannot_be_selected(string kind)
    {
        var input = ItineraryTestFacts.Input();
        var brief = Brief(start: Today.AddDays(kind == "identity" ? 3 : 28));
        var original = Generate(brief, input.Build().Value);
        var evidence = input
            .Evidence.Select(e =>
                kind == "identity" && e.Kind == ItineraryFactKind.IdentityLocation
                    ? (
                        ItineraryTestFacts.EvidenceParameters(e.Id, e.Kind, e.ScopeId) with
                        {
                            ValidUntil = Now.AddDays(2),
                        }
                    )
                        .Build()
                        .Value
                    : e
            )
            .ToArray();
        var facts = (input with { ReleaseValidUntil = Now.AddDays(1), Evidence = evidence })
            .Build()
            .Value;
        // Walking is current at asOf, but the requested day exceeds snapshot validity.
        if (kind == "walking")
        {
            var early = Generate(Brief(), facts);
            var lateBlocks = early
                .Days[0]
                .Blocks.Select(b => new Block(b) { Date = brief.StartDate, Note = b.Note }.Build())
                .ToArray();
            original = new Output(early)
            {
                Days = [new(brief.StartDate, lateBlocks, early.Days[0].Edges)],
            }.Build();
        }
        Reject(brief, facts, original);
    }

    [Fact]
    public void Day_duration_uses_source_dwell_rather_than_a_producer_claim()
    {
        var input = ItineraryTestFacts.Input();
        var brief = Brief();
        var outcome = Generate(brief, input.Build().Value);
        var heavy = input
            .Pois.Select(p =>
                ItineraryPoi
                    .Create(
                        p.Id,
                        p.Title,
                        p.Tags,
                        120,
                        p.WikidataEntityId,
                        p.WikidataRevision,
                        p.IdentityEvidenceId,
                        p.Latitude,
                        p.Longitude,
                        p.Access
                    )
                    .Value
            )
            .ToArray();
        var facts = (input with { Pois = heavy }).Build().Value;
        Reject(brief, facts, outcome);
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("future")]
    public void Supplied_facts_must_be_current_even_for_unsupported_outputs(string state)
    {
        var brief = Brief(days: 4);
        var facts = ItineraryTestFacts.Create();
        var outcome = Generate(brief, facts);
        Reject(
            brief,
            facts,
            outcome,
            state == "expired" ? facts.ReleaseValidUntil : Now.AddTicks(-1)
        );
    }

    [Fact]
    public void Invalid_normalized_arguments_return_a_safe_input_error()
    {
        var brief = Brief();
        var facts = ItineraryTestFacts.Create();
        var outcome = Generate(brief, facts);
        ItineraryCandidateValidator
            .Validate(null!, facts.Destination, facts, outcome, Now)
            .FirstError.Code.ShouldBe("Trips.InvalidInput");
        ItineraryCandidateValidator
            .Validate(brief, null!, facts, outcome, Now)
            .FirstError.Code.ShouldBe("Trips.InvalidInput");
        ItineraryCandidateValidator
            .Validate(brief, facts.Destination, null!, outcome, Now)
            .FirstError.Code.ShouldBe("Trips.InvalidInput");
        Validate(brief, facts, outcome, Now.ToOffset(TimeSpan.FromHours(3)))
            .FirstError.Code.ShouldBe("Trips.InvalidInput");
        ItineraryCandidateValidator
            .Validate(brief, facts.Destination with { CatalogueVersion = "2" }, facts, outcome, Now)
            .FirstError.Code.ShouldBe("Trips.InvalidInput");
    }

    [Fact]
    public void Walking_upper_sum_rejects_a_route_even_when_each_connection_is_valid()
    {
        var input = ItineraryTestFacts.Input();
        var brief = Brief("active");
        var original = Generate(brief, input.Build().Value);
        var modelFacts = input
            .Evidence.Select(e =>
                e.Kind != ItineraryFactKind.Walking
                    ? e
                    : ItineraryTestFacts
                        .EvidenceParameters(
                            e.Id,
                            e.Kind,
                            e.ScopeId,
                            (
                                ItineraryTestFacts.PathInput(
                                    e.ModelledWalk!.PathId,
                                    e.ModelledWalk.FromPoiId,
                                    e.ModelledWalk.ToPoiId
                                ) with
                                {
                                    PathLengthMillimetres = 1_800_000,
                                    CrossingCount = 1,
                                }
                            )
                                .Build()
                                .Value
                        )
                        .Build()
                        .Value
            )
            .ToArray();
        var edges = input
            .Edges.Select(e =>
            {
                var m = modelFacts.Single(f => f.Id == e.EvidenceId).ModelledWalk!;
                return ItineraryWalkEdge
                    .Create(
                        e.Id,
                        e.FromPoiId,
                        e.ToPoiId,
                        m.MinimumMinutes,
                        m.MaximumMinutes,
                        e.EvidenceId,
                        e.Restrictions
                    )
                    .Value;
            })
            .ToArray();
        var factsResult = (input with { Edges = edges, Evidence = modelFacts }).Build();
        factsResult.IsError.ShouldBeFalse();
        var facts = factsResult.Value;
        var used = original
            .Days[0]
            .Edges.Select(e =>
            {
                var actual = edges.Single(x => x.Id == e.EdgeId);
                return new ItineraryCandidateEdge(
                    actual.Id,
                    actual.FromPoiId,
                    actual.ToPoiId,
                    actual.MinimumMinutes,
                    actual.MaximumMinutes,
                    actual.EvidenceId,
                    actual.Restrictions,
                    ItineraryFactMethod.Modelled,
                    ItineraryRightsBasis.Odbl,
                    modelFacts.Single(f => f.Id == actual.EvidenceId).ModelledWalk!
                );
            })
            .ToArray();
        used.Length.ShouldBe(3);
        used.Sum(e => e.MaximumMinutes).ShouldBe(123);
        Reject(
            brief,
            facts,
            new Output(original) { Days = [new(Today, original.Days[0].Blocks, used)] }.Build()
        );
    }

    [Fact]
    public void Repeated_tags_cannot_cover_an_absent_distinct_requested_interest()
    {
        var input = ItineraryTestFacts.Input();
        var brief = Brief(interests: ["architecture", "nature"]);
        var original = Generate(brief, input.Build().Value);
        var selected = original.Days[0].Blocks.Select(b => b.PoiId).ToHashSet();
        var pois = input
            .Pois.Select(p =>
                !selected.Contains(p.Id)
                    ? p
                    : ItineraryPoi
                        .Create(
                            p.Id,
                            p.Title,
                            ["architecture"],
                            p.DwellMinutes,
                            p.WikidataEntityId,
                            p.WikidataRevision,
                            p.IdentityEvidenceId,
                            p.Latitude,
                            p.Longitude,
                            p.Access
                        )
                        .Value
            )
            .ToArray();
        var factsResult = (input with { Pois = pois }).Build();
        factsResult.IsError.ShouldBeFalse();
        original.CoveredInterests.ShouldContain("nature");
        Reject(brief, factsResult.Value, original);
    }

    [Theory]
    [InlineData("uuid")]
    [InlineData("poi")]
    [InlineData("dateOrder")]
    public void Uniqueness_and_date_order_apply_across_all_days(string mutation)
    {
        var facts = ItineraryTestFacts.Create();
        var brief = Brief(days: 2);
        var original = Generate(brief, facts);
        var first = original.Days[0].Blocks[0];
        var second = original.Days[1];
        var replacement =
            mutation == "poi"
                ? new Block(first) { Date = second.Date }.Build()
                : new Block(second.Blocks[0]) { Id = first.ProposedBlockId }.Build();
        var days = original.Days.ToArray();
        days[1] = new(second.Date, [replacement, .. second.Blocks.Skip(1)], second.Edges);
        if (mutation == "dateOrder")
            days = original.Days.Reverse().ToArray();
        Reject(brief, facts, new Output(original) { Days = days }.Build());
    }

    [Fact]
    public void Expiry_is_clamped_to_release_and_uses_the_saved_zone()
    {
        var input = ItineraryTestFacts.Input();
        var destination = input.Destination with
        {
            TimeZone = "America/Los_Angeles",
            DisplayName = "Saved historical label",
        };
        var facts = (
            input with
            {
                Destination = destination,
                ReleaseValidUntil = Now.AddHours(21).AddMinutes(5),
            }
        )
            .Build()
            .Value;
        var brief = Brief();
        var asOf = Now.AddHours(21);
        var output = Generate(brief, facts, asOf);
        output.ExpiresAt.ShouldBe(facts.ReleaseValidUntil);
        output.Kind.ShouldBe(ItineraryOutcomeKind.Candidate);
        Validate(brief, facts, output, asOf).Value.ShouldBeSameAs(output);
    }

    private static ItineraryOutcome ReplaceFirst(
        ItineraryOutcome outcome,
        ItineraryCandidateBlock block
    ) =>
        new Output(outcome)
        {
            Days =
            [
                new(
                    outcome.Days[0].Date,
                    [block, .. outcome.Days[0].Blocks.Skip(1)],
                    outcome.Days[0].Edges
                ),
            ],
        }.Build();

    private static ItineraryTestFacts.FactsInput WithAccess(string state)
    {
        var input = ItineraryTestFacts.Input();
        var accesses = input
            .Pois.Select(p =>
                ItineraryTestFacts
                    .EvidenceParameters("access-" + p.Id, ItineraryFactKind.Access, p.Id)
                    .Build()
                    .Value
            )
            .ToArray();
        var intervals =
            state == "closed"
                ? Array.Empty<ItineraryWeeklyInterval>()
                : Enum.GetValues<DayOfWeek>()
                    .Select(d => ItineraryWeeklyInterval.Create(d, 0, 1440).Value)
                    .ToArray();
        var pois = input
            .Pois.Select(p =>
                ItineraryPoi
                    .Create(
                        p.Id,
                        p.Title,
                        p.Tags,
                        p.DwellMinutes,
                        p.WikidataEntityId,
                        p.WikidataRevision,
                        p.IdentityEvidenceId,
                        p.Latitude,
                        p.Longitude,
                        ItineraryAccess.Create(p.Id, true, "access-" + p.Id, intervals, []).Value
                    )
                    .Value
            )
            .ToArray();
        return input with { Pois = pois, Evidence = [.. input.Evidence, .. accesses] };
    }

    private static TripBrief Brief(
        string pace = "balanced",
        int days = 1,
        string[]? interests = null,
        DateOnly? start = null,
        string? origin = null,
        string? budget = null
    ) =>
        TripBrief
            .Create(
                new(
                    "moscow",
                    origin,
                    start ?? Today,
                    (start ?? Today).AddDays(days - 1),
                    1,
                    "RUB",
                    budget,
                    interests ?? [],
                    pace
                )
            )
            .Value;

    private static ItineraryOutcome Generate(
        TripBrief brief,
        ValidatedItineraryFacts facts,
        DateTimeOffset? now = null
    ) =>
        ItineraryPolicy
            .Generate(
                brief,
                facts.Destination,
                facts,
                now ?? Now,
                TestContext.Current.CancellationToken
            )
            .Value;

    private static ErrorOr.ErrorOr<ItineraryOutcome> Validate(
        TripBrief brief,
        ValidatedItineraryFacts facts,
        ItineraryOutcome output,
        DateTimeOffset? now = null
    ) => ItineraryCandidateValidator.Validate(brief, facts.Destination, facts, output, now ?? Now);

    private static void Reject(
        TripBrief brief,
        ValidatedItineraryFacts facts,
        ItineraryOutcome output,
        DateTimeOffset? now = null
    )
    {
        var result = Validate(brief, facts, output, now);
        result.IsError.ShouldBeTrue();
        result.FirstError.Code.ShouldBe("Trips.ItineraryUnavailable");
        result.FirstError.Description.ShouldBe("Itinerary generation is unavailable.");
    }

    private sealed record Output(ItineraryOutcome Original)
    {
        internal ItineraryOutcomeKind Kind { get; init; } = Original.Kind;
        internal DestinationSelection Destination { get; init; } = Original.Destination;
        internal string DatasetId { get; init; } = Original.DatasetId;
        internal string Version { get; init; } = Original.DatasetVersion;
        internal string Hash { get; init; } = Original.DatasetHash;
        internal string Policy { get; init; } = Original.PolicyVersion;
        internal DateTimeOffset Generated { get; init; } = Original.GeneratedAt;
        internal DateTimeOffset Expires { get; init; } = Original.ExpiresAt;
        internal ItineraryDemoPreparation Preparation { get; init; } = Original.Preparation;
        internal ItineraryModelSource Model { get; init; } = Original.ModelSource;
        internal IReadOnlyList<ItineraryCandidateDay> Days { get; init; } = Original.Days;
        internal ItineraryCoverage Coverage { get; init; } = Original.Coverage;
        internal IReadOnlyList<string> Covered { get; init; } = Original.CoveredInterests;
        internal IReadOnlyList<string> Unmet { get; init; } = Original.UnmetInterests;
        internal IReadOnlyList<string> Reasons { get; init; } = Original.Reasons;
        internal IReadOnlyList<string> Exclusions { get; init; } = Original.Exclusions;
        internal IReadOnlyList<string> Uncertainties { get; init; } = Original.Uncertainties;
        internal bool Limited { get; init; } = Original.SearchLimited;
        internal int Counter { get; init; } = Original.ExpansionCount;

        internal ItineraryOutcome Build() =>
            new(
                Kind,
                Destination,
                DatasetId,
                Version,
                Hash,
                Policy,
                Generated,
                Expires,
                Preparation,
                Model,
                Days,
                Coverage,
                Covered,
                Unmet,
                Reasons,
                Exclusions,
                Uncertainties,
                Limited,
                Counter
            );
    }

    private sealed record Block(ItineraryCandidateBlock Original)
    {
        internal string Poi { get; init; } = Original.PoiId;
        internal Guid Id { get; init; } = Original.ProposedBlockId;
        internal string Title { get; init; } = Original.Title;
        internal DateOnly Date { get; init; } = Original.Date;
        internal string? Start { get; init; } = Original.StartTime;
        internal string? End { get; init; } = Original.EndTime;
        internal PlanningMoney? Estimate { get; init; } = Original.Estimate;
        internal string Note { get; init; } = Original.Note;
        internal IReadOnlyList<ItineraryCandidateFact> Facts { get; init; } = Original.Facts;
        internal IReadOnlyList<ItineraryWeeklyInterval> Intervals { get; init; } =
            Original.OpeningIntervals;

        internal ItineraryCandidateBlock Build() =>
            new(Poi, Id, Title, Date, Start, End, Estimate, Note, Facts, Intervals);
    }
}
