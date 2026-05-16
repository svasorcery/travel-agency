using ErrorOr;

namespace Travel.Modules.Flights.Application.Idempotency;

public readonly record struct IdempotencyKey(string Value);

public sealed record IdempotencyRecord(
    string ResponseHash,
    int ResponseStatus,
    string ResponseBody
);

public enum BeginOutcome
{
    /// <summary>The caller is the first writer for this key — proceed to handler.</summary>
    Started,

    /// <summary>A completed (2xx) record exists for this key+route+user+body — replay it.</summary>
    Replay,

    /// <summary>A row is already in flight for this key — concurrent caller must back off.</summary>
    InFlight,

    /// <summary>A row exists but with a different body — semantic conflict.</summary>
    BodyConflict,
}

public sealed record BeginResult(BeginOutcome Outcome, IdempotencyRecord? Replay);

public interface IIdempotencyStore
{
    /// <summary>
    /// Atomically reserves a row for this key. On <see cref="BeginOutcome.Started"/> the
    /// row is in flight (ResponseHash = null) and the caller MUST eventually call
    /// <see cref="CompleteAsync"/> or <see cref="AbandonAsync"/>. On
    /// <see cref="BeginOutcome.Replay"/> the cached response is returned.
    /// </summary>
    Task<BeginResult> TryBeginAsync(
        IdempotencyKey key,
        Guid userId,
        string route,
        string bodyHash,
        CancellationToken ct
    );

    /// <summary>
    /// Updates the in-flight row with the terminal response. Only called for 2xx responses;
    /// non-2xx responses go through <see cref="AbandonAsync"/>.
    /// </summary>
    Task CompleteAsync(
        IdempotencyKey key,
        Guid userId,
        string route,
        string responseHash,
        int responseStatus,
        string responseBody,
        CancellationToken ct
    );

    /// <summary>
    /// Removes an in-flight row whose handler returned a non-2xx response, so a retry
    /// with the same key can re-execute the handler.
    /// </summary>
    Task AbandonAsync(IdempotencyKey key, Guid userId, string route, CancellationToken ct);

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
