using Shouldly;
using Travel.Modules.Trips.Core;
using Travel.Modules.Trips.Core.Itineraries;
using Xunit;

namespace Travel.Modules.Trips.Tests.Unit;

public sealed class ItineraryFactsTests
{
    [Fact]
    public void Accepts_fictional_modelled_release_without_inventing_observers_or_human_quality()
    {
        var result = ItineraryTestFacts.Input().Build();
        result.IsError.ShouldBeFalse();
        var facts = result.Value;
        facts.Pois.Count.ShouldBe(18);
        facts.Edges.Count.ShouldBe(60);
        facts.DataMode.ShouldBe("osm-modelled-demo");
        facts.PreparedBy.ShouldBe("fictional-builder-v1");
        facts.HumanCurator.ShouldBeNull();
        facts.HumanReviewer.ShouldBeNull();
        facts.HumanQuality.ShouldBe("not-evaluated");
        facts.CalibrationStatus.ShouldBe("not-evaluated");
        facts
            .Evidence.Where(e => e.Kind == ItineraryFactKind.Walking)
            .ShouldAllBe(e =>
                e.Method == ItineraryFactMethod.Modelled
                && e.RightsBasis == ItineraryRightsBasis.Odbl
                && e.ObservedAt == null
            );
    }

    [Theory]
    [InlineData("measured", null, null, "not-evaluated", "not-evaluated")]
    [InlineData("osm-modelled-demo", "person", null, "not-evaluated", "not-evaluated")]
    [InlineData("osm-modelled-demo", null, "person", "not-evaluated", "not-evaluated")]
    [InlineData("osm-modelled-demo", null, null, "passed", "not-evaluated")]
    [InlineData("osm-modelled-demo", null, null, "not-evaluated", "calibrated")]
    public void Rejects_fake_human_or_calibration_claims(
        string mode,
        string? curator,
        string? reviewer,
        string quality,
        string calibration
    ) =>
        ItineraryDemoPreparation
            .Create(
                mode,
                "builder-v1",
                curator,
                reviewer,
                quality,
                calibration,
                ItineraryTestFacts.Now
            )
            .IsError.ShouldBeTrue();

    [Fact]
    public void Rejects_missing_software_preparer_and_non_utc_build()
    {
        ItineraryDemoPreparation
            .Create(
                "osm-modelled-demo",
                null,
                null,
                null,
                "not-evaluated",
                "not-evaluated",
                ItineraryTestFacts.Now
            )
            .IsError.ShouldBeTrue();
        ItineraryDemoPreparation
            .Create(
                "osm-modelled-demo",
                "builder-v1",
                null,
                null,
                "not-evaluated",
                "not-evaluated",
                ItineraryTestFacts.Now.ToOffset(TimeSpan.FromHours(3))
            )
            .IsError.ShouldBeTrue();
    }

    [Fact]
    public void Binds_snapshot_publication_to_exact_input_and_ordered_frozen_utc_times()
    {
        var input = ItineraryTestFacts.SourceInput();
        input.Build().IsError.ShouldBeFalse();
        foreach (
            var invalid in new[]
            {
                input with
                {
                    PublicationInputSha256 = new string('c', 64),
                },
                input with
                {
                    InputSha256 = new string('B', 64),
                },
                input with
                {
                    SnapshotPublishedAtUtc = input.DownloadedAtUtc.AddTicks(1),
                },
                input with
                {
                    DownloadedAtUtc = input.BuildAsOfUtc.AddTicks(1),
                },
                input with
                {
                    SnapshotPublishedAtUtc = input.SnapshotPublishedAtUtc.ToOffset(
                        TimeSpan.FromHours(3)
                    ),
                },
                input with
                {
                    SnapshotPublishedAtUtc = default,
                },
                input with
                {
                    ProfileVersion = "profile-v2",
                },
                input with
                {
                    PairSelectorVersion = "pair-selector-v2",
                },
                input with
                {
                    ModelVersion = "model-v2",
                },
            }
        )
            invalid.Build().IsError.ShouldBeTrue();
    }

