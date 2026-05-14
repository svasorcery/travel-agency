using ErrorOr;
using Travel.Modules.Flights.Core.Providers;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Travel.Shared.Abstractions;

namespace Travel.Modules.Flights.Infrastructure.Payments;

[TestOnly]
public sealed class DuffelTestWalletPaymentGateway : IPaymentGateway
{
    public Task<ErrorOr<PaymentRef>> AuthorizeAsync(
        Money amount,
        string idempotencyKey,
        CancellationToken ct
    ) => Task.FromResult<ErrorOr<PaymentRef>>(new PaymentRef(Guid.NewGuid()));

    public Task<ErrorOr<Success>> CaptureAsync(PaymentRef payment, CancellationToken ct) =>
        Task.FromResult<ErrorOr<Success>>(Result.Success);

    public Task<ErrorOr<RefundRef>> RefundAsync(
        PaymentRef payment,
        Money amount,
        CancellationToken ct
    ) => Task.FromResult<ErrorOr<RefundRef>>(new RefundRef(Guid.NewGuid()));
}
