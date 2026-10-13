using Shouldly;
using Travel.Modules.Support.Core;

namespace Travel.Modules.Support.Tests.Unit;

public sealed class SupportClassificationPolicyTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-13T12:00:00Z");

    [Theory]
    [InlineData(ManualCategory.Payment)]
    [InlineData(ManualCategory.Ticketing)]
    [InlineData(ManualCategory.Cancellation)]
    [InlineData(ManualCategory.Refund)]
    [InlineData(ManualCategory.FareConditions)]
    [InlineData(ManualCategory.Ancillaries)]
    [InlineData(ManualCategory.Technical)]
    [InlineData(ManualCategory.Other)]
    public void Fixed_categories_are_eligible_candidates(ManualCategory category)
    {
        SupportClassificationPolicy.IsCandidate(category).ShouldBeTrue();
    }

    [Theory]
    [InlineData(ManualCategory.Unclassified)]
    [InlineData((ManualCategory)(-1))]
    [InlineData((ManualCategory)9)]
    public void Manual_fallback_and_undefined_categories_are_not_candidates(ManualCategory category)
    {
        SupportClassificationPolicy.IsCandidate(category).ShouldBeFalse();
    }

    [Theory]
    [InlineData("2026-10-13T12:00:00.0000001Z", SupportUrgencyKind.High)]
    [InlineData("2026-10-14T12:00:00Z", SupportUrgencyKind.High)]
    [InlineData("2026-10-14T12:00:00.0000001Z", SupportUrgencyKind.KeepCurrent)]
    [InlineData("2026-10-13T12:00:00Z", SupportUrgencyKind.Unknown)]
    [InlineData("2026-10-13T11:59:59.9999999Z", SupportUrgencyKind.Unknown)]
    public void Active_journey_uses_the_future_departure_and_inclusive_24_hour_boundary(
        string departure,
        SupportUrgencyKind expected
    )
    {
        var advice = Recommend(nextDeparture: DateTimeOffset.Parse(departure));

        advice.Kind.ShouldBe(expected);
    }

    [Theory]
    [InlineData("2026-10-13T12:00:00Z", SupportUrgencyKind.High)]
    [InlineData("2026-10-13T11:59:30Z", SupportUrgencyKind.High)]
    [InlineData("2026-10-13T11:59:29.9999999Z", SupportUrgencyKind.Unknown)]
    [InlineData("2026-10-13T12:00:00.0000001Z", SupportUrgencyKind.Unknown)]
    public void Observation_must_not_be_future_or_more_than_30_seconds_old(
        string observation,
        SupportUrgencyKind expected
    )
    {
        var advice = Recommend(observedAt: DateTimeOffset.Parse(observation));

        advice.Kind.ShouldBe(expected);
    }

    [Fact]
    public void Equivalent_offset_timestamps_use_the_same_instant()
    {
        var advice = Recommend(
            nextDeparture: DateTimeOffset.Parse("2026-10-14T15:00:00+03:00"),
            observedAt: DateTimeOffset.Parse("2026-10-13T15:00:00+03:00")
        );

        advice.Kind.ShouldBe(SupportUrgencyKind.High);
    }

    [Theory]
    [InlineData(SupportPriority.Normal)]
    [InlineData(SupportPriority.High)]
    public void Active_journey_without_a_departure_remains_unknown(SupportPriority current)
    {
        SupportClassificationPolicy
            .RecommendUrgency(current, SupportJourneyState.Active, null, Now, 7, Now)
            .Kind.ShouldBe(SupportUrgencyKind.Unknown);
    }

    [Theory]
    [InlineData(SupportPriority.Normal)]
    [InlineData(SupportPriority.High)]
    public void Closed_journey_keeps_current_priority(SupportPriority current)
    {
        var advice = SupportClassificationPolicy.RecommendUrgency(
            current,
            SupportJourneyState.Closed,
            null,
            Now,
            7,
            Now
        );

        advice.Kind.ShouldBe(SupportUrgencyKind.KeepCurrent);
        advice.NextScheduledDepartureAt.ShouldBeNull();
        advice.ObservedAt.ShouldBe(Now);
        advice.StreamVersion.ShouldBe(7);
    }

    [Fact]
    public void Closed_journey_with_a_future_departure_is_inconsistent()
    {
        Recommend(journey: SupportJourneyState.Closed).Kind.ShouldBe(SupportUrgencyKind.Unknown);
    }

    [Theory]
    [InlineData(SupportJourneyState.Unknown)]
    [InlineData((SupportJourneyState)(-1))]
    [InlineData((SupportJourneyState)3)]
    public void Unknown_or_undefined_journey_cannot_establish_priority(SupportJourneyState journey)
    {
        Recommend(journey: journey).Kind.ShouldBe(SupportUrgencyKind.Unknown);
        Recommend(current: SupportPriority.High, journey: journey)
            .Kind.ShouldBe(SupportUrgencyKind.Unknown);
    }

    [Theory]
    [InlineData((SupportPriority)(-1))]
    [InlineData((SupportPriority)2)]
    public void Undefined_current_priority_fails_closed(SupportPriority current)
    {
        Recommend(current: current).Kind.ShouldBe(SupportUrgencyKind.Unknown);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0L)]
    [InlineData(-1L)]
    public void Missing_or_nonpositive_stream_version_fails_closed(long? streamVersion)
    {
        SupportClassificationPolicy
            .RecommendUrgency(
                SupportPriority.High,
                SupportJourneyState.Active,
                DateTimeOffset.Parse("2026-10-13T13:00:00Z"),
                Now,
                streamVersion,
                Now
            )
            .Kind.ShouldBe(SupportUrgencyKind.Unknown);
    }

    [Fact]
    public void Missing_observation_cannot_preserve_a_known_recommendation()
    {
        SupportClassificationPolicy
            .RecommendUrgency(SupportPriority.High, SupportJourneyState.Closed, null, null, 7, Now)
            .Kind.ShouldBe(SupportUrgencyKind.Unknown);
    }

    [Theory]
    [InlineData("2026-10-13T13:00:00Z")]
    [InlineData("2026-10-14T12:00:00.0000001Z")]
    public void Existing_high_priority_is_not_replaced_for_known_active_facts(string departure)
    {
        Recommend(current: SupportPriority.High, nextDeparture: DateTimeOffset.Parse(departure))
            .Kind.ShouldBe(SupportUrgencyKind.KeepCurrent);
    }

    [Theory]
    [InlineData("2026-10-13T12:00:00Z")]
    [InlineData("2026-10-13T11:59:59.9999999Z")]
    public void Existing_high_does_not_turn_missing_future_departure_into_known_facts(
        string departure
    )
    {
        Recommend(current: SupportPriority.High, nextDeparture: DateTimeOffset.Parse(departure))
            .Kind.ShouldBe(SupportUrgencyKind.Unknown);
    }

    [Fact]
    public void Existing_high_does_not_turn_stale_or_future_observation_into_known_facts()
    {
        Recommend(
            current: SupportPriority.High,
            observedAt: DateTimeOffset.Parse("2026-10-13T11:59:29.9999999Z")
        )
            .Kind.ShouldBe(SupportUrgencyKind.Unknown);
        Recommend(
            current: SupportPriority.High,
            observedAt: DateTimeOffset.Parse("2026-10-13T12:00:00.0000001Z")
        )
            .Kind.ShouldBe(SupportUrgencyKind.Unknown);
    }

    [Fact]
    public void Verified_advice_retains_the_source_schedule_observation_and_version()
    {
        var advice = Recommend();

        advice.Kind.ShouldBe(SupportUrgencyKind.High);
        advice.NextScheduledDepartureAt.ShouldBe(DateTimeOffset.Parse("2026-10-13T13:00:00Z"));
        advice.ObservedAt.ShouldBe(Now);
        advice.StreamVersion.ShouldBe(7);
    }

    [Fact]
    public void Unknown_advice_does_not_disclose_invalid_context_as_verified_evidence()
    {
        var advice = Recommend(journey: SupportJourneyState.Unknown);

        advice.NextScheduledDepartureAt.ShouldBeNull();
        advice.ObservedAt.ShouldBeNull();
        advice.StreamVersion.ShouldBeNull();
    }

    [Fact]
    public async Task Financial_category_candidates_and_urgency_leave_manual_state_and_io_unchanged()
    {
        var context = new SupportTestContext();
        await context.CreateAsync();
        var saved = context.Store.Cases[context.CaseId];
        var writes = context.Store.WriteAttempts;
        var orderReads = context.Orders.Calls;

        SupportClassificationPolicy.IsCandidate(ManualCategory.Cancellation).ShouldBeTrue();
        SupportClassificationPolicy.IsCandidate(ManualCategory.Refund).ShouldBeTrue();
        SupportClassificationPolicy.IsCandidate(ManualCategory.Unclassified).ShouldBeFalse();
        Recommend(current: saved.Priority).Kind.ShouldBe(SupportUrgencyKind.High);

        context.Store.Cases[context.CaseId].ShouldBeSameAs(saved);
        saved.Category.ShouldBe(ManualCategory.Unclassified);
        saved.Priority.ShouldBe(SupportPriority.Normal);
        saved.Revision.ShouldBe(1);
        saved.State.ShouldBe(CaseState.Open);
        context.Store.WriteAttempts.ShouldBe(writes);
        context.Orders.Calls.ShouldBe(orderReads);
        context.Store.Receipts.Count.ShouldBe(1);
    }

    private static SupportUrgencyAdvice Recommend(
        SupportPriority current = SupportPriority.Normal,
        SupportJourneyState journey = SupportJourneyState.Active,
        DateTimeOffset? nextDeparture = null,
        DateTimeOffset? observedAt = null
    ) =>
        SupportClassificationPolicy.RecommendUrgency(
            current,
            journey,
            nextDeparture ?? DateTimeOffset.Parse("2026-10-13T13:00:00Z"),
            observedAt ?? Now,
            7,
            Now
        );
}
