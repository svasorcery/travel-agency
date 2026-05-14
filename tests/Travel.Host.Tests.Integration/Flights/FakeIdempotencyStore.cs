using ErrorOr;
using Travel.Modules.Flights.Application.Idempotency;

namespace Travel.Host.Tests.Integration.Flights;

/// <summary>
/// No-op <see cref="IIdempotencyStore"/> for the HTTP-pipeline tests: every request is
/// treated as the first occurrence (no cached response, no conflict) so the
/// <c>IdempotencyKeyMiddleware</c> always falls through to the endpoint. Lets the tests
/// exercise routing/auth without a real Postgres-backed store.
/// </summary>
public sealed class FakeIdempotencyStore : IIdempotencyStore
{
    public Task<IdempotencyRecord?> TryGetAsync(
        IdempotencyKey key,
        Guid userId,
        string route,
        string bodyHash,
        CancellationToken ct
    ) => Task.FromResult<IdempotencyRecord?>(null);

    public Task<ErrorOr<Success>> SaveAsync(
        IdempotencyKey key,
        Guid userId,
        string route,
        string bodyHash,
        string responseHash,
        int responseStatus,
        string responseBody,
        CancellationToken ct
    ) => Task.FromResult<ErrorOr<Success>>(Result.Success);

    public Task<ErrorOr<Success>> CheckOrConflictAsync(
        IdempotencyKey key,
        Guid userId,
        string route,
        string bodyHash,
        CancellationToken ct
    ) => Task.FromResult<ErrorOr<Success>>(Result.Success);

    public Task PurgeExpiredAsync(CancellationToken ct) => Task.CompletedTask;
}
