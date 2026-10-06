using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Shouldly;
using Travel.Modules.Flights.Core.Cancellation;
using Travel.Modules.Flights.Core.Providers.Dtos;
using Travel.Modules.Flights.Infrastructure.Providers.Duffel;
using Travel.Modules.Flights.Infrastructure.Providers.Duffel.Dto;

namespace Travel.Modules.Flights.Tests.Unit.Cancellation;

public sealed class DuffelCancellationMapperTests
{
    private static readonly DateTimeOffset Now = CancellationTestData.Now;
    private static readonly CancellationTerms Terms = CancellationTerms
        .Create(CancellationTestData.Input(), Now)
        .Value;

    [Fact]
    public void Explicit_zero_cash_with_empty_credits_is_supported_without_inventing_a_payout()
    {
        var dto = Dto(node => node["refund_amount"] = "0.00");
        var result = DuffelCancellationMapper.MapQuote(dto, Terms.ProviderOrderRef, null, Now);
        result.Outcome.ShouldBe(CancellationQuoteOutcome.TermsAvailable);
        result.Quote!.Refund!.Amount.ShouldBe(0m);
        result.Quote.Settlement.Composition.ShouldBe(SettlementComposition.CashOnly);
        result.Quote.Settlement.Provenance.ShouldBe(CancellationResolutionSource.SupplierApi);
    }

    [Fact]
    public void Explicit_unknown_financial_amount_is_completed_unsupported_not_zero()
    {
        var result = DuffelCancellationMapper.MapQuote(
            Dto(node => node["refund_amount"] = null),
            Terms.ProviderOrderRef,
            null,
            Now
        );
        result.Outcome.ShouldBe(CancellationQuoteOutcome.UnsupportedTerms);
        result.Quote.ShouldNotBeNull();
        result.Quote.Refund.ShouldBeNull();
    }

    [Theory]
    [InlineData("id")]
    [InlineData("order_id")]
    [InlineData("created_at")]
    [InlineData("confirmed_at")]
    [InlineData("refund_amount")]
    [InlineData("airline_credits")]
    public void Missing_required_shape_cannot_prove_create_completion(string field)
    {
        var result = DuffelCancellationMapper.MapQuote(
            Dto(node => node.Remove(field)),
            Terms.ProviderOrderRef,
            null,
            Now
        );
        result.Outcome.ShouldBe(CancellationQuoteOutcome.Unknown);
        result.Quote.ShouldBeNull();
    }

    [Theory]
    [InlineData("garbage")]
    [InlineData("-1.00")]
    [InlineData("1,000.00")]
    public void Malformed_or_negative_amount_is_unknown(string amount)
    {
        DuffelCancellationMapper
            .MapQuote(
                Dto(node => node["refund_amount"] = amount),
                Terms.ProviderOrderRef,
                null,
                Now
            )
            .Outcome.ShouldBe(CancellationQuoteOutcome.Unknown);
    }

    [Fact]
    public void Null_expiry_is_completed_but_unsupported()
    {
        var result = DuffelCancellationMapper.MapQuote(
            Dto(node => node["expires_at"] = null),
            Terms.ProviderOrderRef,
            null,
            Now
        );
        result.Outcome.ShouldBe(CancellationQuoteOutcome.UnsupportedTerms);
        result.Quote!.ExpiresAt.ShouldBeNull();
    }

    [Theory]
    [InlineData("balance")]
    [InlineData("original_form_of_payment")]
    public void Credits_or_mixed_grand_total_never_become_cash_only(string destination)
    {
        var dto = Dto(node =>
        {
            node["refund_to"] = destination;
            node["airline_credits"] = JsonNode.Parse(
                """[{"credit_amount":"17.25","credit_code":"FICTIONAL-CODE"}]"""
            );
        });
        var result = DuffelCancellationMapper.MapQuote(dto, Terms.ProviderOrderRef, null, Now);
        result.Outcome.ShouldBe(CancellationQuoteOutcome.UnsupportedTerms);
        result.Quote!.Settlement.Composition.ShouldNotBe(SettlementComposition.CashOnly);
        JsonSerializer.Serialize(result).ShouldNotContain("FICTIONAL-CODE");
    }