    [Fact]
    public void Thirty_day_model_expiry_starts_at_publication_and_is_not_renewed_by_rebuilding()
    {
        var input = ItineraryTestFacts.SourceInput();
        var first = input.Build().Value;
        first.ValidUntil.ShouldBe(new DateTimeOffset(2026, 11, 17, 0, 0, 0, TimeSpan.Zero));
        var rebuilt = (input with { BuildAsOfUtc = ItineraryTestFacts.Now.AddDays(3) }).Build();
        rebuilt.IsError.ShouldBeFalse();
        rebuilt.Value.ValidUntil.ShouldBe(first.ValidUntil);
        (input with { BuildAsOfUtc = first.ValidUntil.AddTicks(-1) })
            .Build()
            .IsError.ShouldBeFalse();
        (input with { BuildAsOfUtc = first.ValidUntil }).Build().IsError.ShouldBeTrue();
        (input with { BuildAsOfUtc = first.ValidUntil.AddTicks(1) }).Build().IsError.ShouldBeTrue();
    }

    [Theory]
    [InlineData(0, 0, 1, 1)]
    [InlineData(1, 0, 1, 1)]
    [InlineData(45000, 0, 1, 1)]
    [InlineData(45001, 0, 1, 2)]
    [InlineData(75000, 0, 1, 2)]
    [InlineData(75001, 2, 2, 4)]
    [InlineData(900000, 2, 12, 22)]
    [InlineData(5400000, 0, 72, 120)]
    public void Computes_model_v1_integer_envelope_without_claiming_measurement(
        long millimetres,
        int crossings,
        int lower,
        int upper
    )
    {
        var result = (
            ItineraryTestFacts.PathInput() with
            {
                PathLengthMillimetres = millimetres,
                CrossingCount = crossings,
                ToAnchorNodeId = millimetres == 0 ? 1 : 2,
            }
        ).Build();
        result.IsError.ShouldBeFalse();
        result.Value.MinimumMinutes.ShouldBe(lower);
        result.Value.MaximumMinutes.ShouldBe(upper);
        result.Value.CalibrationStatus.ShouldBe("not-evaluated");
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(1, -1)]
    [InlineData(5400001, 0)]
    [InlineData(5400000, 1)]
    [InlineData(long.MaxValue, int.MaxValue)]
    public void Rejects_invalid_or_unbounded_model_inputs_without_overflow(
        long millimetres,
        int crossings
    ) =>
        (
            ItineraryTestFacts.PathInput() with
            {
                PathLengthMillimetres = millimetres,
                CrossingCount = crossings,
            }
        )
            .Build()
            .IsError.ShouldBeTrue();

    [Fact]
    public void Rejects_invalid_path_identity_anchor_and_direction()
    {
        var input = ItineraryTestFacts.PathInput();
        foreach (
            var invalid in new[]
            {
                input with
                {
                    PathHash = "bad",
                },
                input with
                {
                    PathId = "../x",
                },
                input with
                {
                    FromAnchorNodeId = 0,
                },
                input with
                {
                    ToAnchorNodeId = -1,
                },
                input with
                {
                    ToPoiId = input.FromPoiId,
                },
            }
        )
            invalid.Build().IsError.ShouldBeTrue();
    }

    [Fact]
    public void Walking_requires_modelled_odbl_and_never_an_observed_timestamp()
    {
        var input = WalkEvidence();
        foreach (
            var invalid in new[]
            {
                input with
                {
                    Method = ItineraryFactMethod.Observed,
                },
                input with
                {
                    RightsBasis = ItineraryRightsBasis.OriginalObservation,
                },
                input with
                {
                    ObservedAt = ItineraryTestFacts.Now,
                },
                input with
                {
                    ModelledWalk = null,
                },
                input with
                {
                    PermittedFields = [ItineraryFactField.OpeningHours],
                },
            }
        )
            invalid.Build().IsError.ShouldBeTrue();
    }

