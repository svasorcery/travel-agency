using System.Globalization;
using System.Text.Json;
using Shouldly;
using Travel.Modules.Hotels.Application;
using Travel.Modules.Hotels.Core;

namespace Travel.Modules.Hotels.Tests.Unit;

public sealed class HotelMatchingRankingTests
{
    [Fact]
    public void Distinct_normalized_conditions_are_a_stable_material_tie_break()
    {
        var a = Offer("a", "hotel", 200, "GBP");
        var b = a with
        {
            SelectionKey = "other-token",
            Terms = a.Terms with { Conditions = [new("Arrive before 20:00", "WholeStay")] },
        };
        HotelCostRanking.Fingerprint(a).ShouldNotBe(HotelCostRanking.Fingerprint(b));
        var forward = HotelCostRanking.Rank(HotelPropertyMatching.Group([a, b], [], Now));
        var reverse = HotelCostRanking.Rank(HotelPropertyMatching.Group([b, a], [], Now));
        forward
            .SelectMany(g => g.Offers)
            .Select(o => HotelCostRanking.Fingerprint(o))
            .ShouldBe(
                reverse.SelectMany(g => g.Offers).Select(o => HotelCostRanking.Fingerprint(o))
            );
    }

    [Fact]
    public void Same_mapping_id_revision_cannot_replace_material_review_evidence()
    {
        var a = Offer("a", "a", 200, "GBP");
        var b = Offer("b", "b", 200, "GBP");
        var pair = Pair(a, b);
        var binding = new HotelAdmissionBinding(
            2,
            a.Source!,
            "c1",
            "Net",
            "owned",
            pair.Id,
            pair.Revision,
            Now.AddMinutes(1),
            MappingEvidenceDigest: HotelPropertyMatching.MappingDigest(pair)
        );
        new HotelReviewedMap([pair]).IsCurrent(binding, Now).ShouldBeTrue();
        new HotelReviewedMap([pair with { Evidence = "Other reviewed building evidence" }])
            .IsCurrent(binding, Now)
            .ShouldBeFalse();
        new HotelReviewedMap([
            pair with
            {
                Right = pair.Right with { PropertyId = "other" },
                RightPropertyFingerprint = new string('a', 64),
            },
        ])
            .IsCurrent(binding, Now)
            .ShouldBeFalse();
    }

    [Fact]
    public void Public_group_projection_never_exposes_review_notes_or_reviewer()
    {
        var a = Offer("a", "a", 200, "GBP");
        var b = Offer("b", "b", 200, "GBP");
        var pair = Pair(a, b) with
        {
            Reviewer = "private-reviewer-sentinel",
            Evidence = "private-evidence-sentinel",
        };
        var groups = HotelPropertyMatching.Group([a, b], [pair], Now);
        groups.Single().Mapping.ShouldBe(pair);
        var json = JsonSerializer.Serialize(groups);
        json.ShouldNotContain("private-reviewer-sentinel");
        json.ShouldNotContain("private-evidence-sentinel");
    }

    [Fact]
    public void Current_address_conflict_and_expired_evidence_have_distinct_reasons()
    {
        var a = Offer("a", "a", 200, "GBP");
        var b = Offer("b", "b", 200, "GBP");
        var pair = Pair(a, b) with
        {
            LeftPropertyFingerprint = HotelPropertyMatching.PropertyFingerprint(a.Terms.Property),
            RightPropertyFingerprint = HotelPropertyMatching.PropertyFingerprint(b.Terms.Property),
        };
        var changed = a with
        {
            Terms = a.Terms with
            {
                Property = a.Terms.Property with { Address = "Different building" },
            },
        };
        HotelPropertyMatching
            .Group([changed, b], [pair], Now)
            .ShouldAllBe(g => g.MatchReason == "EvidenceConflict");
        HotelPropertyMatching
            .Group([a, b], [pair with { ValidUntil = Now }], Now)
            .ShouldAllBe(g => g.MatchReason == "MappingExpired");
    }

    private static readonly DateTimeOffset Now = DateTimeOffset.Parse(
        "2026-10-19T12:00:00Z",
        CultureInfo.InvariantCulture
    );

