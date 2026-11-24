using System.Text.Json.Serialization;
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
    private TripStayInput? manualStay;

    [JsonIgnore]
    public bool HasManualStay { get; private set; }

    public TripStayInput? ManualStay
    {
        get => manualStay;
        init
        {
            manualStay = value;
            HasManualStay = true;
        }
    }

    public override string ToString() => nameof(ReplaceTripPlanDto);
}