    [Fact]
    public void Walking_evidence_binds_source_url_retrieval_processing_and_publication_lease()
    {
        var input = WalkEvidence();
        foreach (
            var invalid in new[]
            {
                input with
                {
                    SourceUrl = "https://other.example.test/osm.gz",
                },
                input with
                {
                    RetrievedAtUtc = input.RetrievedAtUtc.AddTicks(1),
                },
                input with
                {
                    ProcessedAtUtc = input.ProcessedAtUtc.AddTicks(-1),
                },
                input with
                {
                    ValidUntil = input.ValidUntil.AddTicks(1),
                },
                input with
                {
                    ValidUntil = input.ValidUntil.AddTicks(-1),
                },
            }
        )
            invalid.Build().IsError.ShouldBeTrue();
    }

    [Theory]
    [InlineData(ItineraryFactKind.IdentityLocation, 90)]
    [InlineData(ItineraryFactKind.Access, 7)]
    public void Source_record_leases_start_at_retrieval_not_later_processing(
        ItineraryFactKind kind,
        int days
    )
    {
        var input = ItineraryTestFacts.EvidenceParameters("fact", kind, "p0");
        var boundary = input.RetrievedAtUtc.AddDays(days);
        (input with { ValidUntil = boundary }).Build().IsError.ShouldBeFalse();
        (input with { ValidUntil = boundary.AddTicks(1) }).Build().IsError.ShouldBeTrue();
        (input with { ProcessedAtUtc = input.ProcessedAtUtc.AddDays(1), ValidUntil = boundary })
            .Build()
            .Value.ValidUntil.ShouldBe(boundary);
        var evidence = input.Build().Value;
        evidence.IsKnownAt(input.ProcessedAtUtc.AddTicks(-1)).ShouldBeFalse();
        evidence.IsKnownAt(boundary.AddTicks(-1)).ShouldBeTrue();
        evidence.IsKnownAt(boundary).ShouldBeFalse();
    }

    [Fact]
    public void Observed_access_uses_actual_observation_and_cannot_extend_lease_at_retrieval()
    {
        var input = ItineraryTestFacts.EvidenceParameters(
            "access",
            ItineraryFactKind.Access,
            "p0"
        ) with
        {
            Method = ItineraryFactMethod.Observed,
            RightsBasis = ItineraryRightsBasis.OriginalObservation,
            ObservedAt = ItineraryTestFacts.Now.AddDays(-4),
            ValidUntil = ItineraryTestFacts.Now.AddDays(3),
        };
        input.Build().IsError.ShouldBeFalse();
        (input with { ValidUntil = input.ValidUntil.AddTicks(1) }).Build().IsError.ShouldBeTrue();
        (input with { ObservedAt = null }).Build().IsError.ShouldBeTrue();
        (input with { ObservedAt = input.RetrievedAtUtc.AddTicks(1) })
            .Build()
            .IsError.ShouldBeTrue();
        (input with { Method = ItineraryFactMethod.SourceRecord }).Build().IsError.ShouldBeTrue();
    }

    [Fact]
    public void Rejects_invalid_rights_method_fields_sources_dates_and_enums()
    {
        var input = ItineraryTestFacts.EvidenceParameters(
            "fact",
            ItineraryFactKind.IdentityLocation,
            "p0"
        );
        foreach (
            var invalid in new[]
            {
                input with
                {
                    Method = ItineraryFactMethod.Modelled,
                },
                input with
                {
                    RightsBasis = ItineraryRightsBasis.Odbl,
                },
                input with
                {
                    ObservedAt = input.RetrievedAtUtc,
                },
                input with
                {
                    Kind = (ItineraryFactKind)99,
                },
                input with
                {
                    Method = (ItineraryFactMethod)99,
                },
                input with
                {
                    RightsBasis = (ItineraryRightsBasis)99,
                },
                input with
                {
                    PermittedFields = [(ItineraryFactField)99],
                },
                input with
                {
                    PermittedFields = [ItineraryFactField.Identity],
                },
                input with
                {
                    RightsEvidence = "",
                },
                input with
                {
                    RetrievedAtUtc = input.ProcessedAtUtc.AddTicks(1),
                },
                input with
                {
                    ValidUntil = input.ProcessedAtUtc,
                },
                input with
                {
                    ProcessedAtUtc = input.ProcessedAtUtc.ToOffset(TimeSpan.FromHours(3)),
                },
            }
        )
            invalid.Build().IsError.ShouldBeTrue();
        foreach (
            var url in new[]
            {
                "http://example.test/",
                "https://user:secret@example.test/",
                "https://example.test/#fragment",
                "https://example.test/" + string.Concat(Enumerable.Repeat("😀", 125)),
            }
        )
            (input with { SourceUrl = url }).Build().IsError.ShouldBeTrue();
    }

