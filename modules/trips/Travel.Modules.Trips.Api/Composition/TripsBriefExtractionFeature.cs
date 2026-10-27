using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Travel.IntegrationContracts.AI.TripsBriefExtraction;

namespace Travel.Modules.Trips.Api.Composition;

public sealed record TripsBriefExtractionFeature(
    bool Enabled,
    string? ProcessorId = null,
    string? ModelId = null,
    string? ProcessingPolicyVersion = null
)
{
    public bool Configured =>
        Enabled
        && ProcessorId == "anthropic"
        && PublicIdentifier(ModelId)
        && ProcessingPolicyVersion
            == TripsBriefPolicyIdentity.Compute(
                ProcessorId,
                ModelId!,
                TripsBriefMessageIdentity.PrivacyNoticeVersion
            );

    public static TripsBriefExtractionFeature Read(
        IConfiguration configuration,
        IHostEnvironment environment
    )
    {
        var enabled =
            configuration.GetValue<bool>("Trips:Enabled")
            && configuration.GetValue<bool>("Trips:BriefExtraction:Enabled")
            && !environment.IsProduction();
        var processor = configuration["Trips:BriefExtraction:ProcessorId"];
        var model = configuration["Trips:BriefExtraction:ModelId"];
        var notice = configuration["Trips:BriefExtraction:PrivacyNoticeVersion"];
        return
            enabled
            && processor == "anthropic"
            && PublicIdentifier(model)
            && notice == TripsBriefMessageIdentity.PrivacyNoticeVersion
            ? new(
                true,
                processor,
                model,
                TripsBriefPolicyIdentity.Compute(processor, model!, notice)
            )
            : new(enabled);
    }

    private static bool PublicIdentifier(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length <= 128
        && value.All(c => c is >= '!' and <= '~');
}
