using ErrorOr;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;

namespace Travel.Modules.Flights.Core.Providers;

public interface IPaymentGateway
{
    Task<ErrorOr<PaymentRef>> AuthorizeAsync(
        Money amount,
        string idempotencyKey,
        CancellationToken ct
    );
    Task<ErrorOr<Success>> CaptureAsync(PaymentRef payment, CancellationToken ct);
    Task<ErrorOr<RefundRef>> RefundAsync(PaymentRef payment, Money amount, CancellationToken ct);
}
