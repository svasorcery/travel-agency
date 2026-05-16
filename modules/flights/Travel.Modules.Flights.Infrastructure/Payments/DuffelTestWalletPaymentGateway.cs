using System.Collections.Concurrent;
using ErrorOr;
using Travel.Modules.Flights.Core.Providers;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Travel.Shared.Abstractions;

namespace Travel.Modules.Flights.Infrastructure.Payments;

/// <summary>
/// In-memory payment gateway backed by the Duffel test-wallet flow.
/// For use in non-production environments only (<see cref="TestOnlyAttribute"/>).
/// <para>
/// The idempotency cache (<c>_cache</c>) is process-lifetime: it grows for the life of the
/// singleton and is never automatically cleared. Tests that share a single gateway instance
/// across test classes should call <see cref="Reset"/> from their fixture teardown to avoid
/// state leaking between suites.
/// </para>
/// </summary>
[TestOnly]
public sealed class DuffelTestWalletPaymentGateway : IPaymentGateway
{
    // Keyed by idempotencyKey — repeated calls with the same key return the same PaymentRef.
    private readonly ConcurrentDictionary<string, PaymentRef> _cache = new();

    public Task<ErrorOr<PaymentRef>> AuthorizeAsync(
        Money amount,
        string idempotencyKey,
        CancellationToken ct
    )
    {
        var paymentRef = _cache.GetOrAdd(idempotencyKey, _ => new PaymentRef(Guid.NewGuid()));
        return Task.FromResult<ErrorOr<PaymentRef>>(paymentRef);
    }

    public Task<ErrorOr<Success>> CaptureAsync(PaymentRef payment, CancellationToken ct) =>
        Task.FromResult<ErrorOr<Success>>(Result.Success);

    public Task<ErrorOr<RefundRef>> RefundAsync(
        PaymentRef payment,
        Money amount,
        CancellationToken ct
    ) => Task.FromResult<ErrorOr<RefundRef>>(new RefundRef(Guid.NewGuid()));

    /// <summary>
    /// Clears all idempotency-keyed payment refs from the in-memory cache.
    /// Test infrastructure use only — call from test fixture teardown when
    /// the gateway is shared across test classes. Not safe for concurrent
    /// use during a payment flow.
    /// </summary>
    public void Reset() => _cache.Clear();
}
