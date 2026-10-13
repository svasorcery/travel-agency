namespace Travel.Modules.Support.Core;

public enum SupportJourneyState
{
    Unknown,
    Active,
    Closed,
}

public enum SupportUrgencyKind
{
    Unknown,
    KeepCurrent,
    High,
}

public sealed record SupportUrgencyAdvice(
    SupportUrgencyKind Kind,
    DateTimeOffset? NextScheduledDepartureAt,
    DateTimeOffset? ObservedAt,
    long? StreamVersion
);

public static class SupportClassificationPolicy
{
    public static bool IsCandidate(ManualCategory category) =>
        category
            is ManualCategory.Payment
                or ManualCategory.Ticketing
                or ManualCategory.Cancellation
                or ManualCategory.Refund
                or ManualCategory.FareConditions
                or ManualCategory.Ancillaries
                or ManualCategory.Technical
                or ManualCategory.Other;

    public static SupportUrgencyAdvice RecommendUrgency(
        SupportPriority current,
        SupportJourneyState journey,
        DateTimeOffset? nextDeparture,
        DateTimeOffset? observedAt,
        long? streamVersion,
        DateTimeOffset now
    )
    {
        if (
            !Enum.IsDefined(current)
            || !Enum.IsDefined(journey)
            || journey == SupportJourneyState.Unknown
            || observedAt is not { } observation
            || streamVersion is not > 0
            || observation > now
            || now - observation > TimeSpan.FromSeconds(30)
        )
            return new(SupportUrgencyKind.Unknown, null, null, null);

        if (
            journey == SupportJourneyState.Closed && nextDeparture is not null
            || journey == SupportJourneyState.Active
                && (nextDeparture is null || nextDeparture <= now)
        )
            return new(SupportUrgencyKind.Unknown, null, null, null);

        var kind =
            current == SupportPriority.High || journey == SupportJourneyState.Closed
                ? SupportUrgencyKind.KeepCurrent
            : nextDeparture!.Value - now <= TimeSpan.FromHours(24) ? SupportUrgencyKind.High
            : SupportUrgencyKind.KeepCurrent;

        return new(kind, nextDeparture, observation, streamVersion);
    }
}
