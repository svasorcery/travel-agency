using System.Text;
using ErrorOr;
using Travel.Modules.Trips.Core;

namespace Travel.Modules.Trips.Application;

public sealed class TripPlanService(
    ITripPlanStore store,
    ITripPlanProtector protector,
    ITripPlanCodec codec,
    ITripPlanCursor cursor,
    TimeProvider timeProvider
)
{
    public const int MaximumBodyBytes = 128 * 1024;
    public const int PageSize = 20;
    public const int OwnerCapacity = 100;

    public async Task<ErrorOr<TripPlan>> CreateAsync(
        OwnerIdentity? owner,
        CreateTripPlan command,
        CancellationToken cancellationToken = default
    )
    {
        if (owner is null)
            return TripErrors.Forbidden;
        if (command is null)
            return TripErrors.InvalidInput;
        var plan = TripPlan.Create(Guid.NewGuid(), owner, command.Title, command.Brief, WriteNow());
        if (plan.IsError)
            return plan.Errors;
        var row = await PrepareAsync(plan.Value, cancellationToken);
        if (row.IsError)
            return row.Errors;
        var written = await store.TryCreateAsync(row.Value, OwnerCapacity, cancellationToken);
        return written.IsError ? written.Errors : plan.Value;
    }

    public async Task<ErrorOr<TripPlan>> GetAsync(
        OwnerIdentity? owner,
        Guid tripId,
        CancellationToken cancellationToken = default
    )
    {
        var loaded = await LoadAsync(owner, tripId, cancellationToken);
        return loaded.IsError ? loaded.Errors : loaded.Value.Plan;
    }

    public async Task<ErrorOr<TripPlan>> ReplaceAsync(
        OwnerIdentity? owner,
        Guid tripId,
        long expectedRevision,
        ReplaceTripPlan command,
        CancellationToken cancellationToken = default
    )
    {
        if (owner is null)
            return TripErrors.Forbidden;
        if (expectedRevision <= 0 || command is null)
            return TripErrors.InvalidInput;
        var loaded = await LoadAsync(owner, tripId, cancellationToken);
        if (loaded.IsError)
            return loaded.Errors;
        var (row, previous) = loaded.Value;
        if (previous.Revision != expectedRevision)
            return TripErrors.RevisionMismatch;
        var candidate = previous.Replace(command.Title, command.Brief, command.Days, WriteNow());
        if (candidate.IsError)
            return candidate.Errors;
        if (ReferenceEquals(candidate.Value, previous))
        {
            // Even a no-op must not succeed after a concurrent revision or deletion wins.
            var checkedRevision = await store.TryReplaceAsync(
                expectedRevision,
                row,
                cancellationToken
            );
            return checkedRevision.IsError ? checkedRevision.Errors : previous;
        }
        var prepared = await PrepareAsync(candidate.Value, cancellationToken);
        if (prepared.IsError)
            return prepared.Errors;
        var written = await store.TryReplaceAsync(
            expectedRevision,
            prepared.Value,
            cancellationToken
        );
        return written.IsError ? written.Errors : candidate.Value;
    }

    public async Task<ErrorOr<Success>> DeleteAsync(
        OwnerIdentity? owner,
        Guid tripId,
        long expectedRevision,
        CancellationToken cancellationToken = default
    )
    {
        if (owner is null)
            return TripErrors.Forbidden;
        if (tripId == Guid.Empty)
            return TripErrors.NotFound;
        if (expectedRevision <= 0)
            return TripErrors.InvalidInput;
        // Atomic owner+revision delete needs no private payload read or decryption.
        return await store.TryDeleteAsync(owner, tripId, expectedRevision, cancellationToken);
    }

    public async Task<ErrorOr<TripPlanPage>> ListAsync(
        OwnerIdentity? owner,
        string? afterCursor = null,
        CancellationToken cancellationToken = default
    )
    {
        if (owner is null)
            return TripErrors.Forbidden;
        TripPlanPosition? after = null;
        if (afterCursor is not null)
        {
            if (afterCursor.Length is < 1 or > 256)
                return TripErrors.InvalidCursor;
            var decoded = cursor.Decode(owner, afterCursor);
            if (decoded.IsError)
                return decoded.Errors;
            after = decoded.Value;
        }
        var loaded = await store.ListAsync(owner, after, PageSize, cancellationToken);
        if (loaded.IsError)
            return loaded.Errors;
        if (
            loaded.Value.Items.Count > PageSize
            || (loaded.Value.HasMore && loaded.Value.Items.Count == 0)
        )
            return TripErrors.Unavailable;
        var summaries = new List<TripPlanSummary>();
        var ids = new HashSet<Guid>();
        var previousPosition = after;
        foreach (var row in loaded.Value.Items)
        {
            if (
                !ids.Add(row.TripId)
                || (
                    previousPosition is not null
                    && (
                        row.UpdatedAt > previousPosition.UpdatedAt
                        || (
                            row.UpdatedAt == previousPosition.UpdatedAt
                            && row.TripId.CompareTo(previousPosition.TripId) <= 0
                        )
                    )
                )
            )
                return TripErrors.Unavailable;
            previousPosition = new(row.UpdatedAt, row.TripId);
            var plan = await ReadAsync(owner, row.TripId, row, cancellationToken);
            if (plan.IsError)
                return plan.Errors;
            var value = plan.Value;
            summaries.Add(
                new(
                    value.TripId,
                    value.Revision,
                    value.Title,
                    value.Destination.DestinationId,
                    value.Destination.DisplayName,
                    value.Brief.StartDate,
                    value.Brief.EndDate,
                    value.UpdatedAt
                )
            );
        }
        string? next = null;
        if (loaded.Value.HasMore)
        {
            var last = summaries[^1];
            var encoded = cursor.Encode(owner, new(last.UpdatedAt, last.TripId));
            if (encoded.IsError || encoded.Value.Length is < 1 or > 256)
                return TripErrors.Unavailable;
            next = encoded.Value;
        }
        var page = new TripPlanPage(summaries.AsReadOnly(), next);
        var size = codec.MeasurePageBytes(page);
        return size.IsError || size.Value < 0 || size.Value > MaximumBodyBytes
            ? TripErrors.Unavailable
            : page;
    }

    private async Task<ErrorOr<(TripPlanRecord Row, TripPlan Plan)>> LoadAsync(
        OwnerIdentity? owner,
        Guid tripId,
        CancellationToken cancellationToken
    )
    {
        if (owner is null)
            return TripErrors.Forbidden;
        if (tripId == Guid.Empty)
            return TripErrors.NotFound;
        var loaded = await store.LoadAsync(owner, tripId, cancellationToken);
        if (loaded.IsError)
            return loaded.Errors;
        if (loaded.Value is not { } row)
            return TripErrors.NotFound;
        var plan = await ReadAsync(owner, tripId, row, cancellationToken);
        return plan.IsError ? plan.Errors : (row, plan.Value);
    }

    private async Task<ErrorOr<TripPlan>> ReadAsync(
        OwnerIdentity owner,
        Guid tripId,
        TripPlanRecord row,
        CancellationToken cancellationToken
    )
    {
        // Do not rely on ciphertext contents or a faulty store to establish authorization.
        if (row.Owner != owner || row.TripId != tripId)
            return TripErrors.NotFound;
        if (row.Revision <= 0 || string.IsNullOrEmpty(row.ProtectedPayload))
            return TripErrors.Unavailable;
        var plaintext = await protector.UnprotectAsync(
            row.ProtectedPayload,
            new(owner, tripId, row.Revision),
            cancellationToken
        );
        if (plaintext.IsError || Encoding.UTF8.GetByteCount(plaintext.Value) > MaximumBodyBytes)
            return TripErrors.Unavailable;
        var plan = codec.Deserialize(plaintext.Value, row);
        if (
            plan.IsError
            || plan.Value.TripId != tripId
            || plan.Value.Owner != owner
            || plan.Value.Revision != row.Revision
            || plan.Value.CreatedAt != row.CreatedAt
            || plan.Value.UpdatedAt != row.UpdatedAt
        )
            return TripErrors.Unavailable;
        var size = codec.MeasureDetailBytes(plan.Value, reserveMaximumServerFields: false);
        return size.IsError || size.Value < 0 || size.Value > MaximumBodyBytes
            ? TripErrors.Unavailable
            : plan.Value;
    }

    private async Task<ErrorOr<TripPlanRecord>> PrepareAsync(
        TripPlan plan,
        CancellationToken cancellationToken
    )
    {
        var measured = codec.MeasureDetailBytes(plan, reserveMaximumServerFields: true);
        if (measured.IsError)
            return TripErrors.Unavailable;
        if (measured.Value > MaximumBodyBytes)
            return TripErrors.PlanTooLarge;
        if (measured.Value < 0)
            return TripErrors.Unavailable;
        var serialized = codec.Serialize(plan);
        if (serialized.IsError)
            return TripErrors.Unavailable;
        if (Encoding.UTF8.GetByteCount(serialized.Value) > MaximumBodyBytes)
            return TripErrors.PlanTooLarge;
        var protectedPayload = await protector.ProtectAsync(
            serialized.Value,
            new(plan.Owner, plan.TripId, plan.Revision),
            cancellationToken
        );
        if (protectedPayload.IsError || string.IsNullOrWhiteSpace(protectedPayload.Value))
            return TripErrors.Unavailable;
        return new TripPlanRecord(
            plan.TripId,
            plan.Owner,
            plan.Revision,
            plan.CreatedAt,
            plan.UpdatedAt,
            protectedPayload.Value
        );
    }

    // PostgreSQL timestamptz retains microseconds. Persist precisely the timestamp encoded in the protected snapshot.
    private DateTimeOffset WriteNow()
    {
        var now = timeProvider.GetUtcNow();
        return new DateTimeOffset(now.UtcTicks - now.UtcTicks % 10, TimeSpan.Zero);
    }
}
