using ErrorOr;

namespace Travel.Modules.Trips.Core;

public sealed record TripStayInput(string PlaceName, DateOnly CheckInDate, DateOnly CheckOutDate)
{
    public override string ToString() => nameof(TripStayInput);
}

public sealed record TripManualStay
{
    public string PlaceName { get; }
    public DateOnly CheckInDate { get; }
    public DateOnly CheckOutDate { get; }

    private TripManualStay(TripStayInput input)
    {
        PlaceName = input.PlaceName;
        CheckInDate = input.CheckInDate;
        CheckOutDate = input.CheckOutDate;
    }

    public static ErrorOr<TripManualStay> Create(TripStayInput input) =>
        TripText.IsValid(input.PlaceName, 100)
        && input.PlaceName.Any(character => !char.IsWhiteSpace(character) && character != '\uFEFF')
        && input.CheckInDate < input.CheckOutDate
            ? new TripManualStay(input)
            : TripErrors.InvalidInput;

    public bool Fits(TripBrief brief) =>
        CheckInDate >= brief.StartDate && CheckOutDate <= brief.EndDate;

    public TripStayInput ToInput() => new(PlaceName, CheckInDate, CheckOutDate);

    public override string ToString() => nameof(TripManualStay);
}
