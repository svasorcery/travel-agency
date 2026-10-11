using ErrorOr;
using Travel.Modules.Flights.Core.Refunds;
using Travel.Modules.Flights.Core.ValueObjects;

namespace Travel.Modules.Flights.Core.Providers;

public sealed record CustomerRefundRequest(
    Guid RefundId,
    CustomerPaymentBasis OriginalPayment,
    Money Amount,
    CustomerRefundDestination Destination
)
{
    public override string ToString() => nameof(CustomerRefundRequest);
}

public interface ICustomerRefundGateway
{
    Task<ErrorOr<CustomerRefundObservation>> ExecuteAsync(
        CustomerRefundRequest request,
        CancellationToken ct
    );
}
