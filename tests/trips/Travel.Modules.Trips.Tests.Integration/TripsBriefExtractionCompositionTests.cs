using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Travel.IntegrationContracts.AI.TripsBriefExtraction;
using Travel.Modules.Trips.Api.Composition;
using Travel.Modules.Trips.Application.BriefExtraction;

namespace Travel.Modules.Trips.Tests.Integration;

public sealed class TripsBriefExtractionCompositionTests
{
    [Theory]
    [InlineData(false, true, "Development")]
    [InlineData(true, false, "Development")]
    [InlineData(true, true, "Production")]
    public void Startup_off_or_production_has_no_extraction_transport_or_usecase(
        bool trips,
        bool extraction,
        string environment
    )
    {
        var builder = Host.CreateApplicationBuilder(
            new HostApplicationBuilderSettings { EnvironmentName = environment }
        );
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Trips:Enabled"] = trips.ToString(),
                ["Trips:BriefExtraction:Enabled"] = extraction.ToString(),
            }
        );
        builder.AddTripsModule();
        builder
            .Services.Single(x => x.ServiceType == typeof(TripsBriefExtractionFeature))
            .ImplementationInstance.ShouldBeOfType<TripsBriefExtractionFeature>()
            .Enabled.ShouldBeFalse();
        builder.Services.ShouldNotContain(x =>
            x.ServiceType == typeof(ITripBriefExtractionClient)
            || x.ServiceType == typeof(TripBriefExtractionService)
        );
    }

    [Theory]
    [InlineData(null, "fictional-model", "trips-brief-ru-v1")]
    [InlineData("unknown", "fictional-model", "trips-brief-ru-v1")]
    [InlineData("anthropic", null, "trips-brief-ru-v1")]
    [InlineData("anthropic", "fictional-model", null)]
    [InlineData("anthropic", "fictional-model", "unknown")]
    public void Missing_or_unknown_public_tuple_is_unconfigured(
        string? processor,
        string? model,
        string? notice
    )
    {
        var builder = Host.CreateApplicationBuilder(
            new HostApplicationBuilderSettings { EnvironmentName = Environments.Development }
        );
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Trips:Enabled"] = "true",
                ["Trips:BriefExtraction:Enabled"] = "true",
                ["Trips:BriefExtraction:ProcessorId"] = processor,
                ["Trips:BriefExtraction:ModelId"] = model,
                ["Trips:BriefExtraction:PrivacyNoticeVersion"] = notice,
            }
        );
        var feature = TripsBriefExtractionFeature.Read(builder.Configuration, builder.Environment);
        feature.Enabled.ShouldBeTrue();
        feature.Configured.ShouldBeFalse();
        feature.ProcessorId.ShouldBeNull();
        feature.ModelId.ShouldBeNull();
        feature.ProcessingPolicyVersion.ShouldBeNull();
    }

    [Fact]
    public void Exact_known_public_tuple_binds_notice_model_and_processor()
    {
        var builder = Host.CreateApplicationBuilder(
            new HostApplicationBuilderSettings { EnvironmentName = Environments.Development }
        );
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Trips:Enabled"] = "true",
                ["Trips:BriefExtraction:Enabled"] = "true",
                ["Trips:BriefExtraction:ProcessorId"] = "anthropic",
                ["Trips:BriefExtraction:ModelId"] = "fictional-model",
                ["Trips:BriefExtraction:PrivacyNoticeVersion"] = "trips-brief-ru-v1",
            }
        );
        var feature = TripsBriefExtractionFeature.Read(builder.Configuration, builder.Environment);
        feature.Configured.ShouldBeTrue();
        feature.ProcessingPolicyVersion.ShouldBe(
            TripsBriefPolicyIdentity.Compute(
                "anthropic",
                "fictional-model",
                TripsBriefMessageIdentity.PrivacyNoticeVersion
            )
        );
    }
}