    [Fact]
    public void Rejects_duplicate_poi_edge_evidence_and_directed_pairs()
    {
        var input = ItineraryTestFacts.Input();
        (input with { Pois = [.. input.Pois.Skip(1), input.Pois[1]] })
            .Build()
            .IsError.ShouldBeTrue();
        (input with { Edges = [.. input.Edges.Skip(1), input.Edges[1]] })
            .Build()
            .IsError.ShouldBeTrue();
        (input with { Evidence = [.. input.Evidence, input.Evidence[0]] })
            .Build()
            .IsError.ShouldBeTrue();
        var other = ItineraryWalkEdge
            .Create(
                "different-id",
                input.Edges[0].FromPoiId,
                input.Edges[0].ToPoiId,
                12,
                22,
                "walk-e59",
                "FICTIONAL"
            )
            .Value;
        (input with { Edges = [.. input.Edges.Take(59), other] }).Build().IsError.ShouldBeTrue();
    }

    [Fact]
    public void Rejects_missing_wrong_scope_revision_or_orphan_identity_evidence()
    {
        var input = ItineraryTestFacts.Input();
        (input with { Evidence = input.Evidence.Skip(1).ToArray() }).Build().IsError.ShouldBeTrue();
        var identity = ItineraryTestFacts.EvidenceParameters(
            "identity-p0",
            ItineraryFactKind.IdentityLocation,
            "p0"
        );
        foreach (
            var replacement in new[]
            {
                identity with
                {
                    ScopeId = "p1",
                },
                identity with
                {
                    SourceUrl = "https://www.wikidata.org/w/index.php?title=Q2&oldid=1",
                },
            }
        )
            (input with { Evidence = [replacement.Build().Value, .. input.Evidence.Skip(1)] })
                .Build()
                .IsError.ShouldBeTrue();
        (
            input with
            {
                Evidence = [.. input.Evidence, (identity with { Id = "orphan" }).Build().Value],
            }
        )
            .Build()
            .IsError.ShouldBeTrue();
    }

    [Fact]
    public void Binds_walk_formula_direction_input_metadata_and_unique_path_ids()
    {
        var input = ItineraryTestFacts.Input();
        var wrongMinutes = ItineraryWalkEdge
            .Create("e0", "p0", "p1", 12, 23, "walk-e0", "FICTIONAL")
            .Value;
        (input with { Edges = [wrongMinutes, .. input.Edges.Skip(1)] })
            .Build()
            .IsError.ShouldBeTrue();
        var alternative = (
            ItineraryTestFacts.SourceInput() with
            {
                InputSha256 = new string('c', 64),
                PublicationInputSha256 = new string('c', 64),
            }
        )
            .Build()
            .Value;
        var badPaths = new[]
        {
            ItineraryTestFacts.PathInput() with
            {
                FromPoiId = "p1",
                ToPoiId = "p0",
            },
            ItineraryTestFacts.PathInput() with
            {
                Source = alternative,
            },
            ItineraryTestFacts.PathInput() with
            {
                PathId = "path-e1",
            },
            ItineraryTestFacts.PathInput() with
            {
                FromAnchorNodeId = 999,
            },
        };
        foreach (var path in badPaths)
        {
            var evidence = ItineraryTestFacts
                .EvidenceParameters("walk-e0", ItineraryFactKind.Walking, "e0", path.Build().Value)
                .Build()
                .Value;
            (
                input with
                {
                    Evidence = input
                        .Evidence.Select(e => e.Id == "walk-e0" ? evidence : e)
                        .ToArray(),
                }
            )
                .Build()
                .IsError.ShouldBeTrue();
        }
    }

