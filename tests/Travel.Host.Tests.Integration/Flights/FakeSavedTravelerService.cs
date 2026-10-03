using ErrorOr;
using Travel.Modules.Flights.Application.SavedTravelers;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;

namespace Travel.Host.Tests.Integration.Flights;

public sealed class FakeSavedTravelerService : ISavedTravelerService
{
    public static readonly Guid Revision = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    public static readonly Guid ForeignId = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");
    public static readonly SavedTravelerDetails Details = SavedTravelerDetails
        .CreateRaw(
            "mr",
            "John",
            "Smith",
            new DateOnly(1990, 1, 1),
            "male",
            "john@example.test",
            "+12025550123",
            new DateOnly(2026, 10, 3)
        )
        .Value;
    public int Calls { get; private set; }
    public Guid Owner { get; private set; }
    public SavedTravelerId Id { get; private set; }
    public Guid? ExpectedRevision { get; private set; }
    public SavedTravelerDetails? SubmittedDetails { get; private set; }
    public List<Error>? Errors { get; set; }

    public void Reset()
    {
        Calls = 0;
        Errors = null;
        Owner = Guid.Empty;
        ExpectedRevision = null;
        SubmittedDetails = null;
    }

    private void Capture(Guid owner, SavedTravelerId id = default)
    {
        Calls++;
        Owner = owner;
        Id = id;
    }

    public Task<ErrorOr<SavedTravelerPage>> ListAsync(
        Guid ownerUserId,
        int offset,
        CancellationToken ct
    )
    {
        Capture(ownerUserId);
        return Task.FromResult<ErrorOr<SavedTravelerPage>>(
            Errors is not null ? Errors
            : !SavedTravelerPaging.IsValid(offset) ? SavedTravelerErrors.PageInvalid
            : new SavedTravelerPage(
                [
                    new SavedTravelerView(
                        SavedTravelerId
                            .Create(Guid.Parse("11111111-1111-1111-1111-111111111111"))
                            .Value,
                        Revision,
                        Details
                    ),
                ],
                offset,
                true
            )
        );
    }

    public Task<ErrorOr<SavedTravelerView>> GetAsync(
        Guid ownerUserId,
        SavedTravelerId id,
        CancellationToken ct
    )
    {
        Capture(ownerUserId, id);
        return Task.FromResult<ErrorOr<SavedTravelerView>>(
            Errors is not null ? Errors
            : id.Value == ForeignId ? SavedTravelerErrors.NotFound
            : new SavedTravelerView(id, Revision, Details)
        );
    }

    public Task<ErrorOr<SavedTravelerReceipt>> CreateAsync(
        Guid ownerUserId,
        SavedTravelerId id,
        SavedTravelerDetails details,
        CancellationToken ct
    )
    {
        Capture(ownerUserId, id);
        SubmittedDetails = details;
        return Task.FromResult<ErrorOr<SavedTravelerReceipt>>(
            Errors is not null ? Errors
            : id.Value == ForeignId ? SavedTravelerErrors.NotFound
            : new SavedTravelerReceipt(id, Revision)
        );
    }

    public Task<ErrorOr<SavedTravelerReceipt>> UpdateAsync(
        Guid ownerUserId,
        SavedTravelerId id,
        Guid expectedRevision,
        SavedTravelerDetails details,
        CancellationToken ct
    )
    {
        Capture(ownerUserId, id);
        ExpectedRevision = expectedRevision;
        SubmittedDetails = details;
        return Task.FromResult<ErrorOr<SavedTravelerReceipt>>(
            Errors is not null ? Errors
            : id.Value == ForeignId ? SavedTravelerErrors.NotFound
            : expectedRevision != Revision ? SavedTravelerErrors.PreconditionFailed
            : new SavedTravelerReceipt(id, Revision)
        );
    }

    public Task<ErrorOr<Success>> DeleteAsync(
        Guid ownerUserId,
        SavedTravelerId id,
        Guid expectedRevision,
        CancellationToken ct
    )
    {
        Capture(ownerUserId, id);
        ExpectedRevision = expectedRevision;
        return Task.FromResult<ErrorOr<Success>>(
            Errors is not null ? Errors
            : id.Value == ForeignId ? SavedTravelerErrors.NotFound
            : expectedRevision != Revision ? SavedTravelerErrors.PreconditionFailed
            : Result.Success
        );
    }
}
