using Shouldly;
using Travel.Modules.Trips.Core;
using Travel.Modules.Trips.Core.Itineraries;
using Xunit;

namespace Travel.Modules.Trips.Tests.Unit;

public sealed class ItineraryPolicyTests
{
    private static readonly DateTimeOffset Now = ItineraryTestFacts.Now;
    private static readonly DateOnly Today = new(2026, 10, 21);
    private static readonly DestinationSelection Destination = ItineraryTestFacts.Destination;

    [Theory]
    [InlineData("relaxed", 2)]
    [InlineData("balanced", 3)]
    [InlineData("active", 4)]
    public void Pace_fills_its_actual_target_without_inventing_time_or_money(
        string pace,
        int target
    )
    {
        var facts = ItineraryTestFacts.Create();
        var result = Generate(Brief(pace: pace, origin: "kaliningrad", budget: "1000.00"), facts);
        result.Kind.ShouldBe(ItineraryOutcomeKind.Candidate);
        result.Coverage.ShouldBe(ItineraryCoverage.Filled);
        result.Days.Single().Blocks.Count.ShouldBe(target);
        result.Exclusions.ShouldContain("transferNotPlanned");
        result.Exclusions.ShouldContain("totalCostUnknown");
        result.Uncertainties.ShouldContain("openingHoursUnknown");
        result.Uncertainties.ShouldContain("walkingModelled");
        result.Uncertainties.ShouldContain("calibrationNotEvaluated");
        result.Preparation.HumanQuality.ShouldBe("not-evaluated");
        result.Preparation.HumanCurator.ShouldBeNull();
        result.Preparation.HumanReviewer.ShouldBeNull();
        result
            .Days.SelectMany(d => d.Blocks)
            .ShouldAllBe(b => b.StartTime == null && b.EndTime == null && b.Estimate == null);
        foreach (var block in result.Days.SelectMany(d => d.Blocks))
        {
            block.Title.ShouldBe(facts.Pois.Single(p => p.Id == block.PoiId).Title);
            block.Note.ShouldContain(facts.DatasetId);
            block.Note.ShouldContain(facts.Version);
            block.Note.ShouldContain(block.PoiId);
            block.Note.EnumerateRunes().Count().ShouldBeLessThanOrEqualTo(500);
            block.ProposedBlockId.ShouldNotBe(Guid.Empty);
            block
                .Facts.Single(f => f.Kind == ItineraryFactKind.Access)
                .State.ShouldBe(ItineraryFactState.Unknown);
            block.OpeningIntervals.ShouldBeEmpty();
        }
        result
            .Days.SelectMany(d => d.Edges)
            .ShouldAllBe(e =>
                e.Method == ItineraryFactMethod.Modelled
                && e.RightsBasis == ItineraryRightsBasis.Odbl
                && e.ModelledWalk.CalibrationStatus == "not-evaluated"
                && e.ModelledWalk.Source.ModelVersion == "model-v1"
            );
    }

    [Fact]
    public void Fixed_inputs_and_shuffled_fact_arrays_keep_route_references_and_reasons_identical()
    {
        var input = ItineraryTestFacts.Input();
        var brief = Brief(days: 3, interests: ["nature", "architecture", "food"]);
        var first = Generate(brief, input.Build().Value);
        var repeated = Generate(brief, input.Build().Value);
        var shuffled = Generate(
            brief,
            (
                input with
                {
                    Pois = input.Pois.Reverse().ToArray(),
                    Edges = input.Edges.Reverse().ToArray(),
                    Evidence = input.Evidence.Reverse().ToArray(),
                }
            )
                .Build()
                .Value
        );
        Signature(first).ShouldBe(Signature(repeated));
        Signature(first).ShouldBe(Signature(shuffled));
        first.PolicyVersion.ShouldBe(ItineraryPolicy.PolicyVersion);
        first.GeneratedAt.ShouldBe(Now);
        first.ExpiresAt.ShouldBe(Now.AddMinutes(15));
        first.DatasetHash.ShouldBe(input.DatasetHash);
        first.Days.Select(d => d.Date).ShouldBe([Today, Today.AddDays(1), Today.AddDays(2)]);
        var blocks = first.Days.SelectMany(d => d.Blocks).ToArray();
        blocks.Select(b => b.PoiId).Distinct().Count().ShouldBe(blocks.Length);
        blocks.Select(b => b.ProposedBlockId).Distinct().Count().ShouldBe(blocks.Length);
    }