    [Fact]
    public void Release_cannot_outlive_model_or_identity_leases_or_fake_build_time()
    {
        var input = ItineraryTestFacts.Input();
        (input with { ReleaseValidUntil = input.ModelSource.ValidUntil.AddTicks(1) })
            .Build()
            .IsError.ShouldBeTrue();
        (input with { ReleaseValidUntil = input.ModelSource.ValidUntil })
            .Build()
            .IsError.ShouldBeFalse();
        (input with { ReleaseValidUntil = ItineraryTestFacts.Now }).Build().IsError.ShouldBeTrue();
        var identity = ItineraryTestFacts.EvidenceParameters(
            "identity-p0",
            ItineraryFactKind.IdentityLocation,
            "p0"
        ) with
        {
            ValidUntil = ItineraryTestFacts.Now.AddDays(1),
        };
        (input with { Evidence = [identity.Build().Value, .. input.Evidence.Skip(1)] })
            .Build()
            .IsError.ShouldBeTrue();
        var preparation = ItineraryDemoPreparation
            .Create(
                "osm-modelled-demo",
                "builder-v1",
                null,
                null,
                "not-evaluated",
                "not-evaluated",
                ItineraryTestFacts.Now.AddTicks(1)
            )
            .Value;
        (input with { Preparation = preparation }).Build().IsError.ShouldBeTrue();
    }

    [Fact]
    public void Unknown_is_not_full_day_and_closed_dates_override_known_weekly_hours()
    {
        var unknown = ItineraryAccess.Create("p0", false, null, [], []).Value;
        var evidence = ItineraryTestFacts
            .EvidenceParameters("access-p0", ItineraryFactKind.Access, "p0")
            .Build()
            .Value;
        var date = new DateOnly(2026, 10, 22);
        unknown.GetIntervalsOn(date, null, ItineraryTestFacts.Destination).ShouldBeNull();
        ItineraryAccess.Create("p0", false, "access-p0", [], []).IsError.ShouldBeTrue();
        var full = ItineraryWeeklyInterval.Create(DayOfWeek.Thursday, 0, 1440).Value;
        var closed = ItineraryAccess.Create("p0", true, "access-p0", [full], [date]).Value;
        closed.GetIntervalsOn(date, evidence, ItineraryTestFacts.Destination)!.ShouldBeEmpty();
        var open = ItineraryAccess.Create("p0", true, "access-p0", [full], []).Value;
        open.GetIntervalsOn(date, evidence, ItineraryTestFacts.Destination)!
            .Single()
            .EndMinute.ShouldBe(1440);
        open.GetIntervalsOn(date.AddDays(1), evidence, ItineraryTestFacts.Destination)!
            .ShouldBeEmpty();
        open.GetIntervalsOn(date.AddDays(10), evidence, ItineraryTestFacts.Destination)
            .ShouldBeNull();
    }

    [Fact]
    public void Whole_local_day_knownness_uses_saved_zone_and_publication_expiry()
    {
        var input = ItineraryTestFacts.EvidenceParameters(
            "access-p0",
            ItineraryFactKind.Access,
            "p0"
        ) with
        {
            ValidUntil = ItineraryTestFacts.Now.AddDays(1).AddHours(21),
        };
        var evidence = input.Build().Value;
        var date = new DateOnly(2026, 10, 22);
        evidence.IsKnownOn(date, CityCatalogue.Find("moscow").Value).ShouldBeTrue();
        evidence.IsKnownOn(date, CityCatalogue.Find("kaliningrad").Value).ShouldBeFalse();
        evidence.IsKnownOn(date.AddDays(-2), ItineraryTestFacts.Destination).ShouldBeFalse();
        evidence.IsKnownOn(date, new("x", "x", "invalid/zone", "1")).ShouldBeFalse();
        evidence.IsKnownOn(DateOnly.MaxValue, ItineraryTestFacts.Destination).ShouldBeFalse();
        var walk = WalkEvidence().Build().Value;
        walk.IsKnownAt(walk.ValidUntil).ShouldBeFalse();
        walk.IsKnownOn(new(2026, 11, 16), ItineraryTestFacts.Destination).ShouldBeTrue();
        walk.IsKnownOn(new(2026, 11, 17), ItineraryTestFacts.Destination).ShouldBeFalse();
    }

