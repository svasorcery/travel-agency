using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;

namespace Travel.Modules.Flights.Application.SavedTravelers;

public sealed record SavedTravelerView(
    SavedTravelerId Id,
    Guid Revision,
    SavedTravelerDetails Details
)
{
    public override string ToString() => nameof(SavedTravelerView);
}

public sealed record SavedTravelerPage(
    IReadOnlyList<SavedTravelerView> Items,
    int Offset,
    bool HasMore
)
{
    public override string ToString() => nameof(SavedTravelerPage);
}

public sealed record SavedTravelerReceipt(SavedTravelerId Id, Guid Revision);

public static class SavedTravelerPaging
{
    public const int PageSize = 20;
    public const int ReadSize = PageSize + 1;
    public const int MaximumOffset = 2147483640;

    public static bool IsValid(int offset) =>
        offset >= 0 && offset <= MaximumOffset && offset % PageSize == 0;
}
