using ErrorOr;
using Travel.Modules.Trips.Core;

namespace Travel.Modules.Trips.Application.SavedOptions;

public sealed record NewSavedOption(TripPlanRecord Plan, string ProtectedOption)
{
    public const int MaximumEnvelopeBytes = 64 * 1024;

    public override string ToString() => nameof(NewSavedOption);
}

public sealed record SavedOptionWrite(Guid TripId, bool Created);

public sealed record SavedOptionEnvelope(string? ProtectedOption)
{
    public override string ToString() => nameof(SavedOptionEnvelope);
}

public interface ISavedOptionStore
{
    Task<ErrorOr<SavedOptionWrite>> TrySaveAsync(
        OwnerIdentity owner,
        Guid tripId,
        DateTimeOffset expiresAt,
        Func<CancellationToken, Task<ErrorOr<NewSavedOption>>> prepareFirstWrite,
        CancellationToken ct
    );

    Task<ErrorOr<SavedOptionEnvelope>> LoadAsync(
        OwnerIdentity owner,
        Guid tripId,
        CancellationToken ct
    );
}
