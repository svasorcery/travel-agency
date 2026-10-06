using Shouldly;
using Travel.Modules.Flights.Core.Cancellation;

namespace Travel.Modules.Flights.Tests.Unit.Cancellation;

public sealed class CancellationEvidenceTests
{
    [Fact]
    public void Matching_supplier_confirmation_preserves_the_observed_supplier_fact()
    {
        var terms = CancellationTerms
            .Create(CancellationTestData.Input(), CancellationTestData.Now)
            .Value;
        var facts = CancellationTestData.Confirmation(terms);
        var result = CancellationEvidence.FromSupplierConfirmation(terms, facts, facts.ObservedAt);
        result.IsError.ShouldBeFalse();
        result.Value.Source.ShouldBe(CancellationResolutionSource.SupplierApi);
        result.Value.Refund.ShouldBe(terms.Refund);
    }

    [Fact]
    public void Operator_attestation_is_never_promoted_to_supplier_api_evidence()
    {
        var terms = CancellationTerms
            .Create(CancellationTestData.Input(), CancellationTestData.Now)
            .Value;
        var facts = CancellationTestData.Confirmation(terms) with
        {
            Source = CancellationResolutionSource.OperatorVerified,
        };
        CancellationEvidence
            .FromSupplierConfirmation(terms, facts, facts.ObservedAt)
            .IsError.ShouldBeTrue();
        var manual = CancellationEvidence.FromOperatorConfirmation(terms, facts, facts.ObservedAt);
        manual.IsError.ShouldBeFalse();
        manual.Value.Source.ShouldBe(CancellationResolutionSource.OperatorVerified);
    }

    [Theory]
    [MemberData(nameof(MismatchedFacts), DisableDiscoveryEnumeration = true)]
    public void Inconsistent_identity_money_scope_or_time_cannot_prove_success(
        CancellationConfirmationFacts facts
    )
    {
        var terms = CancellationTerms
            .Create(CancellationTestData.Input(), CancellationTestData.Now)
            .Value;
        CancellationEvidence
            .FromSupplierConfirmation(terms, facts, CancellationTestData.Now.AddSeconds(2))
            .IsError.ShouldBeTrue();
    }

    public static IEnumerable<object[]> MismatchedFacts()
    {
        var terms = CancellationTerms
            .Create(CancellationTestData.Input(), CancellationTestData.Now)
            .Value;
        var facts = CancellationTestData.Confirmation(terms);
        foreach (
            var changed in new[]
            {
                facts with
                {
                    ProviderOrderRef = "ord_fictional_other",
                },
                facts with
                {
                    ProviderCancellationRef = "occ_fictional_other",
                },
                facts with
                {
                    ItineraryPartyHash = new string('b', 64),
                },
                facts with
                {
                    Refund = CancellationTestData.Money(18m),
                },
                facts with
                {
                    Refund = CancellationTestData.Money(17.25m, "EUR"),
                },
                facts with
                {
                    Destination = CancellationRefundDestination.Card,
                },
                facts with
                {
                    Settlement = facts.Settlement with
                    {
                        Composition = SettlementComposition.Mixed,
                    },
                },
                facts with
                {
                    ConfirmedAt = null,
                },
                facts with
                {
                    ConfirmedAt = facts.ObservedAt.AddMinutes(1),
                },
                facts with
                {
                    ObservedAt = facts.ObservedAt.AddMinutes(1),
                },
                facts with
                {
                    Source = CancellationResolutionSource.None,
                },
            }
        )
            yield return new object[] { changed };
    }
}