    [Fact]
    public void Empty_interests_have_no_artificial_preference_and_food_stays_unmet()
    {
        var facts = ItineraryTestFacts.Create();
        var none = Generate(Brief(interests: []), facts);
        var food = Generate(Brief(interests: ["food"]), facts);
        none.CoveredInterests.ShouldBeEmpty();
        none.UnmetInterests.ShouldBeEmpty();
        none.Coverage.ShouldBe(ItineraryCoverage.Filled);
        food.CoveredInterests.ShouldBeEmpty();
        food.UnmetInterests.ShouldBe(["food"]);
        food.Coverage.ShouldBe(ItineraryCoverage.Partial);
        food.Kind.ShouldBe(ItineraryOutcomeKind.Candidate);
        food.Reasons.ShouldContain("unmetInterests");
        food.Days.SelectMany(d => d.Blocks)
            .Select(b => b.PoiId)
            .ShouldBe(none.Days.SelectMany(d => d.Blocks).Select(b => b.PoiId));
    }

    [Fact]
    public void Distinct_requested_interests_beat_repeated_tags_before_slot_count()
    {
        var facts = Fixture(
            ["p0", "p1", "p2", "p3", "p4"],
            [("p0", "p1"), ("p1", "p2"), ("p3", "p4")],
            tags: new()
            {
                ["p0"] = ["architecture"],
                ["p1"] = ["architecture"],
                ["p2"] = ["architecture"],
                ["p3"] = ["architecture"],
                ["p4"] = ["nature"],
            }
        );
        var outcome = Generate(Brief(interests: ["architecture", "nature", "food"]), facts);
        outcome.Days.Single().Blocks.Select(b => b.PoiId).ShouldBe(["p3", "p4"]);
        outcome.CoveredInterests.ShouldBe(["architecture", "nature"]);
        outcome.UnmetInterests.ShouldBe(["food"]);
        outcome.Coverage.ShouldBe(ItineraryCoverage.Partial);
    }

    [Fact]
    public void Interest_score_unions_already_chosen_days_instead_of_rewarding_repeated_tags()
    {
        var facts = Fixture(
            ["p0", "p1", "p2", "p3", "p4"],
            [("p0", "p1"), ("p2", "p3"), ("p3", "p2")],
            tags: new()
            {
                ["p0"] = ["architecture"],
                ["p1"] = ["architecture"],
                ["p2"] = ["architecture"],
                ["p3"] = ["architecture"],
                ["p4"] = ["nature"],
            },
            firstDayClosed: ["p4"]
        );
        var outcome = Generate(
            Brief(days: 2, pace: "relaxed", interests: ["architecture", "nature"]),
            facts
        );
        outcome.Days[0].Blocks.Select(b => b.PoiId).ShouldBe(["p0", "p1"]);
        outcome.Days[1].Blocks.Select(b => b.PoiId).ShouldBe(["p4"]);
        outcome.CoveredInterests.ShouldBe(["architecture", "nature"]);
        outcome.UnmetInterests.ShouldBeEmpty();
    }

    [Fact]
    public void Score_prefers_lower_upper_walk_then_ordinal_sequence_not_minimum_walk()
    {
        var facts = Fixture(
            ["p0", "p1", "p2", "p3"],
            [("p0", "p1"), ("p2", "p3")],
            lengths: new() { [("p0", "p1")] = 900_000, [("p2", "p3")] = 450_000 }
        );
        Generate(Brief(pace: "relaxed"), facts)
            .Days.Single()
            .Blocks.Select(b => b.PoiId)
            .ShouldBe(["p2", "p3"]);
        var tied = Fixture(["p0", "p1", "p2", "p3"], [("p0", "p1"), ("p2", "p3")]);
        Generate(Brief(pace: "relaxed"), tied)
            .Days.Single()
            .Blocks.Select(b => b.PoiId)
            .ShouldBe(["p0", "p1"]);
    }

