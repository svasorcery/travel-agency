using Shouldly;
using Travel.Modules.Flights.Core.Providers;
using Travel.Modules.Flights.Core.Refunds;
using Travel.Modules.Flights.Infrastructure.Payments;

namespace Travel.Modules.Flights.Tests.Unit.Refunds;

public sealed class FictionalCustomerRefundGatewayTests
{
    [Theory]
    [InlineData(FictionalCustomerRefundPreset.Sent, CustomerRefundState.Sent)]
    [InlineData(FictionalCustomerRefundPreset.Pending, CustomerRefundState.Pending)]
    [InlineData(FictionalCustomerRefundPreset.NoEffect, CustomerRefundState.ConfirmedNoEffect)]
    public async Task Offline_receipt_retains_exact_payment_money_and_original_method(
        FictionalCustomerRefundPreset preset,
        CustomerRefundState expected
    )
    {
        var c = CustomerRefundDecisionTests.Admitted().CustomerRefund!;
        var gateway = new FictionalCustomerRefundGateway(
            new CustomerRefundExecutionTests.Clock(),
            preset
        );
        var result = await gateway.ExecuteAsync(
            new(
                c.Id,
                c.Basis!,
                c.Amount!,
                CustomerRefundDestination.OriginalFictionalPaymentMethod
            ),
            CancellationToken.None
        );
        result.IsError.ShouldBeFalse();
        result.Value.State.ShouldBe(expected);
        result.Value.Amount.Amount.ShouldBe(80m);
        result.Value.PaymentReference.ShouldBe(RefundTestBookings.Payment);
        result.Value.Source.ShouldBe(CustomerRefundEvidenceSource.FictionalGateway);
    }

    [Fact]
    public async Task Lost_result_is_unknown_and_never_a_decline_or_success_receipt()
    {
        var c = CustomerRefundDecisionTests.Admitted().CustomerRefund!;
        var result = await new FictionalCustomerRefundGateway(
            new CustomerRefundExecutionTests.Clock(),
            FictionalCustomerRefundPreset.ResultLost
        ).ExecuteAsync(
            new(
                c.Id,
                c.Basis!,
                c.Amount!,
                CustomerRefundDestination.OriginalFictionalPaymentMethod
            ),
            CancellationToken.None
        );
        result.IsError.ShouldBeTrue();
    }

    [Fact]
    public async Task Invalid_capture_or_currency_cannot_be_sent_even_in_the_fictional_adapter()
    {
        var c = CustomerRefundDecisionTests.Admitted().CustomerRefund!;
        var result = await new FictionalCustomerRefundGateway(
            new CustomerRefundExecutionTests.Clock()
        ).ExecuteAsync(
            new(
                c.Id,
                c.Basis!,
                RefundTestBookings.Money(110m),
                CustomerRefundDestination.OriginalFictionalPaymentMethod
            ),
            CancellationToken.None
        );
        result.IsError.ShouldBeTrue();
    }
}