    [Fact]
    public void Original_form_requires_independent_positive_original_payment_facts()
    {
        var dto = Dto(node => node["refund_to"] = "original_form_of_payment");
        DuffelCancellationMapper
            .MapQuote(dto, Terms.ProviderOrderRef, null, Now)
            .Outcome.ShouldBe(CancellationQuoteOutcome.UnsupportedTerms);
        var independent = new CancellationEligibility(
            Terms.ProviderOrderRef,
            true,
            CancellationProviderPaymentState.Paid,
            CancellationTestData.Money(),
            new(
                SettlementComposition.CashOnly,
                true,
                CancellationResolutionSource.SupplierApi,
                true,
                false
            )
        );
        DuffelCancellationMapper
            .MapQuote(dto, Terms.ProviderOrderRef, independent, Now)
            .Outcome.ShouldBe(CancellationQuoteOutcome.TermsAvailable);
    }

    [Fact]
    public void Awaiting_payment_requires_positive_unpaid_order_and_zero_refund()
    {
        var dto = Dto(node =>
        {
            node["refund_to"] = "awaiting_payment";
            node["refund_amount"] = "0.00";
        });
        var unpaid = new CancellationEligibility(
            Terms.ProviderOrderRef,
            true,
            CancellationProviderPaymentState.AwaitingPayment,
            CancellationTestData.Money(),
            new(
                SettlementComposition.Unpaid,
                true,
                CancellationResolutionSource.SupplierApi,
                false,
                true
            )
        );
        DuffelCancellationMapper
            .MapQuote(dto, Terms.ProviderOrderRef, unpaid, Now)
            .Outcome.ShouldBe(CancellationQuoteOutcome.TermsAvailable);
        DuffelCancellationMapper
            .MapQuote(dto, Terms.ProviderOrderRef, null, Now)
            .Outcome.ShouldBe(CancellationQuoteOutcome.UnsupportedTerms);
        DuffelCancellationMapper
            .MapQuote(
                dto,
                Terms.ProviderOrderRef,
                unpaid with
                {
                    PaymentState = CancellationProviderPaymentState.Paid,
                },
                Now
            )
            .Outcome.ShouldBe(CancellationQuoteOutcome.UnsupportedTerms);
    }

    [Fact]
    public void Matching_confirmed_observation_can_recover_after_terms_expired()
    {
        var dto = Dto(node => node["confirmed_at"] = Now.AddSeconds(1).ToString("O"));
        var result = DuffelCancellationMapper.MapObservation(
            dto,
            new(
                Terms.ProviderOrderRef,
                Terms.ProviderCancellationRef,
                Terms,
                Terms.ItineraryPartyHash
            ),
            Now.AddSeconds(700)
        );
        result.State.ShouldBe(CancellationObservationState.Confirmed);
        result.Source.ShouldBe(CancellationResolutionSource.SupplierApi);
        result.ConfirmedAt.ShouldBe(Now.AddSeconds(1));
    }

    [Theory]
    [InlineData("order_id", "ord_foreign")]
    [InlineData("id", "occ_foreign")]
    [InlineData("refund_amount", "50.00")]
    [InlineData("refund_to", "card")]
    public void Confirmed_response_with_different_identity_or_terms_is_unknown(
        string field,
        string value
    )
    {
        var dto = Dto(node =>
        {
            node["confirmed_at"] = Now.AddSeconds(1).ToString("O");
            node[field] = value;
        });
        var result = DuffelCancellationMapper.MapObservation(
            dto,
            new(
                Terms.ProviderOrderRef,
                Terms.ProviderCancellationRef,
                Terms,
                Terms.ItineraryPartyHash
            ),
            Now.AddSeconds(2)
        );
        result.State.ShouldBe(CancellationObservationState.Unknown);
    }

