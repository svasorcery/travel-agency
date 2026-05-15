using System.Collections.Concurrent;
using ErrorOr;
using Travel.Modules.Flights.Core.Providers;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Travel.Shared.Abstractions;

namespace Travel.Modules.Flights.Infrastructure.Payments;

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
}
