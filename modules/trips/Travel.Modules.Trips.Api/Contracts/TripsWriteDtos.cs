using Travel.Modules.Trips.Core;

namespace Travel.Modules.Trips.Api.Contracts;

public sealed record CreateTripPlanDto(string Title, TripBriefInput Brief)
{
    public override string ToString() => nameof(CreateTripPlanDto);
}

public sealed record ReplaceTripPlanDto(
    string Title,
    TripBriefInput Brief,
    IReadOnlyList<TripDayInput> Days
)
{
    public override string ToString() => nameof(ReplaceTripPlanDto);
}