    [Theory]
    [InlineData(false, "order_not_cancellable", CancellationReason.NotCancellable)]
    [InlineData(true, "order_not_cancellable", CancellationReason.ProviderUnavailable)]
    [InlineData(true, "order_cancellation_stale", CancellationReason.StaleProposal)]
    [InlineData(false, "order_cancellation_stale", CancellationReason.ProviderUnavailable)]
    [InlineData(true, "already_cancelled", CancellationReason.AlreadyCancelled)]
    public void Error_codes_are_scoped_to_documented_endpoint(
        bool confirmation,
        string code,
        CancellationReason expected
    )
    {
        DuffelCancellationMapper
            .ClassifyFailure(
                HttpStatusCode.UnprocessableEntity,
                [new(code, "invalid_state_error")],
                confirmation
            )
            .ShouldBe(expected);
        DuffelCancellationMapper
            .ClassifyFailure(
                HttpStatusCode.ServiceUnavailable,
                [new(code, "invalid_state_error")],
                confirmation
            )
            .ShouldBe(CancellationReason.ProviderUnavailable);
    }

    [Theory]
    [InlineData(CancellationResolutionSource.None)]
    [InlineData(CancellationResolutionSource.TravelAdmission)]
    [InlineData((CancellationResolutionSource)999)]
    public void Unpaid_order_without_valid_independent_provenance_stays_unsupported(
        CancellationResolutionSource source
    )
    {
        var dto = Dto(node =>
        {
            node["refund_to"] = "awaiting_payment";
            node["refund_amount"] = "0.00";
        });
        var independent = new CancellationEligibility(
            Terms.ProviderOrderRef,
            true,
            CancellationProviderPaymentState.AwaitingPayment,
            CancellationTestData.Money(),
            new(SettlementComposition.Unpaid, true, source, false, true)
        );
        DuffelCancellationMapper
            .MapQuote(dto, Terms.ProviderOrderRef, independent, Now)
            .Outcome.ShouldBe(CancellationQuoteOutcome.UnsupportedTerms);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Contradictory_original_payment_facts_do_not_prove_cash_only(bool awaiting)
    {
        var dto = Dto(node => node["refund_to"] = "original_form_of_payment");
        var independent = new CancellationEligibility(
            Terms.ProviderOrderRef,
            true,
            awaiting
                ? CancellationProviderPaymentState.AwaitingPayment
                : CancellationProviderPaymentState.Paid,
            CancellationTestData.Money(),
            new(
                SettlementComposition.CashOnly,
                true,
                CancellationResolutionSource.SupplierApi,
                true,
                !awaiting
            )
        );
        DuffelCancellationMapper
            .MapQuote(dto, Terms.ProviderOrderRef, independent, Now)
            .Outcome.ShouldBe(CancellationQuoteOutcome.UnsupportedTerms);
    }

    [Theory]
    [InlineData("refund_amount", "50.00")]
    [InlineData("id", "occ_foreign")]
    public void Proven_order_cancellation_is_retained_when_own_consent_does_not_match(
        string field,
        string value
    )
    {
        var dto = Dto(node =>
        {
            node["confirmed_at"] = Now.AddSeconds(1).ToString("O");
            node[field] = value;
        });
        var result = DuffelCancellationMapper.MapObservation(
            dto,
            new(
                Terms.ProviderOrderRef,
                Terms.ProviderCancellationRef,
                Terms,
                Terms.ItineraryPartyHash
            ),
            Now.AddSeconds(2)
        );
        result.State.ShouldBe(CancellationObservationState.Unknown);
        result.OrderCancellation.ShouldNotBeNull();
        result.OrderCancellation.ProviderOrderRef.ShouldBe(Terms.ProviderOrderRef);
        result.OrderCancellation.CancelledAt.ShouldBe(Now.AddSeconds(1));
    }

    internal static DuffelCancellationDetailsDto Dto(Action<JsonObject>? mutate = null)
    {
        var node = new JsonObject
        {
            ["id"] = Terms.ProviderCancellationRef,
            ["order_id"] = Terms.ProviderOrderRef,
            ["created_at"] = Now.ToString("O"),
            ["expires_at"] = Terms.ExpiresAt.ToString("O"),
            ["confirmed_at"] = null,
            ["refund_amount"] = "17.25",
            ["refund_currency"] = "USD",
            ["refund_to"] = "balance",
            ["airline_credits"] = new JsonArray(),
        };
        mutate?.Invoke(node);
        return JsonSerializer.Deserialize<DuffelCancellationDetailsDto>(node.ToJsonString())!;
    }
}
