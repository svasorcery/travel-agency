using Alba;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shouldly;
using Travel.Shared.Infrastructure.Initialization;
using Wolverine;
using Xunit;

namespace Travel.Host.Tests.Integration.Configuration;

public sealed class HostProgramConfigurationTests
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public Task Production_rejects_missing_settings_before_pipeline_mapping() =>
        AssertProductionStartupRejectedAsync(ProductionSettings.Missing);

    [Fact]
    public Task Production_rejects_canonical_loopback_settings_before_pipeline_mapping() =>
        AssertProductionStartupRejectedAsync(ProductionSettings.CanonicalLoopback);

    [Fact]
    public Task Valid_production_settings_start_without_external_runtime() =>
        StartProductionHostAsync(ProductionSettings.ValidNonLoopback);

    private static async Task AssertProductionStartupRejectedAsync(ProductionSettings settings)
    {
        var exception = await Should.ThrowAsync<Exception>(() =>
            StartProductionHostAsync(settings)
        );

        var validation = exception.GetBaseException().ShouldBeOfType<OptionsValidationException>();
        validation.Message.ShouldContain("Production travel connection");
        validation.Message.ShouldNotContain("program-lifecycle-secret-sentinel");
    }

    private static async Task StartProductionHostAsync(ProductionSettings settings)
    {
        var hostTask = AlbaHost.For<Program>(builder =>
        {
            builder.UseEnvironment(Environments.Production);
            builder.UseSetting("OTEL_EXPORTER_OTLP_ENDPOINT", string.Empty);
            builder.UseSetting("HealthEndpoints:InternalPort", "55098");
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureServices(DisableExternalRuntime);

            var hasSettings = settings is not ProductionSettings.Missing;
            var connectionHostName = settings switch
            {
                ProductionSettings.CanonicalLoopback => "LOCALHOST.",
                ProductionSettings.ValidNonLoopback => "service.example.invalid",
                _ => string.Empty,
            };
            var dependencyHostName = hasSettings ? "dependency.example.invalid" : string.Empty;
            builder.UseSetting(
                "ConnectionStrings:travel",
                hasSettings
                    ? $"Host={connectionHostName};Port=1;Database=travel;Password=program-lifecycle-secret-sentinel;Timeout=1"
                    : string.Empty
            );
            builder.UseSetting(
                "ConnectionStrings:nats",
                hasSettings ? $"nats://{connectionHostName}:1" : string.Empty
            );
            builder.UseSetting(
                "ConnectionStrings:redis",
                hasSettings ? $"{connectionHostName}:1" : string.Empty
            );
            builder.UseSetting(
                "Keycloak:Authority",
                hasSettings ? $"https://{dependencyHostName}:8443/realms/travel" : string.Empty
            );
            builder.UseSetting("Keycloak:Audience", hasSettings ? "travel-web" : string.Empty);
            builder.UseSetting("Flights:FeatureFlags:Travelpayouts:Enabled", "false");
            builder.UseSetting(
                "Flights:Duffel:BaseUrl",
                hasSettings ? $"https://{dependencyHostName}:9443" : string.Empty
            );
            builder.UseSetting(
                "Flights:Duffel:ApiKey",
                hasSettings ? "program-lifecycle-secret-sentinel" : string.Empty
            );
            builder.UseSetting(
                "Flights:Duffel:WebhookSecret",
                hasSettings ? "program-lifecycle-secret-sentinel" : string.Empty
            );
            builder.UseSetting("Flights:Duffel:TimeoutSeconds", hasSettings ? "10" : "0");
            builder.UseSetting("Flights:Duffel:SearchTimeoutSeconds", hasSettings ? "4" : "0");
            builder.UseSetting("Flights:Smtp:Host", dependencyHostName);
            builder.UseSetting("Flights:Smtp:Port", hasSettings ? "1025" : "0");
            builder.UseSetting(
                "Flights:Smtp:FromAddress",
                hasSettings ? "noreply@travel.example" : string.Empty
            );
            builder.UseSetting(
                "Flights:Providers:Frankfurter:BaseAddress",
                hasSettings ? $"https://{dependencyHostName}:9444" : string.Empty
            );
            builder.UseSetting(
                "Flights:Providers:Frankfurter:TimeoutSeconds",
                hasSettings ? "2" : "0"
            );
        });

        await using var host = await hostTask.WaitAsync(StartupTimeout);
    }

    private static void DisableExternalRuntime(IServiceCollection services)
    {
        services.DisableAllExternalWolverineTransports();
        RemoveHostedService(
            services,
            descriptor =>
                descriptor.ImplementationFactory?.Method.ReturnType == typeof(AppInitializer),
            nameof(AppInitializer)
        );
        RemoveHostedService(
            services,
            descriptor =>
                descriptor.ImplementationFactory?.Method.DeclaringType?.DeclaringType
                == typeof(Wolverine.HostBuilderExtensions),
            "WolverineRuntime"
        );
    }

    private static void RemoveHostedService(
        IServiceCollection services,
        Func<ServiceDescriptor, bool> matches,
        string owner
    )
    {
        var descriptors = services
            .Where(descriptor =>
                descriptor.ServiceType == typeof(IHostedService) && matches(descriptor)
            )
            .ToArray();
        descriptors.Length.ShouldBe(1, $"Expected one {owner} hosted-service descriptor.");
        services.Remove(descriptors[0]).ShouldBeTrue();
    }

    private enum ProductionSettings
    {
        Missing,
        CanonicalLoopback,
        ValidNonLoopback,
    }
}