    [Fact]
    public void Validates_known_access_scope_and_expires_access_without_expiring_the_package()
    {
        var input = ItineraryTestFacts.Input();
        var access = ItineraryAccess.Create("p0", true, "access-p0", [], []).Value;
        var evidence = ItineraryTestFacts.EvidenceParameters(
            "access-p0",
            ItineraryFactKind.Access,
            "p0"
        );
        var known = input with
        {
            Pois = [ItineraryTestFacts.Poi("p0", access: access), .. input.Pois.Skip(1)],
            Evidence = [.. input.Evidence, evidence.Build().Value],
        };
        known.Build().IsError.ShouldBeFalse();
        access
            .GetIntervalsOn(new(2026, 10, 27), evidence.Build().Value, input.Destination)
            .ShouldBeNull();
        (
            known with
            {
                Evidence = [.. input.Evidence, (evidence with { ScopeId = "p1" }).Build().Value],
            }
        )
            .Build()
            .IsError.ShouldBeTrue();
    }

    [Fact]
    public void Defensively_copies_nested_collections()
    {
        var tags = new[] { "nature" };
        var poi = ItineraryTestFacts.Poi("p0", tags: tags);
        tags[0] = "food";
        poi.Tags.Single().ShouldBe("nature");
        var intervals = new[] { ItineraryWeeklyInterval.Create(DayOfWeek.Monday, 1, 2).Value };
        var dates = new[] { new DateOnly(2026, 1, 1) };
        var access = ItineraryAccess.Create("p0", true, "a", intervals, dates).Value;
        intervals[0] = ItineraryWeeklyInterval.Create(DayOfWeek.Tuesday, 1, 2).Value;
        dates[0] = new(2026, 1, 2);
        access.WeeklyIntervals.Single().Day.ShouldBe(DayOfWeek.Monday);
        access.ClosedDates.Single().ShouldBe(new DateOnly(2026, 1, 1));
        var input = ItineraryTestFacts.Input();
        var result = input.Build();
        result.IsError.ShouldBeFalse();
        var facts = result.Value;
        input.Pois[0] = ItineraryTestFacts.Poi("p999");
        facts.Pois[0].Id.ShouldBe("p0");
        ((IList<ItineraryPoi>)facts.Pois).IsReadOnly.ShouldBeTrue();
        var ei = WalkEvidence();
        var e = ei.Build().Value;
        ei.PermittedFields[0] = ItineraryFactField.OpeningHours;
        e.PermittedFields.ShouldBe([
            ItineraryFactField.WalkingDuration,
            ItineraryFactField.Restrictions,
        ]);
        ((IList<ItineraryFactField>)e.PermittedFields).IsReadOnly.ShouldBeTrue();
    }

    [Fact]
    public void Rejects_incomplete_release_coverage_hash_zone_and_foreign_edges()
    {
        var input = ItineraryTestFacts.Input();
        (input with { Pois = input.Pois.Take(17).ToArray() }).Build().IsError.ShouldBeTrue();
        (input with { Edges = input.Edges.Take(59).ToArray() }).Build().IsError.ShouldBeTrue();
        (
            input with
            {
                Pois = input
                    .Pois.Select(p => ItineraryTestFacts.Poi(p.Id, tags: ["nature"]))
                    .ToArray(),
            }
        )
            .Build()
            .IsError.ShouldBeTrue();
        (input with { DatasetHash = new string('A', 64) }).Build().IsError.ShouldBeTrue();
        (input with { Destination = input.Destination with { TimeZone = "Russian Standard Time" } })
            .Build()
            .IsError.ShouldBeTrue();
        var edge = ItineraryWalkEdge
            .Create("e0", "p0", "foreign", 12, 22, "walk-e0", "FICTIONAL")
            .Value;
        (input with { Edges = [edge, .. input.Edges.Skip(1)] }).Build().IsError.ShouldBeTrue();
    }

