using Alba;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shouldly;
using Travel.AI.FareSummary;
using Travel.AI.NlSearch;
using Travel.AI.TripsBriefExtraction;
using Travel.IntegrationContracts.AI.TripsBriefExtraction;
using Travel.Shared.Infrastructure.Initialization;
using Wolverine;
using Xunit;

namespace Travel.AI.Tests.TripsBriefExtraction;

public sealed class TripsBriefCompositionTests
{
    [Theory]
    [InlineData("Testing", false, false)]
    [InlineData("Testing", true, true)]
    [InlineData("Production", true, false)]
    public async Task Real_program_applies_independent_default_off_and_production_exclusion(
        string environment,
        bool enabled,
        bool effective
    )
    {
        await using var host = await AlbaHost
            .For<Program>(builder =>
            {
                builder.UseEnvironment(environment);
                builder.UseSetting("OTEL_EXPORTER_OTLP_ENDPOINT", string.Empty);
                builder.UseSetting("HealthEndpoints:InternalPort", "55159");
                builder.UseSetting(
                    "ConnectionStrings:travel",
                    "Host=service.example.invalid;Port=1;Database=travel;Password=fictional;Timeout=1"
                );
                builder.UseSetting("ConnectionStrings:nats", "nats://service.example.invalid:1");
                builder.UseSetting("Anthropic:ApiKey", "fictional-unusable-key");
                builder.UseSetting("TripsBrief:Enabled", enabled.ToString());
                builder.ConfigureLogging(l => l.ClearProviders());
                builder.ConfigureServices(services =>
                {
                    services.DisableAllExternalWolverineTransports();
                    foreach (
                        var descriptor in services
                            .Where(s =>
                                s.ServiceType == typeof(IHostedService)
                                && (
                                    s.ImplementationFactory?.Method.ReturnType
                                        == typeof(AppInitializer)
                                    || s.ImplementationFactory?.Method.ReturnType
                                        == typeof(InitializationGatedHostedService)
                                )
                            )
                            .ToArray()
                    )
                        services.Remove(descriptor);
                });
            })
            .WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
        var options = host.Services.GetRequiredService<TripsBriefOptions>();
        options.Enabled.ShouldBe(effective);
        options.IsConfigured.ShouldBeFalse();
        options.Model.ShouldBeEmpty();
        // Resolving the independent wrapper creates no SDK connection/key lookup. Existing clients remain their own types.
        host.Services.GetRequiredKeyedService<IChatClient>(TripsBriefOptions.ChatClientKey)
            .ShouldBeOfType<TripsBriefChatClient>();
        host.Services.GetRequiredService<IChatClient>().ShouldBeOfType<NlSearchChatClient>();
        host.Services.GetRequiredService<FareSummaryChatClient>().ShouldNotBeNull();
        var request = new TripsBriefRequested(
            Guid.NewGuid(),
            "stale",
            "fictional",
            "ru",
            DateTimeOffset.UtcNow,
            null,
            null,
            DateTimeOffset.UtcNow.AddSeconds(5)
        );
        var result = await host
            .Services.GetRequiredService<TripsBriefExtractor>()
            .ExtractAsync(
                request,
                new TripsBriefExtractorTests.FakeLedger(),
                TestContext.Current.CancellationToken
            );
        result.Status.ShouldBe(effective ? "ModelUnavailable" : "Disabled");
    }
}