    [Fact]
    public void Only_current_reviewed_exact_namespaces_merge_and_never_deduplicate_source_rates()
    {
        var a = Offer("duffel-stays", "same", 200, "GBP");
        var b = Offer("hotelbeds", "same", 190, "GBP");
        var pair = Pair(a, b);
        var matched = HotelPropertyMatching.Group([a, b], [pair], Now);
        matched.Length.ShouldBe(1);
        matched[0].Offers.Length.ShouldBe(2);
        matched[0].Mapping.ShouldBe(pair);
        HotelPropertyMatching.Group([a, b], [], Now).Length.ShouldBe(2);
        HotelPropertyMatching
            .Group([a, b], [pair with { ValidUntil = Now }], Now)
            .Length.ShouldBe(2);
        HotelPropertyMatching
            .Group([a, b with { Source = b.Source! with { Account = "other" } }], [pair], Now)
            .Length.ShouldBe(2);
    }

    [Fact]
    public void Duplicate_mapping_membership_is_rejected_without_transitive_merge()
    {
        var a = Offer("a", "a", 200, "GBP");
        var b = Offer("b", "b", 190, "GBP");
        var c = Offer("c", "c", 180, "GBP");
        var groups = HotelPropertyMatching.Group(
            [a, b, c],
            [Pair(a, b), Pair(b, c) with { Id = "other" }],
            Now
        );
        groups.Length.ShouldBe(3);
        groups.ShouldAllBe(g => g.MatchReason == "MappingConflict");
    }

    [Fact]
    public void One_group_has_scoped_minima_and_primary_currency_is_navigation_only()
    {
        var a = Offer("a", "a", 200, "GBP");
        var b = Offer("b", "b", 300, "EUR");
        var c = Offer("c", "c", 100, "GBP");
        var groups = HotelCostRanking.Rank(
            HotelPropertyMatching.Group([c, b, a], [Pair(a, b)], Now)
        );
        groups.Length.ShouldBe(2);
        groups[0].PrimaryBucket.ShouldBe("SupplierNet:EUR");
        groups[0].Minima.Length.ShouldBe(2);
        groups[0].Offers.Length.ShouldBe(2);
        groups[1].PrimaryBucket.ShouldBe("SupplierNet:GBP");
        groups[0].Offers[0].Ranking!.PolicyVersion.ShouldBe("hotel-cost-first-v1");
        groups[0].Offers[0].Ranking!.Scope.ShouldBe("WholeStay");
        groups[0].Offers[0].Ranking!.Reason.ShouldBe("KnownWholeStayObligations");
    }

    [Fact]
    public void Semantic_order_ignores_arrival_tokens_culture_and_unknown_prices()
    {
        var a = Offer("a", "a", 200, "GBP");
        var b = Offer("b", "b", 200, "GBP");
        var unknown = Offer("c", "c", 1, "GBP") with { Price = null };
        var first = HotelCostRanking.Rank(HotelPropertyMatching.Group([b, unknown, a], [], Now));
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            var second = HotelCostRanking.Rank(
                HotelPropertyMatching.Group(
                    [
                        a with
                        {
                            SelectionKey = "random-z",
                        },
                        b with
                        {
                            SelectionKey = "random-a",
                        },
                        unknown,
                    ],
                    [],
                    Now
                )
            );
            second.Select(g => g.Key).ShouldBe(first.Select(g => g.Key));
            second.SelectMany(g => g.Offers).Select(o => o.Provider).ShouldBe(["a", "b", "c"]);
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }

    private static HotelOffer Offer(string provider, string id, decimal amount, string currency) =>
        new(
            provider,
            "Test",
            Guid.NewGuid().ToString("N"),
            ReservationStateTests.Terms() with
            {
                Property = new(id, "Same name", null, null, null),
                Total = new(amount, currency),
            },
            null,
            true,
            null
        )
        {
            Source = new(provider, "owned", "Test", "Fictional"),
            Price = new("SupplierNet", new(amount, currency), null, "WholeStay", provider, [], []),
            RoomIdentity = "room-code",
        };

    private static ReviewedHotelPair Pair(HotelOffer a, HotelOffer b) =>
        new(
            "pair",
            "revision-1",
            new(a.Source!, a.Terms.Property.Id),
            new(b.Source!, b.Terms.Property.Id),
            "owned-address-and-building-evidence",
            "owner",
            "same fictional building",
            Now.AddDays(-1),
            Now.AddMinutes(5),
            "Fictional",
            HotelPropertyMatching.PropertyFingerprint(a.Terms.Property),
            HotelPropertyMatching.PropertyFingerprint(b.Terms.Property)
        );
}
