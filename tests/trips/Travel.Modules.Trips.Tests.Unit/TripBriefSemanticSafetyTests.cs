using System.Text.Json;
using Shouldly;
using Travel.IntegrationContracts.AI.TripsBriefExtraction;
using Travel.Modules.Trips.Application.BriefExtraction;

namespace Travel.Modules.Trips.Tests.Unit;

public class TripBriefSemanticSafetyTests
{
    [Theory]
    [InlineData("Москва, не интересуют история и архитектура и природа.")]
    [InlineData("Москва, не интересуют история, архитектура и природа.")]
    [InlineData("Москва, не интересуют история и живопись и архитектура и природа.")]
    public void Entire_finite_negative_interest_list_cannot_supply_a_positive_tag(string text)
    {
        var input = TripBriefCandidateValidatorTests.Input(text);
        TripBriefCandidateValidator.Analyze(input).Fields.Interests.State.ShouldBe("missing");
        var hostile = TripBriefCandidateValidatorTests.Reply(
            input,
            interests: TripBriefCandidateValidatorTests.Extracted(
                text,
                "природа",
                new[] { "nature" }
            )
        );
        TripBriefCandidateValidator.Validate(input, hostile).IsError.ShouldBeTrue();
    }

    [Fact]
    public void Explicit_positive_clause_resets_negative_interest_list()
    {
        const string text = "Москва, не интересуют история и архитектура, хочу природу.";
        var input = TripBriefCandidateValidatorTests.Input(text);
        var analyzed = TripBriefCandidateValidator.Analyze(input);
        analyzed
            .Fields.Interests.Value!.Value.EnumerateArray()
            .Select(value => value.GetString())
            .ShouldBe(new[] { "nature" });
        TripBriefCandidateValidator
            .Validate(
                input,
                TripBriefCandidateValidatorTests.Reply(
                    input,
                    interests: TripBriefCandidateValidatorTests.Extracted(
                        text,
                        "природу",
                        new[] { "nature" }
                    )
                )
            )
            .IsError.ShouldBeFalse();
    }

    [Fact]
    public void Negated_duration_never_supplies_days_or_a_derived_end()
    {
        const string text = "Москва, с 2026-11-01, не на 3 дня.";
        var input = TripBriefCandidateValidatorTests.Input(text);
        var analyzed = TripBriefCandidateValidator.Analyze(input);
        analyzed.Fields.StartDate.Value!.Value.GetString().ShouldBe("2026-11-01");
        analyzed.Fields.DurationDays.State.ShouldBe("missing");
        analyzed.Fields.EndDate.State.ShouldBe("missing");
        var hostile = TripBriefCandidateValidatorTests.Reply(
            input,
            start: TripBriefCandidateValidatorTests.Extracted(text, "2026-11-01", "2026-11-01"),
            duration: TripBriefCandidateValidatorTests.Extracted(text, "на 3 дня", 3),
            end: TripBriefCandidateValidatorTests.Extracted(text, text, "2026-11-03")
        );
        TripBriefCandidateValidator.Validate(input, hostile).IsError.ShouldBeTrue();
    }

    [Theory]
    [InlineData("Москва, с 2026-11-01, на 3 дня.", 3, "2026-11-03")]
    [InlineData("Москва, с 2026-11-01, не на 3 дня, а на 2 дня.", 2, "2026-11-02")]
    public void Positive_duration_and_explicit_alternative_preserve_inclusive_derivation(
        string text,
        int days,
        string end
    )
    {
        var input = TripBriefCandidateValidatorTests.Input(text);
        var analyzed = TripBriefCandidateValidator.Analyze(input);
        analyzed.Fields.DurationDays.Value!.Value.GetInt32().ShouldBe(days);
        analyzed.Fields.EndDate.Value!.Value.GetString().ShouldBe(end);
        var reply = new TripsBriefParsed(
            input.RequestId,
            input.ProcessingPolicyVersion,
            "Ready",
            analyzed.Fields,
            analyzed.Issues
        );
        TripBriefCandidateValidator.Validate(input, reply).IsError.ShouldBeFalse();
    }

    [Theory]
    [InlineData("Москва, с 2026-11-01 (пятница) по 2026-11-03.")]
    [InlineData("Москва, с 2026-11-01 по 2026-11-03 (пятница).")]
    [InlineData("Москва, с 2026-11-01 по пятницу 2026-11-03.")]
    public void Weekday_contradiction_on_either_date_blocks_full_range_and_hostile_claim(
        string text
    )
    {
        var input = TripBriefCandidateValidatorTests.Input(text);
        var analyzed = TripBriefCandidateValidator.Analyze(input);
        analyzed.Fields.StartDate.State.ShouldBe("ambiguous");
        analyzed.Fields.EndDate.State.ShouldBe("ambiguous");
        analyzed.Issues.Select(issue => issue.Code).ShouldContain("weekdayDateConflict");
        var hostile = TripBriefCandidateValidatorTests.Reply(
            input,
            start: TripBriefCandidateValidatorTests.Extracted(text, "2026-11-01", "2026-11-01"),
            end: TripBriefCandidateValidatorTests.Extracted(text, "2026-11-03", "2026-11-03")
        );
        var checkedCandidate = TripBriefCandidateValidator.Validate(input, hostile);
        checkedCandidate.IsError.ShouldBeFalse();
        checkedCandidate.Value.Fields.StartDate.State.ShouldBe("ambiguous");
        checkedCandidate.Value.Fields.EndDate.State.ShouldBe("ambiguous");
        checkedCandidate
            .Value.Issues.Select(issue => issue.Code)
            .ShouldContain("weekdayDateConflict");
    }

    [Theory]
    [InlineData("Москва, с 2026-11-01 (воскресенье) по 2026-11-03 (вторник).")]
    [InlineData("Москва, с воскресенья 2026-11-01 по вторник 2026-11-03.")]
    public void Matching_weekdays_are_attached_to_their_own_date(string text)
    {
        var input = TripBriefCandidateValidatorTests.Input(text);
        var analyzed = TripBriefCandidateValidator.Analyze(input);
        analyzed.Fields.StartDate.Value!.Value.GetString().ShouldBe("2026-11-01");
        analyzed.Fields.EndDate.Value!.Value.GetString().ShouldBe("2026-11-03");
        analyzed.Issues.Select(issue => issue.Code).ShouldNotContain("weekdayDateConflict");
        TripBriefCandidateValidator
            .Validate(
                input,
                new TripsBriefParsed(
                    input.RequestId,
                    input.ProcessingPolicyVersion,
                    "Ready",
                    analyzed.Fields,
                    analyzed.Issues
                )
            )
            .IsError.ShouldBeFalse();
    }
}
