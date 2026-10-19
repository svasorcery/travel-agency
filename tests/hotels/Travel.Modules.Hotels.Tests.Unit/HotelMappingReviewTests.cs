using System.Globalization;
using Shouldly;
using Travel.Modules.Hotels.Application;
using Travel.Modules.Hotels.Core;

namespace Travel.Modules.Hotels.Tests.Unit;

public sealed class HotelMappingReviewTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse(
        "2026-10-19T12:00:00Z",
        CultureInfo.InvariantCulture
    );

    [Theory]
    [InlineData("evidence")]
    [InlineData("reviewer")]
    [InlineData("fingerprint")]
    [InlineData("other-profile")]
    [InlineData("replace-evidence")]
    [InlineData("replace-reviewer")]
    [InlineData("replace-fingerprint")]
    [InlineData("replace-member")]
    [InlineData("replace-check-time")]
    public void Retained_quote_cannot_use_a_pair_that_is_no_longer_valid_reviewed_evidence(
        string change
    )
    {
        var pair = Pair();
        var binding = Binding(pair);
        new HotelReviewedMap([pair]).IsCurrent(binding, Now).ShouldBeTrue();
        var changed = change switch
        {
            "evidence" => pair with { Evidence = "" },
            "reviewer" => pair with { Reviewer = "" },
            "fingerprint" => pair with { LeftPropertyFingerprint = null },
            "other-profile" => pair with
            {
                Right = pair.Right with
                {
                    Source = pair.Right.Source with { Profile = "External" },
                },
            },
            "replace-evidence" => pair with
            {
                Evidence = "Another independently reviewed building record",
            },
            "replace-reviewer" => pair with { Reviewer = "another-reviewer" },
            "replace-fingerprint" => pair with { RightPropertyFingerprint = new string('a', 64) },
            "replace-member" => pair with
            {
                Right = pair.Right with { PropertyId = "another-hotel" },
            },
            "replace-check-time" => pair with { CheckedAt = pair.CheckedAt.AddHours(1) },
            _ => throw new InvalidOperationException(),
        };
        new HotelReviewedMap([changed]).IsCurrent(binding, Now).ShouldBeFalse();
    }

    [Fact]
    public void Duplicate_pair_ids_refuse_current_admission_without_throwing()
    {
        var pair = Pair();
        var duplicate = pair with
        {
            Left = pair.Left with { PropertyId = "other-left" },
            Right = pair.Right with { PropertyId = "other-right" },
        };
        new HotelReviewedMap([pair, duplicate]).IsCurrent(Binding(pair), Now).ShouldBeFalse();
    }

    private static ReviewedHotelPair Pair()
    {
        var left = new HotelProperty("owned-left", "Owned House", "1 Owned Square", null, null);
        var right = new HotelProperty("owned-right", "Owned House", "1 Owned Square", null, null);
        return new(
            "owned-pair",
            "review-1",
            new(new("duffel-stays", "owned", "Test", "Fictional"), left.Id),
            new(new("hotelbeds", "owned", "Test", "Fictional"), right.Id),
            "independently reviewed fictional address/building records",
            "owner",
            "same owned fictional building",
            Now.AddDays(-1),
            Now.AddMinutes(5),
            "Fictional",
            HotelPropertyMatching.PropertyFingerprint(left),
            HotelPropertyMatching.PropertyFingerprint(right)
        );
    }

    private static HotelAdmissionBinding Binding(ReviewedHotelPair pair) =>
        new(
            2,
            pair.Left.Source,
            "config-1",
            "Net",
            "owned-review-1",
            pair.Id,
            pair.Revision,
            Now.AddMinutes(2),
            MappingEvidenceDigest: HotelPropertyMatching.MappingDigest(pair)
        );
}
