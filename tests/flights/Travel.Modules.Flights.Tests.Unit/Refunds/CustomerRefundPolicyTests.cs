using System.Text.Json;
using System.Text.Json.Nodes;
using Shouldly;
using Travel.Modules.Flights.Core.Cancellation;
using Travel.Modules.Flights.Core.DomainEvents;
using Travel.Modules.Flights.Core.Refunds;

namespace Travel.Modules.Flights.Tests.Unit.Refunds;

public sealed class CustomerRefundPolicyTests
{
    [Theory]
    [InlineData(80, CustomerRefundState.Ready)]
    [InlineData(0, CustomerRefundState.NoRefundDue)]
    [InlineData(110, CustomerRefundState.ManualReviewRequired)]
    public void Exact_entitlement_never_adds_fees_or_caps_supplier_money(
        decimal amount,
        CustomerRefundState expected
    )
    {
        var result = CustomerRefundPolicy.Assess(
            RefundTestBookings.Cancelled(refund: amount),
            RefundTestBookings.Cancellation,
            RefundTestBookings.Now.AddSeconds(6)
        );
        result.IsError.ShouldBeFalse();
        result.Value.State.ShouldBe(expected);
        if (expected is CustomerRefundState.Ready or CustomerRefundState.NoRefundDue)
            result.Value.Amount!.Amount.ShouldBe(amount);
        if (expected == CustomerRefundState.Ready)
        {
            result.Value.Basis!.CapturedAmount.Amount.ShouldBe(100m);
            result.Value.Basis.Source.ShouldBe(CustomerPaymentSource.TestWalletObserved);
        }
    }

    [Fact]
    public void Positively_completed_unpaid_order_does_not_create_a_refund_receipt()
    {
        var result = CustomerRefundPolicy.Assess(
            RefundTestBookings.Unpaid(),
            RefundTestBookings.Cancellation,
            RefundTestBookings.Now.AddSeconds(6)
        );
        result.Value.State.ShouldBe(CustomerRefundState.NoCustomerPayment);
        result.Value.Basis.ShouldBeNull();
        result.Value.Amount!.Amount.ShouldBe(0m);
    }

    [Fact]
    public void Unresolved_confirmation_is_not_mistaken_for_an_unpaid_order()
    {
        var b = RefundTestBookings.Unpaid();
        RefundTestBookings.Apply(
            b,
            new ConfirmationAttemptStarted(
                Guid.NewGuid(),
                RefundTestBookings.Owner,
                Guid.NewGuid(),
                new string('c', 64),
                RefundTestBookings.Order,
                RefundTestBookings.Money(100m),
                RefundTestBookings.Now.AddSeconds(5)
            )
        );
        CustomerRefundPolicy
            .Assess(b, RefundTestBookings.Cancellation, RefundTestBookings.Now.AddSeconds(6))
            .Value.State.ShouldBe(CustomerRefundState.ManualReviewRequired);
    }

    [Fact]
    public void Different_currency_does_not_create_an_automatic_payment()
    {
        var result = CustomerRefundPolicy.Assess(
            RefundTestBookings.Cancelled(refundCurrency: "EUR"),
            RefundTestBookings.Cancellation,
            RefundTestBookings.Now.AddSeconds(6)
        );
        result.Value.State.ShouldBe(CustomerRefundState.ManualReviewRequired);
        result.Value.Amount.ShouldBeNull();
    }

    [Fact]
    public void Payment_reference_without_capture_observation_is_unverified()
    {
        var result = CustomerRefundPolicy.Assess(
            RefundTestBookings.Cancelled(captureObserved: false),
            RefundTestBookings.Cancellation,
            RefundTestBookings.Now.AddSeconds(6)
        );
        result.Value.State.ShouldBe(CustomerRefundState.LegacyUnverified);
    }

