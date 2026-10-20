using ErrorOr;
using Travel.Modules.Trips.Core;

namespace Travel.Modules.Trips.Application;

public sealed record CreateTripPlan(string Title, TripBriefInput Brief)
{
    public override string ToString() => nameof(CreateTripPlan);
}

public sealed record ReplaceTripPlan(
    string Title,
    TripBriefInput Brief,
    IReadOnlyList<TripDayInput> Days
)
{
    public override string ToString() => nameof(ReplaceTripPlan);
}

public sealed record TripPlanRecord(
    Guid TripId,
    OwnerIdentity Owner,
    long Revision,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string ProtectedPayload
)
{
    public override string ToString() => nameof(TripPlanRecord);
}

public sealed record TripPlanPosition(DateTimeOffset UpdatedAt, Guid TripId);

public sealed record TripPlanRecordPage(IReadOnlyList<TripPlanRecord> Items, bool HasMore);

public sealed record TripPlanSummary(
    Guid TripId,
    long Revision,
    string Title,
    string DestinationId,
    string DisplayName,
    DateOnly StartDate,
    DateOnly EndDate,
    DateTimeOffset UpdatedAt
)
{
    public override string ToString() => nameof(TripPlanSummary);
}

public sealed record TripPlanPage(IReadOnlyList<TripPlanSummary> Items, string? NextCursor)
{
    public override string ToString() => nameof(TripPlanPage);
}

public sealed record TripPlanBinding(
    OwnerIdentity Owner,
    Guid TripId,
    long Revision,
    int SchemaVersion = 1
);

/// <summary>Owner guards precede payload access. Create serializes owner capacity; replace/delete are atomic CAS and never upsert. Identical-row replace checks the revision without changing timestamp/revision.</summary>
public interface ITripPlanStore
{
    Task<ErrorOr<TripPlanRecord?>> LoadAsync(
        OwnerIdentity owner,
        Guid tripId,
        CancellationToken cancellationToken
    );
    Task<ErrorOr<TripPlanRecordPage>> ListAsync(
        OwnerIdentity owner,
        TripPlanPosition? after,
        int limit,
        CancellationToken cancellationToken
    );
    Task<ErrorOr<Success>> TryCreateAsync(
        TripPlanRecord record,
        int ownerCapacity,
        CancellationToken cancellationToken
    );
    Task<ErrorOr<Success>> TryReplaceAsync(
        long expectedRevision,
        TripPlanRecord record,
        CancellationToken cancellationToken
    );
    Task<ErrorOr<Success>> TryDeleteAsync(
        OwnerIdentity owner,
        Guid tripId,
        long expectedRevision,
        CancellationToken cancellationToken
    );
}

public interface ITripPlanProtector
{
    Task<ErrorOr<string>> ProtectAsync(
        string plaintext,
        TripPlanBinding binding,
        CancellationToken cancellationToken
    );
    Task<ErrorOr<string>> UnprotectAsync(
        string envelope,
        TripPlanBinding binding,
        CancellationToken cancellationToken
    );
}

/// <summary>The canonical complete detail is both protected payload and HTTP body. Size checks include derived/server fields, with the maximum revision/timestamp reserve before commit.</summary>
public interface ITripPlanCodec
{
    ErrorOr<string> Serialize(TripPlan plan);
    ErrorOr<TripPlan> Deserialize(string plaintext, TripPlanRecord record);
    ErrorOr<int> MeasureDetailBytes(TripPlan plan, bool reserveMaximumServerFields);
    ErrorOr<int> MeasurePageBytes(TripPlanPage page);
    ErrorOr<string> SerializePage(TripPlanPage page);
}

public interface ITripPlanCursor
{
    ErrorOr<string> Encode(OwnerIdentity owner, TripPlanPosition position);
    ErrorOr<TripPlanPosition> Decode(OwnerIdentity owner, string cursor);
}
