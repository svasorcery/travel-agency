using Shouldly;
using Travel.AI.FareSummary;
using Travel.Tests.AiEvals.Flights.FareSummary;
using Xunit;

namespace Travel.AI.Tests.FareSummary;

public sealed class FareSummaryPaidGateTests
{
    [Theory]
    [InlineData(false, "APPROVED_FICTIONAL_FARE_SUMMARY_EVAL", "fictional-unused-key")]
    [InlineData(true, null, "fictional-unused-key")]
    [InlineData(true, "different-token", "fictional-unused-key")]
    [InlineData(true, "APPROVED_FICTIONAL_FARE_SUMMARY_EVAL", null)]
    public void A_key_alone_never_authorizes_paid_evaluation(
        bool authorized,
        string? token,
        string? key
    )
    {
        Should.Throw<InvalidOperationException>(() =>
            FareSummaryPaidEvalRunner.ValidateAuthorization(authorized, token, key, Verified())
        );
    }

    [Fact]
    public void Explicit_gate_accepts_verified_configuration_without_invoking_a_client()
    {
        Should.NotThrow(() =>
            FareSummaryPaidEvalRunner.ValidateAuthorization(
                true,
                FareSummaryPaidEvalRunner.AuthorizationToken,
                "fictional-unused-key",
                Verified()
            )
        );
    }

    [Fact]
    public void Exact_model_price_metadata_is_also_required()
    {
        var options = Verified();
        options.PricingModel = "other-model";
        Should.Throw<InvalidOperationException>(() =>
            FareSummaryPaidEvalRunner.ValidateAuthorization(
                true,
                FareSummaryPaidEvalRunner.AuthorizationToken,
                "fictional-unused-key",
                options
            )
        );
    }

    private static FareSummaryOptions Verified() =>
        new()
        {
            Enabled = true,
            Model = "fictional",
            PricingModel = "fictional",
            PricingVerified = true,
            InputUsdPerMillion = 1,
            OutputUsdPerMillion = 1,
        };
}