    [Fact]
    public void Supports_numeric_version_and_explicit_non_moscow_zone_without_default()
    {
        var result = (
            ItineraryTestFacts.Input() with
            {
                Version = "1",
                Destination = CityCatalogue.Find("kaliningrad").Value,
            }
        ).Build();
        result.IsError.ShouldBeFalse();
        result.Value.Destination.TimeZone.ShouldBe("Europe/Kaliningrad");
    }

    [Fact]
    public void Rejects_invalid_intervals_and_overlapping_or_duplicate_exceptions()
    {
        ItineraryWeeklyInterval.Create((DayOfWeek)9, 0, 60).IsError.ShouldBeTrue();
        ItineraryWeeklyInterval.Create(DayOfWeek.Monday, 60, 60).IsError.ShouldBeTrue();
        ItineraryWeeklyInterval.Create(DayOfWeek.Monday, -1, 60).IsError.ShouldBeTrue();
        ItineraryWeeklyInterval.Create(DayOfWeek.Monday, 0, 1441).IsError.ShouldBeTrue();
        var a = ItineraryWeeklyInterval.Create(DayOfWeek.Monday, 10, 90).Value;
        var b = ItineraryWeeklyInterval.Create(DayOfWeek.Monday, 80, 120).Value;
        ItineraryAccess.Create("p0", true, "a", [a, b], []).IsError.ShouldBeTrue();
        ItineraryAccess
            .Create("p0", true, "a", [], [new(2026, 1, 1), new(2026, 1, 1)])
            .IsError.ShouldBeTrue();
    }

    [Fact]
    public void Rejects_unbounded_poi_text_dwell_coordinates_and_walk_durations()
    {
        var access = ItineraryAccess.Create("p", false, null, [], []).Value;
        foreach (var dwell in new[] { 14, 121 })
            ItineraryPoi
                .Create("p", "Fictional", ["nature"], dwell, "Q1", "1", "f", 1m, 1m, access)
                .IsError.ShouldBeTrue();
        foreach (var title in new[] { "", "bad\n", "\ud800", new string('x', 101) })
            ItineraryPoi
                .Create("p", title, ["nature"], 30, "Q1", "1", "f", 1m, 1m, access)
                .IsError.ShouldBeTrue();
        ItineraryPoi
            .Create("p", "Fictional", ["food"], 30, "Q1", "1", "f", 91m, 1m, access)
            .IsError.ShouldBeTrue();
        ItineraryWalkEdge.Create("e", "p", "p", 1, 2, "f", "fictional").IsError.ShouldBeTrue();
        ItineraryWalkEdge.Create("e", "p", "q", 0, 2, "f", "fictional").IsError.ShouldBeTrue();
        ItineraryWalkEdge.Create("e", "p", "q", 2, 1, "f", "fictional").IsError.ShouldBeTrue();
        ItineraryWalkEdge.Create("e", "p", "q", 1, 121, "f", "fictional").IsError.ShouldBeTrue();
    }

    [Fact]
    public void Null_inputs_fail_as_stable_validation_errors()
    {
        var result = ValidatedItineraryFacts.Create(
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            default,
            null,
            null,
            null
        );
        result.IsError.ShouldBeTrue();
        result.FirstError.Code.ShouldBe("Trips.InvalidInput");
        ItineraryModelledWalk
            .Create(null, null, null, null, null, 0, 0, 0, 0)
            .IsError.ShouldBeTrue();
        ItineraryPoi
            .Create(null, null, null, 30, null, null, null, 0, 0, null)
            .IsError.ShouldBeTrue();
        ItineraryAccess.Create(null, false, null, null, null).IsError.ShouldBeTrue();
    }

    [Fact]
    public void Zero_length_is_only_a_shared_anchor_and_a_shared_anchor_is_not_a_positive_path()
    {
        var input = ItineraryTestFacts.PathInput();
        (input with { PathLengthMillimetres = 0 }).Build().IsError.ShouldBeTrue();
        (input with { ToAnchorNodeId = input.FromAnchorNodeId }).Build().IsError.ShouldBeTrue();
        var valid = (
            input with
            {
                PathLengthMillimetres = 0,
                ToAnchorNodeId = input.FromAnchorNodeId,
                CrossingCount = 1,
            }
        ).Build();
        valid.IsError.ShouldBeFalse();
        valid.Value.MinimumMinutes.ShouldBe(1);
        valid.Value.MaximumMinutes.ShouldBe(1);
    }