    [Fact]
    public void Known_closed_and_empty_intervals_exclude_stops_but_unknown_hours_remain_explicit()
    {
        var facts = Fixture(["p0", "p1"], [("p0", "p1")], firstDayClosed: ["p1"]);
        var outcome = Generate(Brief(pace: "relaxed"), facts);
        outcome.Days.Single().Blocks.Select(b => b.PoiId).ShouldBe(["p0"]);
        outcome.Coverage.ShouldBe(ItineraryCoverage.Partial);
        outcome.Reasons.ShouldContain("insufficientEligibleStops");
        outcome.Uncertainties.ShouldContain("openingHoursUnknown");
    }

    [Fact]
    public void Known_applicable_intervals_are_retained_without_an_arrival_time_claim()
    {
        var facts = Fixture(["p0", "p1"], [("p0", "p1")], knownOpen: ["p0", "p1"]);
        var outcome = Generate(Brief(pace: "relaxed"), facts);
        outcome.Coverage.ShouldBe(ItineraryCoverage.Filled);
        outcome.Uncertainties.ShouldNotContain("openingHoursUnknown");
        foreach (var block in outcome.Days.Single().Blocks)
        {
            var access = block.Facts.Single(f => f.Kind == ItineraryFactKind.Access);
            access.State.ShouldBe(ItineraryFactState.Known);
            access.EvidenceId.ShouldBe("access-" + block.PoiId);
            access.UnknownReason.ShouldBeNull();
            block.OpeningIntervals.Count.ShouldBe(1);
            block.StartTime.ShouldBeNull();
        }
    }

    [Fact]
    public void Disconnected_stops_do_not_get_reverse_or_invented_edges_and_all_days_remain()
    {
        var facts = Fixture(["p0", "p1"], []);
        var outcome = Generate(Brief(days: 3), facts);
        outcome.Kind.ShouldBe(ItineraryOutcomeKind.Candidate);
        outcome.Coverage.ShouldBe(ItineraryCoverage.Partial);
        outcome.Days.Select(d => d.Blocks.Count).ShouldBe([1, 1, 0]);
        outcome.Days.SelectMany(d => d.Edges).ShouldBeEmpty();
        outcome.Days[0].Blocks.Single().PoiId.ShouldBe("p0");
        outcome.Days[1].Blocks.Single().PoiId.ShouldBe("p1");
    }

    [Fact]
    public void Reverse_connection_is_only_used_in_its_permitted_direction()
    {
        var facts = Fixture(["p0", "p1"], [("p1", "p0")]);
        var outcome = Generate(Brief(pace: "relaxed"), facts);
        outcome.Days.Single().Blocks.Select(b => b.PoiId).ShouldBe(["p1", "p0"]);
        outcome.Days.Single().Edges.Single().FromPoiId.ShouldBe("p1");
    }

    [Fact]
    public void No_eligible_stop_is_successful_empty_instead_of_unsupported_or_an_error()
    {
        var outcome = Generate(Brief(days: 2), Fixture([], []));
        outcome.Kind.ShouldBe(ItineraryOutcomeKind.Empty);
        outcome.Days.Count.ShouldBe(2);
        outcome.Days.ShouldAllBe(d => d.Blocks.Count == 0 && d.Edges.Count == 0);
        outcome.Reasons.ShouldContain("noEligibleStops");
    }

    [Theory]
    [InlineData(-1, 1, "historicalDatesUnsupported")]
    [InlineData(0, 4, "dateRangeUnsupported")]
    [InlineData(30, 2, "horizonUnsupported")]
    [InlineData(31, 1, "horizonUnsupported")]
    public void Unsupported_dates_do_not_return_a_fake_partial_route(
        int offset,
        int days,
        string reason
    )
    {
        var outcome = Generate(
            Brief(start: Today.AddDays(offset), days: days),
            ItineraryTestFacts.Create()
        );
        outcome.Kind.ShouldBe(ItineraryOutcomeKind.Unsupported);
        outcome.Days.ShouldBeEmpty();
        outcome.Reasons.ShouldBe([reason]);
        outcome.ExpansionCount.ShouldBe(0);
    }

