extern alias AppHost;

using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace Travel.Host.Tests.Integration;

public class AppHostTopologyTests
{
    [Fact]
    public async Task Host_waits_for_database_before_starting()
    {
        var builder =
            await DistributedApplicationTestingBuilder.CreateAsync<AppHost::Projects.Travel_AppHost>(
                cancellationToken: TestContext.Current.CancellationToken
            );

        var host = builder.Resources.Single(resource => resource.Name == "host");
        var waits = host
            .Annotations.OfType<WaitAnnotation>()
            .Where(annotation => annotation.WaitType == WaitType.WaitUntilHealthy)
            .Select(annotation => annotation.Resource.Name)
            .ToHashSet(StringComparer.Ordinal);

        waits.ShouldContain("travel");
    }

    [Theory]
    [InlineData("host", 5098)]
    [InlineData("ai", 5159)]
    public async Task Process_health_listener_is_internal_and_drives_resource_readiness(
        string resourceName,
        int port
    )
    {
        var builder =
            await DistributedApplicationTestingBuilder.CreateAsync<AppHost::Projects.Travel_AppHost>(
                cancellationToken: TestContext.Current.CancellationToken
            );

        var resource = builder.Resources.Single(candidate => candidate.Name == resourceName);
        var endpoint = resource
            .Annotations.OfType<EndpointAnnotation>()
            .Single(annotation => annotation.Name == "health-internal");

        endpoint.Port.ShouldBe(port);
        endpoint.TargetPort.ShouldBe(port);
        endpoint.UriScheme.ShouldBe("http");
        endpoint.IsExternal.ShouldBeFalse();
        endpoint.IsProxied.ShouldBeFalse();
        endpoint
            .GetType()
            .GetProperty(
                "TargetPortEnvironmentVariable",
                System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.NonPublic
            )!
            .GetValue(endpoint)
            .ShouldBe("HealthEndpoints__InternalPort");
        var healthCheckAnnotation = resource
            .Annotations.OfType<HealthCheckAnnotation>()
            .ShouldHaveSingleItem();
        using var serviceProvider = builder.Services.BuildServiceProvider();
        var healthCheckRegistration = serviceProvider
            .GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value.Registrations.Single(registration =>
                registration.Name == healthCheckAnnotation.Key
            );
        healthCheckRegistration.Name.ShouldBe(
            $"{resourceName}_health-internal_/health/ready_200_check"
        );

        var executionConfiguration = await ExecutionConfigurationBuilder
            .Create(resource)
            .WithEnvironmentVariablesConfig()
            .BuildAsync(
                new DistributedApplicationExecutionContext(
                    DistributedApplicationOperation.Publish,
                    "manifest"
                ),
                serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(resourceName),
                TestContext.Current.CancellationToken
            );
        var environment = executionConfiguration.EnvironmentVariables.ToDictionary();
        environment["HealthEndpoints__InternalPort"]
            .ShouldBe($"{{{resourceName}.bindings.health-internal.targetPort}}");
        environment.TryGetValue("ASPNETCORE_URLS", out var publicUrls).ShouldBeTrue();
        publicUrls.ShouldNotContain($":{port}");
    }
}
