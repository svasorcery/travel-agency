using Shouldly;
using Travel.Modules.Flights.Core.Cancellation;

namespace Travel.Modules.Flights.Tests.Unit.Cancellation;

public sealed class CancellationTermsTests
{
    [Fact]
    public void Known_zero_refund_is_a_valid_proposal_requiring_consent()
    {
        var input = CancellationTestData.Input() with { Refund = CancellationTestData.Money(0m) };
        var result = CancellationTerms.Create(input, CancellationTestData.Now);
        result.IsError.ShouldBeFalse();
        result.Value.Refund.Amount.ShouldBe(0m);
    }

    [Theory]
    [MemberData(nameof(InvalidInputs), DisableDiscoveryEnumeration = true)]
    public void Missing_invalid_or_unsupported_facts_cannot_form_fresh_terms(
        CancellationTermsInput input
    )
    {
        CancellationTerms.Create(input, CancellationTestData.Now).IsError.ShouldBeTrue();
    }

    public static IEnumerable<object[]> InvalidInputs()
    {
        var input = CancellationTestData.Input();
        foreach (
            var invalid in new[]
            {
                input with
                {
                    Refund = null,
                },
                input with
                {
                    Refund = CancellationTestData.HistoricalMoney(-1m, "USD"),
                },
                input with
                {
                    Refund = CancellationTestData.HistoricalMoney(1m, "???"),
                },
                input with
                {
                    Refund = CancellationTestData.Money(1m, "ZZZ"),
                },
                input with
                {
                    Refund = CancellationTestData.Money(1m, "XXX"),
                },
                input with
                {
                    Refund = CancellationTestData.Money(1m, "XTS"),
                },
                input with
                {
                    ExpiresAt = null,
                },
                input with
                {
                    ExpiresAt = CancellationTestData.Now,
                },
                input with
                {
                    ExpiresAt = CancellationTestData.Now.AddSeconds(-1),
                },
                input with
                {
                    Revision = 0,
                },
                input with
                {
                    Revision = -1,
                },
                input with
                {
                    AggregateId = Guid.Empty,
                },
                input with
                {
                    OwnerId = Guid.Empty,
                },
                input with
                {
                    OperationId = Guid.Empty,
                },
                input with
                {
                    ProviderOrderRef = "",
                },
                input with
                {
                    ProviderCancellationRef = "",
                },
                input with
                {
                    ItineraryPartyHash = "not-a-scope-fingerprint",
                },
                input with
                {
                    NoticeVersion = "",
                },
                input with
                {
                    Destination = CancellationRefundDestination.Unknown,
                },
                input with
                {
                    Destination = (CancellationRefundDestination)999,
                },
                input with
                {
                    Settlement = input.Settlement with { CreditsKnownEmpty = false },
                },
                input with
                {
                    Settlement = input.Settlement with
                    {
                        Composition = SettlementComposition.Mixed,
                    },
                },
                input with
                {
                    Settlement = input.Settlement with
                    {
                        Composition = SettlementComposition.CreditOnly,
                    },
                },
                input with
                {
                    Settlement = input.Settlement with
                    {
                        Composition = SettlementComposition.Unknown,
                    },
                },
                input with
                {
                    Settlement = input.Settlement with { Composition = (SettlementComposition)999 },
                },
                input with
                {
                    Settlement = input.Settlement with
                    {
                        Provenance = CancellationResolutionSource.None,
                    },
                },
                input with
                {
                    Settlement = input.Settlement with
                    {
                        Provenance = CancellationResolutionSource.TravelAdmission,
                    },
                },
                input with
                {
                    Settlement = input.Settlement with
                    {
                        Provenance = (CancellationResolutionSource)999,
                    },
                },
                input with
                {
                    Destination = CancellationRefundDestination.OriginalFormOfPayment,
                },
                input with
                {
                    Destination = CancellationRefundDestination.AwaitingPayment,
                },
            }
        )
            yield return new object[] { invalid };
    }

    [Fact]
    public void Original_form_requires_independent_affirmative_cash_only_proof()
    {
        var input = CancellationTestData.Input();
        input = input with
        {
            Destination = CancellationRefundDestination.OriginalFormOfPayment,
            Settlement = input.Settlement with { OriginalCashOnlyVerified = true },
        };
        CancellationTerms.Create(input, CancellationTestData.Now).IsError.ShouldBeFalse();
    }

    [Fact]
    public void Positively_verified_unpaid_order_allows_only_zero_refund()
    {
        var input = CancellationTestData.Input();
        input = input with
        {
            Refund = CancellationTestData.Money(0m),
            Destination = CancellationRefundDestination.AwaitingPayment,
            Settlement = input.Settlement with
            {
                Composition = SettlementComposition.Unpaid,
                UnpaidOrderVerified = true,
            },
        };
        CancellationTerms.Create(input, CancellationTestData.Now).IsError.ShouldBeFalse();
        CancellationTerms
            .Create(
                input with
                {
                    Refund = CancellationTestData.Money(1m),
                },
                CancellationTestData.Now
            )
            .IsError.ShouldBeTrue();
        CancellationTerms
            .Create(
                input with
                {
                    Settlement = input.Settlement with { UnpaidOrderVerified = false },
                },
                CancellationTestData.Now
            )
            .IsError.ShouldBeTrue();
    }

    [Fact]
    public void Historical_terms_deserialization_does_not_apply_fresh_expiry_rules()
    {
        var oldNow = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var input = CancellationTestData.Input() with { ExpiresAt = oldNow.AddMinutes(1) };
        var historical = CancellationTerms.Create(input, oldNow).Value;
        var json = System.Text.Json.JsonSerializer.Serialize(historical);
        var restored = System.Text.Json.JsonSerializer.Deserialize<CancellationTerms>(json);
        restored.ShouldNotBeNull();
        restored.Hash.ShouldBe(historical.Hash);
        CancellationTerms.Create(input, CancellationTestData.Now).IsError.ShouldBeTrue();
    }

    [Theory]
    [InlineData(CancellationRefundDestination.Balance)]
    [InlineData(CancellationRefundDestination.Card)]
    [InlineData(CancellationRefundDestination.ArcBspCash)]
    public void Unpaid_order_does_not_claim_a_cash_paid_destination(
        CancellationRefundDestination destination
    )
    {
        var input = CancellationTestData.Input();
        input = input with
        {
            Refund = CancellationTestData.Money(0m),
            Destination = destination,
            Settlement = input.Settlement with
            {
                Composition = SettlementComposition.Unpaid,
                UnpaidOrderVerified = true,
            },
        };
        CancellationTerms.Create(input, CancellationTestData.Now).IsError.ShouldBeTrue();
    }

    [Fact]
    public void Unpaid_and_original_cash_paid_proofs_cannot_both_be_true()
    {
        var input = CancellationTestData.Input();
        input = input with
        {
            Refund = CancellationTestData.Money(0m),
            Destination = CancellationRefundDestination.AwaitingPayment,
            Settlement = input.Settlement with
            {
                Composition = SettlementComposition.Unpaid,
                UnpaidOrderVerified = true,
                OriginalCashOnlyVerified = true,
            },
        };
        CancellationTerms.Create(input, CancellationTestData.Now).IsError.ShouldBeTrue();
    }
}