    [Fact]
    public void Horizon_boundary_uses_saved_iana_local_date_and_captured_clock()
    {
        var input = ItineraryTestFacts.Input();
        var asOf = Now.AddHours(21); // Moscow date is tomorrow, while UTC is still today.
        var brief = Brief(start: Today.AddDays(31));
        var result = Generate(brief, input.Build().Value, asOf);
        result.Kind.ShouldBe(ItineraryOutcomeKind.Candidate);
        result.Days.Single().Edges.ShouldBeEmpty(); // model evidence expired for that day.
        Generate(brief, input.Build().Value, Now).Kind.ShouldBe(ItineraryOutcomeKind.Unsupported);
        Generate(Brief(start: Today), input.Build().Value, asOf)
            .Reasons.ShouldBe(["historicalDatesUnsupported"]);
    }

    [Fact]
    public void Current_package_is_required_and_preview_expiry_is_clamped_to_release()
    {
        var input = ItineraryTestFacts.Input();
        var shortRelease = (input with { ReleaseValidUntil = Now.AddMinutes(5) }).Build().Value;
        Generate(Brief(), shortRelease).ExpiresAt.ShouldBe(Now.AddMinutes(5));
        var expired = ItineraryPolicy.Generate(
            Brief(),
            Destination,
            shortRelease,
            Now.AddMinutes(5),
            TestContext.Current.CancellationToken
        );
        expired.IsError.ShouldBeTrue();
        expired.FirstError.Code.ShouldBe("Trips.ItineraryUnavailable");
        ItineraryPolicy
            .Generate(
                Brief(),
                Destination,
                input.Build().Value,
                Now.AddSeconds(-1),
                TestContext.Current.CancellationToken
            )
            .IsError.ShouldBeTrue();
    }

    [Fact]
    public void Access_expiry_is_unknown_while_expired_walking_is_never_repaired()
    {
        var input = ItineraryTestFacts.Input();
        var facts = Fixture(["p0", "p1"], [("p0", "p1")], knownOpen: ["p0", "p1"]);
        var future = Today.AddDays(6);
        var outcome = Generate(Brief(start: future, pace: "relaxed"), facts);
        outcome.Uncertainties.ShouldContain("openingHoursUnknown");
        outcome
            .Days.Single()
            .Blocks.SelectMany(b => b.Facts)
            .Where(f => f.Kind == ItineraryFactKind.Access)
            .ShouldAllBe(f => f.State == ItineraryFactState.Unknown && f.EvidenceId == null);
        var expiredWalkDate = Today.AddDays(28);
        var noWalk = Generate(Brief(start: expiredWalkDate), input.Build().Value);
        noWalk.Kind.ShouldBe(ItineraryOutcomeKind.Candidate);
        noWalk.Days.Single().Blocks.Count.ShouldBe(1);
        noWalk.Days.Single().Edges.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("relaxed", 2_000_000, 30, 2)]
    [InlineData("relaxed", 3_000_000, 30, 1)]
    [InlineData("balanced", 3_000_000, 30, 2)]
    [InlineData("active", 3_000_000, 30, 2)]
    [InlineData("balanced", 2_000_000, 30, 3)]
    [InlineData("active", 2_000_000, 30, 3)]
    [InlineData("relaxed", 450_000, 120, 1)]
    [InlineData("balanced", 450_000, 120, 2)]
    [InlineData("active", 450_000, 120, 3)]
    public void Walk_upper_and_dwell_caps_are_enforced(
        string pace,
        long millimetres,
        int dwell,
        int expected
    )
    {
        var facts = Fixture(
            ["p0", "p1", "p2"],
            [("p0", "p1"), ("p1", "p2")],
            lengths: new() { [("p0", "p1")] = millimetres, [("p1", "p2")] = millimetres },
            dwell: dwell
        );
        Generate(Brief(pace: pace), facts).Days.Single().Blocks.Count.ShouldBe(expected);
    }

