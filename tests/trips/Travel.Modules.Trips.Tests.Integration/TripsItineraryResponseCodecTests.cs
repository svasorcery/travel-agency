using System.Text;
using System.Text.Json;
using Shouldly;
using Travel.Modules.Trips.Api.Contracts;
using Travel.Modules.Trips.Application.Itineraries;
using Travel.Modules.Trips.Core;
using Travel.Modules.Trips.Core.Itineraries;
using Xunit;

namespace Travel.Modules.Trips.Tests.Integration;

public sealed class TripsItineraryResponseCodecTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ItineraryOutcome Outcome(ValidatedItineraryFacts facts, int days = 1) =>
        ItineraryPolicy
            .Generate(
                TripBrief
                    .Create(
                        new TripBriefInput(
                            "moscow",
                            null,
                            new(2026, 10, 22),
                            new DateOnly(2026, 10, 22).AddDays(days - 1),
                            2,
                            "RUB",
                            null,
                            ["architecture"],
                            "active"
                        )
                    )
                    .Value,
                facts.Destination,
                facts,
                FictionalItineraryFacts.Now,
                Ct
            )
            .Value;

    [Theory]
    [InlineData("title")]
    [InlineData("note")]
    [InlineData("utf16")]
    [InlineData("time")]
    [InlineData("estimate")]
    [InlineData("identity")]
    [InlineData("unknown")]
    [InlineData("duplicate-block")]
    [InlineData("edge-direction")]
    [InlineData("edge-bounds")]
    [InlineData("edge-reference")]
    [InlineData("missing-edge")]
    public async Task Corrupt_projection_is_rejected_whole_without_truncation_or_repair(
        string mutation
    )
    {
        var facts = FictionalItineraryFacts.Create();
        var outcome = Outcome(facts);
        var day = outcome.Days[0];
        var first = day.Blocks[0];
        var replacement = new ItineraryCandidateBlock(
            first.PoiId,
            first.ProposedBlockId,
            mutation == "title" ? "invented" : first.Title,
            first.Date,
            mutation == "time" ? "10:00" : null,
            null,
            mutation == "estimate" ? PlanningMoney.Create("1.00", "RUB").Value : null,
            mutation == "note" ? new string('x', 501)
                : mutation == "utf16" ? "\ud800"
                : first.Note,
            mutation == "identity"
                    ?
                    [
                        new(
                            ItineraryFactKind.IdentityLocation,
                            ItineraryFactState.Known,
                            "invented",
                            null
                        ),
                        first.Facts[1],
                    ]
                : mutation == "unknown"
                    ?
                    [
                        first.Facts[0],
                        new(ItineraryFactKind.Access, ItineraryFactState.Unknown, null, "invented"),
                    ]
                : first.Facts,
            first.OpeningIntervals
        );
        var blocks = day.Blocks.ToArray();
        blocks[0] = replacement;
        if (mutation == "duplicate-block")
            blocks[1] = replacement;
        var edges = day.Edges.ToArray();
        var edge = edges[0];
        if (mutation.StartsWith("edge-"))
            edges[0] = new ItineraryCandidateEdge(
                edge.EdgeId,
                mutation == "edge-direction" ? edge.ToPoiId : edge.FromPoiId,
                mutation == "edge-direction" ? edge.FromPoiId : edge.ToPoiId,
                edge.MinimumMinutes,
                mutation == "edge-bounds" ? edge.MaximumMinutes + 1 : edge.MaximumMinutes,
                mutation == "edge-reference" ? "invented" : edge.EvidenceId,
                edge.Restrictions,
                edge.Method,
                edge.RightsBasis,
                edge.ModelledWalk
            );
        if (mutation == "missing-edge")
            edges = [];
        var changed = Copy(outcome, [new(day.Date, blocks, edges)]);
        var result = await new ItineraryCandidateResponseEncoder().EncodeAsync(
            new ComputedCandidateResult(1, changed, facts),
            Ct
        );
        result.IsError.ShouldBeTrue();
        result.FirstError.Code.ShouldBe("Trips.ItineraryUnavailable");
    }

    [Fact]
    public async Task Escaped_multibyte_evidence_counts_in_complete_utf8_limit()
    {
        var input = FictionalItineraryFacts.Input();
        var rights = string.Concat(Enumerable.Repeat("😀", 500));
        var evidence = input
            .Evidence.Select(e =>
                ItineraryEvidence
                    .Create(
                        e.Id,
                        e.Kind,
                        e.ScopeId,
                        e.Method,
                        e.RightsBasis,
                        rights,
                        e.SourceUrl,
                        e.RetrievedAtUtc,
                        e.ProcessedAtUtc,
                        e.ObservedAt,
                        e.ValidUntil,
                        e.PermittedFields,
                        e.ModelledWalk
                    )
                    .Value
            )
            .ToArray();
        var made = (input with { Evidence = evidence }).Build();
        made.IsError.ShouldBeFalse();
        var outcome = Outcome(made.Value, 3);
        outcome.Days.Sum(d => d.Blocks.Count).ShouldBe(12);
        var result = await new ItineraryCandidateResponseEncoder().EncodeAsync(
            new ComputedCandidateResult(1, outcome, made.Value),
            Ct
        );
        result.IsError.ShouldBeTrue();
        result.FirstError.Code.ShouldBe("Trips.ItineraryUnavailable");
    }

    [Fact]
    public async Task Original_titles_unknowns_integer_strings_and_metadata_are_preserved()
    {
        var facts = FictionalItineraryFacts.Create();
        var outcome = Outcome(facts);
        var result = await new ItineraryCandidateResponseEncoder().EncodeAsync(
            new ComputedCandidateResult(long.MaxValue, outcome, facts),
            Ct
        );
        result.IsError.ShouldBeFalse();
        using var json = JsonDocument.Parse(
            result.Value,
            new JsonDocumentOptions { MaxDepth = 16 }
        );
        var root = json.RootElement;
        root.GetProperty("baseRevision")
            .GetString()
            .ShouldBe(long.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture));
        root.GetProperty("derivedDataOffer")
            .GetString()
            .ShouldBe(ItineraryCandidateResponseEncoder.DerivedDataOffer);
        var block = root.GetProperty("days")[0].GetProperty("blocks")[0];
        block.GetProperty("title").GetString().ShouldBe(outcome.Days[0].Blocks[0].Title);
        block.GetProperty("startTime").ValueKind.ShouldBe(JsonValueKind.Null);
        block.GetProperty("estimate").ValueKind.ShouldBe(JsonValueKind.Null);
        root.GetProperty("preparation")
            .GetProperty("humanReviewer")
            .ValueKind.ShouldBe(JsonValueKind.Null);
        root.GetProperty("modelSource")
            .GetProperty("assumptions")
            .GetString()!
            .ShouldContain("not observations");
        var walk = root.GetProperty("days")[0].GetProperty("edges")[0].GetProperty("modelledWalk");
        walk.GetProperty("pathLengthMillimetres").ValueKind.ShouldBe(JsonValueKind.String);
        walk.GetProperty("fromAnchorNodeId").ValueKind.ShouldBe(JsonValueKind.String);
        root.GetProperty("evidence").GetArrayLength().ShouldBe(7);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Invalid_base_revision_fails_closed(long revision)
    {
        var result = await new ItineraryCandidateResponseEncoder().EncodeAsync(
            new ScopeUnsupportedCandidateResult(
                revision,
                FictionalItineraryFacts.Destination,
                "destinationUnsupported",
                ItineraryPolicy.PolicyVersion,
                FictionalItineraryFacts.Now
            ),
            Ct
        );
        result.IsError.ShouldBeTrue();
    }

    private static ItineraryOutcome Copy(
        ItineraryOutcome o,
        IReadOnlyList<ItineraryCandidateDay> days
    ) =>
        new(
            o.Kind,
            o.Destination,
            o.DatasetId,
            o.DatasetVersion,
            o.DatasetHash,
            o.PolicyVersion,
            o.GeneratedAt,
            o.ExpiresAt,
            o.Preparation,
            o.ModelSource,
            days,
            o.Coverage,
            o.CoveredInterests,
            o.UnmetInterests,
            o.Reasons,
            o.Exclusions,
            o.Uncertainties,
            o.SearchLimited,
            o.ExpansionCount
        );
}