    [Fact]
    public void One_path_digest_cannot_claim_conflicting_anchor_or_model_inputs()
    {
        var input = ItineraryTestFacts.Input();
        var second = input.Evidence.Single(e => e.Id == "walk-e1").ModelledWalk!;
        var altered = (ItineraryTestFacts.PathInput() with { PathHash = second.PathHash })
            .Build()
            .Value;
        var first = ItineraryTestFacts
            .EvidenceParameters("walk-e0", ItineraryFactKind.Walking, "e0", altered)
            .Build()
            .Value;
        (
            input with
            {
                Evidence = input.Evidence.Select(e => e.Id == "walk-e0" ? first : e).ToArray(),
            }
        )
            .Build()
            .IsError.ShouldBeTrue();
    }

    [Fact]
    public void Accepts_48_unique_pois_but_rejects_49_without_relying_on_duplicate_checks()
    {
        var input = ItineraryTestFacts.Input();
        var pois = Enumerable.Range(0, 49).Select(i => ItineraryTestFacts.Poi("p" + i)).ToArray();
        var identity = pois.Select(p =>
                ItineraryTestFacts
                    .EvidenceParameters(
                        p.IdentityEvidenceId,
                        ItineraryFactKind.IdentityLocation,
                        p.Id
                    )
                    .Build()
                    .Value
            )
            .ToArray();
        var walks = input.Evidence.Where(e => e.Kind == ItineraryFactKind.Walking).ToArray();
        (input with { Pois = pois.Take(48).ToArray(), Evidence = [.. identity.Take(48), .. walks] })
            .Build()
            .IsError.ShouldBeFalse();
        (input with { Pois = pois, Evidence = [.. identity, .. walks] })
            .Build()
            .IsError.ShouldBeTrue();
    }

    [Fact]
    public void Accepts_256_unique_directed_edges_but_rejects_257_without_duplicate_pairs()
    {
        var input = ItineraryTestFacts.Input();
        var paths = Enumerable
            .Range(0, 18)
            .SelectMany(from =>
                Enumerable
                    .Range(0, 18)
                    .Where(to => to != from)
                    .Select(to => (From: "p" + from, To: "p" + to))
            )
            .Take(257)
            .Select(
                (pair, i) =>
                    ItineraryTestFacts
                        .PathInput("path-bound" + i, pair.From, pair.To, input.ModelSource)
                        .Build()
                        .Value
            )
            .ToArray();
        var edges = paths
            .Select(
                (p, i) =>
                    ItineraryWalkEdge
                        .Create(
                            "bound" + i,
                            p.FromPoiId,
                            p.ToPoiId,
                            p.MinimumMinutes,
                            p.MaximumMinutes,
                            "walk-bound" + i,
                            "FICTIONAL upper-bound fixture"
                        )
                        .Value
            )
            .ToArray();
        var evidence = paths
            .Select(
                (p, i) =>
                    ItineraryTestFacts
                        .EvidenceParameters(
                            "walk-bound" + i,
                            ItineraryFactKind.Walking,
                            "bound" + i,
                            p
                        )
                        .Build()
                        .Value
            )
            .ToArray();
        var identity = input
            .Evidence.Where(e => e.Kind == ItineraryFactKind.IdentityLocation)
            .ToArray();
        (
            input with
            {
                Edges = edges.Take(256).ToArray(),
                Evidence = [.. identity, .. evidence.Take(256)],
            }
        )
            .Build()
            .IsError.ShouldBeFalse();
        (input with { Edges = edges, Evidence = [.. identity, .. evidence] })
            .Build()
            .IsError.ShouldBeTrue();
    }

    private static ItineraryTestFacts.EvidenceInput WalkEvidence() =>
        ItineraryTestFacts.EvidenceParameters(
            "walk-e0",
            ItineraryFactKind.Walking,
            "e0",
            ItineraryTestFacts.PathInput().Build().Value
        );
}