    [Fact]
    public void Mismatched_normalized_references_and_non_utc_asof_fail_closed()
    {
        var facts = ItineraryTestFacts.Create();
        foreach (
            var destination in new[]
            {
                Destination with
                {
                    DestinationId = "kaliningrad",
                },
                Destination with
                {
                    TimeZone = "Europe/Kaliningrad",
                },
                Destination with
                {
                    CatalogueVersion = "2",
                },
                Destination with
                {
                    TimeZone = "UTC",
                },
            }
        )
            ItineraryPolicy
                .Generate(Brief(), destination, facts, Now, TestContext.Current.CancellationToken)
                .FirstError.Code.ShouldBe("Trips.InvalidInput");
        ItineraryPolicy
            .Generate(
                Brief(),
                Destination,
                facts,
                Now.ToOffset(TimeSpan.FromHours(3)),
                TestContext.Current.CancellationToken
            )
            .IsError.ShouldBeTrue();
    }

    [Fact]
    public void Core_uses_the_matching_saved_destination_zone_without_a_moscow_default()
    {
        var destination = new DestinationSelection(
            "kaliningrad",
            "Saved name",
            "Europe/Kaliningrad",
            "1"
        );
        var facts = (ItineraryTestFacts.Input() with { Destination = destination }).Build().Value;
        var brief = TripBrief
            .Create(Brief().ToInput() with { DestinationId = "kaliningrad" })
            .Value;
        var result = ItineraryPolicy
            .Generate(
                brief,
                destination,
                facts,
                Now.AddHours(21),
                TestContext.Current.CancellationToken
            )
            .Value;
        result.Kind.ShouldBe(ItineraryOutcomeKind.Candidate); // local date still today, unlike Moscow.
    }

