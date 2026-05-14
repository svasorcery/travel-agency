using ErrorOr;

namespace Travel.Modules.Flights.Application.Idempotency;

public readonly record struct IdempotencyKey(string Value);

public sealed record IdempotencyRecord(
    string ResponseHash,
    int ResponseStatus,
    string ResponseBody
);

public interface IIdempotencyStore
{
    /// <summary>
    /// Returns the stored response only when a completed record exists for this key
    /// AND its request body hash matches — so a same-key/different-body request falls
    /// through to <see cref="CheckOrConflictAsync"/> and is reported as a 409 conflict.
    /// </summary>
    Task<IdempotencyRecord?> TryGetAsync(
        IdempotencyKey key,
        Guid userId,
        string route,
        string bodyHash,
        CancellationToken ct
    );

    Task<ErrorOr<Success>> SaveAsync(
        IdempotencyKey key,
        Guid userId,
        string route,
        string bodyHash,
        string responseHash,
        int responseStatus,
        string responseBody,
        CancellationToken ct
    );

    Task<ErrorOr<Success>> CheckOrConflictAsync(
        IdempotencyKey key,
        Guid userId,
        string route,
        string bodyHash,
        CancellationToken ct
    );

    Task PurgeExpiredAsync(CancellationToken ct);
}