    [Fact]
    public void Operator_verified_cancellation_is_a_manual_customer_case()
    {
        var result = CustomerRefundPolicy.Assess(
            RefundTestBookings.Cancelled(source: CancellationResolutionSource.OperatorVerified),
            RefundTestBookings.Cancellation,
            RefundTestBookings.Now.AddSeconds(6)
        );
        result.Value.State.ShouldBe(CustomerRefundState.ManualReviewRequired);
    }

    [Fact]
    public void Party_and_leg_count_do_not_multiply_a_whole_order_entitlement()
    {
        var result = CustomerRefundPolicy.Assess(
            RefundTestBookings.Cancelled(passengers: 9, legs: 4),
            RefundTestBookings.Cancellation,
            RefundTestBookings.Now.AddSeconds(6)
        );
        result.Value.State.ShouldBe(CustomerRefundState.Ready);
        result.Value.Amount!.Amount.ShouldBe(80m);
    }

    [Fact]
    public void Historical_refunded_status_does_not_establish_customer_payment()
    {
        var booking = RefundTestBookings.Held();
        RefundTestBookings.Apply(
            booking,
            new OrderRefunded(
                new(Guid.NewGuid()),
                RefundTestBookings.Money(100m),
                RefundInitiator.Airline,
                RefundTestBookings.Now
            )
        );
        CustomerRefundPolicy
            .Assess(booking, RefundTestBookings.Cancellation, RefundTestBookings.Now.AddSeconds(6))
            .Value.State.ShouldBe(CustomerRefundState.LegacyUnverified);
    }

    [Theory]
    [InlineData(SettlementComposition.CreditOnly)]
    [InlineData(SettlementComposition.Mixed)]
    [InlineData(SettlementComposition.CashOnly)]
    public void Untrusted_legacy_credit_composition_never_grants_gateway_permission(
        SettlementComposition composition
    )
    {
        var b = RefundTestBookings.Cancelled();
        var op = b.CurrentCancellation!;
        var json = JsonNode.Parse(JsonSerializer.Serialize(op.Terms!))!;
        json["Settlement"]!["Composition"] = (int)composition;
        json["Settlement"]!["CreditsKnownEmpty"] = false;
        var legacy = JsonSerializer.Deserialize<CancellationTerms>(json.ToJsonString())!;
        RefundTestBookings.Apply(
            b,
            new CancellationTermsObtained(op.Id, legacy, RefundTestBookings.Now.AddSeconds(3))
        );
        RefundTestBookings.Apply(
            b,
            new CancellationSucceeded(op.Id, op.Evidence!, RefundTestBookings.Now.AddSeconds(5))
        );
        var result = CustomerRefundPolicy.Assess(b, op.Id, RefundTestBookings.Now.AddSeconds(6));
        result.Value.State.ShouldBe(CustomerRefundState.ManualReviewRequired);
        result.Value.Basis.ShouldBeNull();
    }

    [Fact]
    public void Untrusted_legacy_destination_cannot_turn_agency_card_money_into_a_customer_payout()
    {
        var b = RefundTestBookings.Cancelled();
        var op = b.CurrentCancellation!;
        var json = JsonNode.Parse(JsonSerializer.Serialize(op.Terms!))!;
        json["Destination"] = (int)CancellationRefundDestination.Card;
        var legacy = JsonSerializer.Deserialize<CancellationTerms>(json.ToJsonString())!;
        RefundTestBookings.Apply(
            b,
            new CancellationTermsObtained(op.Id, legacy, RefundTestBookings.Now.AddSeconds(3))
        );
        RefundTestBookings.Apply(
            b,
            new CancellationSucceeded(op.Id, op.Evidence!, RefundTestBookings.Now.AddSeconds(5))
        );
        CustomerRefundPolicy
            .Assess(b, op.Id, RefundTestBookings.Now.AddSeconds(6))
            .Value.State.ShouldBe(CustomerRefundState.ManualReviewRequired);
    }
}
