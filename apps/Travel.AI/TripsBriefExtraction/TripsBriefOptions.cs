using Travel.IntegrationContracts.AI.TripsBriefExtraction;

namespace Travel.AI.TripsBriefExtraction;

// Startup-bound and independent of Flights. Publishing processor metadata grants no execution authority.
public sealed class TripsBriefOptions
{
    public const string SectionName = "TripsBrief";
    public const string ChatClientKey = "trips-brief";
    public bool Enabled { get; set; }
    public bool ExecutionAuthorized { get; set; }
    public bool PrivateTextProcessingAuthorized { get; set; }
    public string ProcessorId { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string PricingModel { get; set; } = string.Empty;
    public bool PricingVerified { get; set; }
    public decimal InputUsdPerMillion { get; set; }
    public decimal OutputUsdPerMillion { get; set; }
    public decimal MaxRequestCostUsd { get; set; }
    public decimal MaxHourlyCostUsd { get; set; }
    public string ProcessingPolicyVersion =>
        TripsBriefPolicyIdentity.Compute(
            ProcessorId,
            Model,
            TripsBriefMessageIdentity.PrivacyNoticeVersion
        );
    public bool IsConfigured =>
        ProcessorId == "anthropic"
        && PublicIdentifier(Model)
        && PricingModel == Model
        && PricingVerified
        && ExecutionAuthorized
        && PrivateTextProcessingAuthorized
        && InputUsdPerMillion > 0
        && OutputUsdPerMillion > 0
        && MaxRequestCostUsd is > 0 and <= .03m
        && MaxHourlyCostUsd is > 0 and <= .30m
        && MaxHourlyCostUsd >= MaxRequestCostUsd;

    private static bool PublicIdentifier(string? value) =>
        value is { Length: > 0 and <= 128 } && value.All(c => c is >= '!' and <= '~');
}
