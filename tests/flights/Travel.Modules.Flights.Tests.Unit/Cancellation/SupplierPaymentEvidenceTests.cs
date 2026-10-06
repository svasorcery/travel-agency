using Shouldly;
using Travel.Modules.Flights.Core.Providers.Dtos;

namespace Travel.Modules.Flights.Tests.Unit.Cancellation;

public sealed class SupplierPaymentEvidenceTests
{
    [Fact]
    public void Positive_balance_receipt_retains_the_actual_supplier_reference_and_amount()
    {
        var result = SupplierPaymentEvidence.Create(
            "pay_fictional_1",
            CancellationTestData.Money(),
            SupplierPaymentKind.Balance
        );
        result.IsError.ShouldBeFalse();
        result.Value.Reference.ShouldBe("pay_fictional_1");
        result.Value.Amount.Amount.ShouldBe(17.25m);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(999)]
    public void Unknown_payment_kind_is_not_assumed_to_be_cash(int kind) =>
        SupplierPaymentEvidence
            .Create("pay_fictional_1", CancellationTestData.Money(), (SupplierPaymentKind)kind)
            .IsError.ShouldBeTrue();

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("ref\ninvalid")]
    public void Missing_or_controlled_reference_cannot_be_confirmation_evidence(string reference) =>
        SupplierPaymentEvidence
            .Create(reference, CancellationTestData.Money(), SupplierPaymentKind.Balance)
            .IsError.ShouldBeTrue();

    [Fact]
    public void Invalid_historical_money_cannot_be_new_supplier_payment_proof() =>
        SupplierPaymentEvidence
            .Create(
                "pay_fictional_1",
                CancellationTestData.HistoricalMoney(-1m, "USD"),
                SupplierPaymentKind.Balance
            )
            .IsError.ShouldBeTrue();
}
