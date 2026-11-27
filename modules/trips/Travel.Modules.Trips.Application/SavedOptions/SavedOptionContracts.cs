using ErrorOr;
using Travel.Modules.Trips.Core;
using Travel.Modules.Trips.Core.Options;

namespace Travel.Modules.Trips.Application.SavedOptions;

public sealed record SavedOptionSelection(
    Guid TripId,
    DateTimeOffset ExpiresAtUtc,
    string SelectionToken
)
{
    public override string ToString() => nameof(SavedOptionSelection);
}

public sealed record IssuedOption(
    int Version,
    Guid TripId,
    DateTimeOffset IssuedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    TripBriefInput Brief,
    TripOptionPreview Preview
)
{
    public override string ToString() => nameof(IssuedOption);
}

public sealed record HistoricalTripOption(
    int Version,
    TripBriefInput Brief,
    TripOptionPreview Preview,
    DateTimeOffset SavedAtUtc
)
{
    public override string ToString() => nameof(HistoricalTripOption);
}

public sealed record SavedOptionDetail(HistoricalTripOption? SavedOption)
{
    public override string ToString() => nameof(SavedOptionDetail);
}

public interface ISavedOptionProtection
{
    ErrorOr<SavedOptionSelection> Issue(
        OwnerIdentity owner,
        TripBriefInput brief,
        TripOptionPreview preview,
        DateTimeOffset now
    );
    ErrorOr<IssuedOption> OpenSelection(OwnerIdentity owner, string token);
    ErrorOr<string> ProtectHistory(OwnerIdentity owner, Guid tripId, HistoricalTripOption option);
    ErrorOr<HistoricalTripOption> OpenHistory(OwnerIdentity owner, Guid tripId, string envelope);
}
