using ErrorOr;

namespace Travel.Modules.Trips.Core;

public sealed record TripBriefInput(
    string DestinationId,
    string? OriginId,
    DateOnly StartDate,
    DateOnly EndDate,
    int Adults,
    string Currency,
    string? TotalBudget,
    IReadOnlyList<string> Interests,
    string Pace
);

public sealed record TripBrief
{
    public string DestinationId { get; }
    public string? OriginId { get; }
    public DateOnly StartDate { get; }
    public DateOnly EndDate { get; }
    public int Adults { get; }
    public string Currency { get; }
    public PlanningMoney? TotalBudget { get; }
    public IReadOnlyList<string> Interests { get; }
    public string Pace { get; }

    private TripBrief(TripBriefInput input, PlanningMoney? budget)
    {
        DestinationId = input.DestinationId;
        OriginId = input.OriginId;
        StartDate = input.StartDate;
        EndDate = input.EndDate;
        Adults = input.Adults;
        Currency = input.Currency;
        TotalBudget = budget;
        Interests = Array.AsReadOnly(input.Interests.ToArray());
        Pace = input.Pace;
    }

    public static ErrorOr<TripBrief> Create(TripBriefInput? input)
    {
        if (
            input is null
            || CityCatalogue.Find(input.DestinationId).IsError
            || (input.OriginId is not null && CityCatalogue.Find(input.OriginId).IsError)
            || TripDateRange.Create(input.StartDate, input.EndDate).IsError
            || PartySize.Create(input.Adults).IsError
            || !PlanningMoney.IsSupportedCurrency(input.Currency)
            || input.Pace is not ("relaxed" or "balanced" or "active")
            || input.Interests is null
            || input.Interests.Count > 5
            || input.Interests.Distinct(StringComparer.Ordinal).Count() != input.Interests.Count
            || input.Interests.Any(x =>
                x is not ("architecture" or "culture" or "food" or "nature" or "history")
            )
        )
            return TripErrors.InvalidInput;
        PlanningMoney? budget = null;
        if (input.TotalBudget is not null)
        {
            var parsed = PlanningMoney.Create(input.TotalBudget, input.Currency);
            if (parsed.IsError)
                return parsed.Errors;
            budget = parsed.Value;
        }
        return new TripBrief(input, budget);
    }

    public TripBriefInput ToInput() =>
        new(
            DestinationId,
            OriginId,
            StartDate,
            EndDate,
            Adults,
            Currency,
            TotalBudget?.Amount,
            Interests,
            Pace
        );

    internal bool HasSameValues(TripBrief other) =>
        DestinationId == other.DestinationId
        && OriginId == other.OriginId
        && StartDate == other.StartDate
        && EndDate == other.EndDate
        && Adults == other.Adults
        && Currency == other.Currency
        && TotalBudget == other.TotalBudget
        && Interests.SequenceEqual(other.Interests)
        && Pace == other.Pace;

    public override string ToString() => nameof(TripBrief);
}