    [Fact]
    public void Cancellation_is_observed_before_any_route_is_returned()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Should.Throw<OperationCanceledException>(() =>
            ItineraryPolicy.Generate(
                Brief(),
                Destination,
                ItineraryTestFacts.Create(),
                Now,
                cancelled.Token
            )
        );
    }

    [Fact]
    public void All_output_collections_are_defensive_copies()
    {
        var generated = Generate(Brief(), ItineraryTestFacts.Create());
        var blocks = generated.Days.Single().Blocks.ToArray();
        var edges = generated.Days.Single().Edges.ToArray();
        var day = new ItineraryCandidateDay(Today, blocks, edges);
        var first = blocks[0];
        blocks[0] = blocks[1];
        day.Blocks[0].ShouldBeSameAs(first);
        var facts = first.Facts.ToArray();
        var block = new ItineraryCandidateBlock(
            first.PoiId,
            first.ProposedBlockId,
            first.Title,
            first.Date,
            null,
            null,
            null,
            first.Note,
            facts,
            []
        );
        var identity = facts[0];
        facts[0] = facts[1];
        block.Facts[0].ShouldBeSameAs(identity);
        ((IList<ItineraryCandidateBlock>)day.Blocks).IsReadOnly.ShouldBeTrue();
    }

    [Fact]
    public void Maximal_supported_package_keeps_one_global_counter_below_the_proven_bound()
    {
        var facts = DenseFacts();
        facts.Pois.Count.ShouldBe(48);
        facts.Edges.Count.ShouldBe(256);
        var one = Generate(Brief(days: 1, pace: "active"), facts);
        var two = Generate(Brief(days: 2, pace: "active"), facts);
        var three = Generate(Brief(days: 3, pace: "active"), facts);
        one.ExpansionCount.ShouldBeGreaterThan(0);
        two.ExpansionCount.ShouldBeGreaterThan(one.ExpansionCount);
        three.ExpansionCount.ShouldBeGreaterThan(two.ExpansionCount);
        // Independent arithmetic: <=3 days*(48 seeds+3 levels*16 paths*48 next-POI attempts).
        three.ExpansionCount.ShouldBeLessThanOrEqualTo(7056);
        three.ExpansionCount.ShouldBeLessThan(ItineraryPolicy.MaximumExpansions);
        three.SearchLimited.ShouldBeFalse();
        three.Coverage.ShouldBe(ItineraryCoverage.Filled);
        three.Days.SelectMany(d => d.Blocks).Select(b => b.PoiId).Distinct().Count().ShouldBe(12);
    }

    [Fact]
    public void Identity_evidence_valid_now_but_expired_on_the_trip_cannot_supply_a_stop()
    {
        var input = ItineraryTestFacts.Input();
        var identities = input
            .Evidence.Where(e => e.Kind == ItineraryFactKind.IdentityLocation)
            .Select(e =>
                (
                    ItineraryTestFacts.EvidenceParameters(e.Id, e.Kind, e.ScopeId) with
                    {
                        ValidUntil = Now.AddDays(2),
                    }
                )
                    .Build()
                    .Value
            )
            .ToArray();
        var facts = (
            input with
            {
                ReleaseValidUntil = Now.AddDays(1),
                Evidence = identities
                    .Concat(input.Evidence.Where(e => e.Kind != ItineraryFactKind.IdentityLocation))
                    .ToArray(),
            }
        )
            .Build()
            .Value;
        var result = Generate(Brief(start: Today.AddDays(3)), facts);
        result.Kind.ShouldBe(ItineraryOutcomeKind.Empty);
        result.Days.Single().Blocks.ShouldBeEmpty();
        result.Days.Single().Edges.ShouldBeEmpty();
    }

    [Fact]
    public void Expired_package_fails_closed_even_when_the_brief_is_outside_generation_scope()
    {
        var facts = ItineraryTestFacts.Create();
        var result = ItineraryPolicy.Generate(
            Brief(days: 4),
            Destination,
            facts,
            Now.AddDays(21),
            TestContext.Current.CancellationToken
        );
        result.IsError.ShouldBeTrue();
        result.FirstError.Code.ShouldBe("Trips.ItineraryUnavailable");
        var extreme = ItineraryPolicy.Generate(
            Brief(),
            Destination,
            facts,
            DateTimeOffset.MaxValue,
            TestContext.Current.CancellationToken
        );
        extreme.IsError.ShouldBeTrue();
    }

    [Fact]
    public void Historical_note_preserves_osm_credit_and_licence_at_maximum_identity_lengths()
    {
        var input = ItineraryTestFacts.Input();
        var ids = input.Pois.ToDictionary(p => p.Id, p => "p" + p.Id[1..].PadLeft(63, '0'));
        var unicodeTitle = string.Concat(Enumerable.Repeat("🧭", 100));
        var pois = input
            .Pois.Select(p =>
                ItineraryPoi
                    .Create(
                        ids[p.Id],
                        unicodeTitle,
                        p.Tags,
                        p.DwellMinutes,
                        p.WikidataEntityId,
                        p.WikidataRevision,
                        p.IdentityEvidenceId,
                        p.Latitude,
                        p.Longitude,
                        ItineraryAccess.Create(ids[p.Id], false, null, [], []).Value
                    )
                    .Value
            )
            .ToArray();
        var evidence = pois.Select(p =>
                ItineraryTestFacts
                    .EvidenceParameters(
                        p.IdentityEvidenceId,
                        ItineraryFactKind.IdentityLocation,
                        p.Id
                    )
                    .Build()
                    .Value
            )
            .ToList();
        var edges = input
            .Edges.Select(edge =>
            {
                var model = ItineraryTestFacts
                    .PathInput("path-" + edge.Id, ids[edge.FromPoiId], ids[edge.ToPoiId])
                    .Build()
                    .Value;
                evidence.Add(
                    ItineraryTestFacts
                        .EvidenceParameters(
                            edge.EvidenceId,
                            ItineraryFactKind.Walking,
                            edge.Id,
                            model
                        )
                        .Build()
                        .Value
                );
                return ItineraryWalkEdge
                    .Create(
                        edge.Id,
                        model.FromPoiId,
                        model.ToPoiId,
                        model.MinimumMinutes,
                        model.MaximumMinutes,
                        edge.EvidenceId,
                        edge.Restrictions
                    )
                    .Value;
            })
            .ToArray();
        var facts = (
            input with
            {
                DatasetId = new string('d', 64),
                Version = new string('v', 64),
                Pois = pois,
                Edges = edges,
                Evidence = evidence.ToArray(),
            }
        )
            .Build()
            .Value;
        var result = Generate(Brief(), facts);
        result.Coverage.ShouldBe(ItineraryCoverage.Filled);
        facts.DatasetId.Length.ShouldBe(64);
        facts.Version.Length.ShouldBe(64);
        foreach (var block in result.Days.SelectMany(d => d.Blocks))
        {
            block.PoiId.Length.ShouldBe(64);
            block.Title.ShouldBe(unicodeTitle);
            block.Title.Length.ShouldBe(200);
            block.Title.EnumerateRunes().Count().ShouldBe(100);
            block.Note.ShouldContain(facts.DatasetId);
            block.Note.ShouldContain(facts.Version);
            block.Note.ShouldContain(block.PoiId);
            block.Note.ShouldContain("asOf=2026-10-21T00:00:00.0000000+00:00");
            block.Note.ShouldContain("© OpenStreetMap contributors");
            block.Note.ShouldContain("ODbL");
            block.Note.ShouldContain("https://opendatacommons.org/licenses/odbl/1-0/");
            block.Note.ShouldContain("https://www.openstreetmap.org/copyright");
            block.Note.EnumerateRunes().Count().ShouldBeLessThanOrEqualTo(500);
            var ordinaryBlock = TripBlock.Create(
                new(block.ProposedBlockId, block.Title, block.Note),
                "RUB"
            );
            ordinaryBlock.IsError.ShouldBeFalse();
            ordinaryBlock.Value.Note.ShouldBe(block.Note);
        }
    }

    private static ValidatedItineraryFacts DenseFacts()
    {
        var input = ItineraryTestFacts.Input();
        var pois = Enumerable.Range(0, 48).Select(i => ItineraryTestFacts.Poi("p" + i)).ToArray();
        var evidence = pois.Select(p =>
                ItineraryTestFacts
                    .EvidenceParameters(
                        p.IdentityEvidenceId,
                        ItineraryFactKind.IdentityLocation,
                        p.Id
                    )
                    .Build()
                    .Value
            )
            .ToList();
        var pairs = Enumerable
            .Range(1, 6)
            .SelectMany(offset =>
                Enumerable
                    .Range(0, 48)
                    .Select(index => (From: "p" + index, To: "p" + ((index + offset) % 48)))
            )
            .Take(256)
            .ToArray();
        var edges = pairs
            .Select(
                (pair, index) =>
                {
                    var id = "e" + index;
                    var model = ItineraryTestFacts
                        .PathInput("path-" + id, pair.From, pair.To)
                        .Build()
                        .Value;
                    evidence.Add(
                        ItineraryTestFacts
                            .EvidenceParameters("walk-" + id, ItineraryFactKind.Walking, id, model)
                            .Build()
                            .Value
                    );
                    return ItineraryWalkEdge
                        .Create(
                            id,
                            pair.From,
                            pair.To,
                            model.MinimumMinutes,
                            model.MaximumMinutes,
                            "walk-" + id,
                            "FICTIONAL uncalibrated mapped eligibility"
                        )
                        .Value;
                }
            )
            .ToArray();
        var result = (
            input with
            {
                Pois = pois,
                Edges = edges,
                Evidence = evidence.ToArray(),
            }
        ).Build();
        result.IsError.ShouldBeFalse();
        return result.Value;
    }

    private static TripBrief Brief(
        int days = 1,
        string pace = "balanced",
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
        DateTimeOffset? asOf = null
    )
    {
        var result = ItineraryPolicy.Generate(
            brief,
            facts.Destination,
            facts,
            asOf ?? Now,
            TestContext.Current.CancellationToken
        );
        result.IsError.ShouldBeFalse();
        return result.Value;
    }

    private static string Signature(ItineraryOutcome outcome) =>
        string.Join(
            "|",
            outcome
                .Days.Select(d =>
                    d.Date
                    + ":"
                    + string.Join(
                        ",",
                        d.Blocks.Select(b =>
                            b.PoiId
                            + ":"
                            + string.Join(
                                ";",
                                b.Facts.Select(f =>
                                    f.Kind
                                    + ":"
                                    + f.State
                                    + ":"
                                    + f.EvidenceId
                                    + ":"
                                    + f.UnknownReason
                                )
                            )
                        )
                    )
                    + ":"
                    + string.Join(
                        ",",
                        d.Edges.Select(e =>
                            e.EdgeId + ":" + e.EvidenceId + ":" + e.ModelledWalk.PathId
                        )
                    )
                )
                .Concat([
                    outcome.Coverage.ToString(),
                    string.Join(",", outcome.Reasons),
                    string.Join(",", outcome.UnmetInterests),
                    outcome.ExpansionCount.ToString(),
                ])
        );

    // FICTIONAL only: inactive places have a valid known-closed access record.
    // Filler edges satisfy package structure entirely among inactive places and never repair active gaps.
    private static ValidatedItineraryFacts Fixture(
        string[] active,
        (string From, string To)[] pairs,
        Dictionary<string, string[]>? tags = null,
        string[]? firstDayClosed = null,
        string[]? knownOpen = null,
        Dictionary<(string From, string To), long>? lengths = null,
        int dwell = 30
    )
    {
        var input = ItineraryTestFacts.Input();
        var evidence = new List<ItineraryEvidence>();
        var pois = input
            .Pois.Select(p =>
            {
                var needsAccess =
                    !active.Contains(p.Id)
                    || (firstDayClosed?.Contains(p.Id) ?? false)
                    || (knownOpen?.Contains(p.Id) ?? false);
                var access = p.Access;
                if (needsAccess)
                {
                    var intervals = active.Contains(p.Id)
                        ? Enum.GetValues<DayOfWeek>()
                            .Select(day => ItineraryWeeklyInterval.Create(day, 0, 1440).Value)
                            .ToArray()
                        : Array.Empty<ItineraryWeeklyInterval>();
                    access = ItineraryAccess
                        .Create(
                            p.Id,
                            true,
                            "access-" + p.Id,
                            intervals,
                            firstDayClosed?.Contains(p.Id) == true ? [Today] : []
                        )
                        .Value;
                    evidence.Add(
                        ItineraryTestFacts
                            .EvidenceParameters("access-" + p.Id, ItineraryFactKind.Access, p.Id)
                            .Build()
                            .Value
                    );
                }
                evidence.Add(input.Evidence.Single(e => e.Id == p.IdentityEvidenceId));
                return ItineraryPoi
                    .Create(
                        p.Id,
                        p.Title,
                        tags?.GetValueOrDefault(p.Id) ?? p.Tags,
                        dwell,
                        p.WikidataEntityId,
                        p.WikidataRevision,
                        p.IdentityEvidenceId,
                        p.Latitude,
                        p.Longitude,
                        access
                    )
                    .Value;
            })
            .ToArray();
        var selectedPairs = pairs.ToList();
        var inactive = pois.Where(p => !active.Contains(p.Id)).Select(p => p.Id).ToArray();
        foreach (var from in inactive)
        foreach (var to in inactive)
            if (from != to && selectedPairs.Count < 60 && !selectedPairs.Contains((from, to)))
                selectedPairs.Add((from, to));
        var edges = selectedPairs
            .Select(
                (pair, index) =>
                {
                    var id = "e" + index;
                    var model = (
                        ItineraryTestFacts.PathInput("path-" + id, pair.From, pair.To) with
                        {
                            PathLengthMillimetres =
                                lengths?.GetValueOrDefault(pair, 450_000) ?? 450_000,
                            CrossingCount = 0,
                        }
                    )
                        .Build()
                        .Value;
                    evidence.Add(
                        ItineraryTestFacts
                            .EvidenceParameters("walk-" + id, ItineraryFactKind.Walking, id, model)
                            .Build()
                            .Value
                    );
                    return ItineraryWalkEdge
                        .Create(
                            id,
                            pair.From,
                            pair.To,
                            model.MinimumMinutes,
                            model.MaximumMinutes,
                            "walk-" + id,
                            "FICTIONAL; no observed feasibility"
                        )
                        .Value;
                }
            )
            .ToArray();
        var result = (
            input with
            {
                Pois = pois,
                Edges = edges,
                Evidence = evidence.ToArray(),
            }
        ).Build();
        result.IsError.ShouldBeFalse();
        return result.Value;
    }
}
