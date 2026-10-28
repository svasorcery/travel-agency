namespace Travel.Tests.AiEvals.TripsBriefExtraction;

public sealed class TripsBriefEvaluationSchedule(
    TimeProvider time,
    TimeSpan? authorizationRemaining = null
)
{
    private readonly long authorizationStamp = time.GetTimestamp();
    private long? started;
    private TimeSpan limit;
    private TimeSpan? lastProvider;
    public int ProviderStarts { get; private set; }
    public TimeSpan Elapsed => started is { } stamp ? time.GetElapsedTime(stamp) : TimeSpan.Zero;
    public TimeSpan EffectiveLimit => limit;

    public void Begin(TimeSpan runLimit)
    {
        if (started is not null || runLimit <= TimeSpan.Zero || runLimit > TimeSpan.FromHours(6))
            throw new InvalidOperationException("InvalidScheduleStart");
        var remaining = authorizationRemaining - time.GetElapsedTime(authorizationStamp);
        if (remaining is { } budget && budget <= TimeSpan.Zero)
            throw new InvalidOperationException("AuthorizationExpired");
        started = time.GetTimestamp();
        limit = remaining is { } cap && cap < runLimit ? cap : runLimit;
    }

    public bool HasBudget(TimeSpan required) => started is not null && Elapsed + required < limit;

    public TimeSpan DelayBeforeCase(int originalOrdinal)
    {
        if (started is null || originalOrdinal is < 0 or > 47)
            throw new InvalidOperationException("InvalidSchedule");
        var planned =
            TimeSpan.FromMinutes(originalOrdinal / 8 * 65)
            + TimeSpan.FromSeconds(originalOrdinal % 8 * 11);
        var earliest =
            lastProvider is { } at && at + TimeSpan.FromSeconds(11) > planned
                ? at + TimeSpan.FromSeconds(11)
                : planned;
        return earliest > Elapsed ? earliest - Elapsed : TimeSpan.Zero;
    }

    public void RecordProviderStart()
    {
        if (
            !HasBudget(TimeSpan.Zero)
            || lastProvider is { } at && Elapsed - at < TimeSpan.FromSeconds(11)
        )
            throw new InvalidOperationException("ProviderScheduleViolation");
        lastProvider = Elapsed;
        ProviderStarts++;
    }
}
