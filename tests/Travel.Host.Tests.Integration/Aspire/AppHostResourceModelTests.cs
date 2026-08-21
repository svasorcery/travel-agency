extern alias AppHost;

using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Shouldly;
using Xunit;

namespace Travel.Host.Tests.Integration.Aspire;

public sealed class AppHostResourceModelTests
{
    private static readonly string[] PersistentResourceNames =
    [
        "postgres",
        "redis",
        "nats",
        "keycloak",
    ];

    [Fact]
    public async Task Development_uses_persistent_named_volumes_by_default()
    {
        var builder = await CreateBuilderAsync("--environment=Development");

        foreach (var resourceName in PersistentResourceNames)
        {
            var resource = GetResource(builder, resourceName);
            resource
                .Annotations.OfType<ContainerMountAnnotation>()
                .Count(annotation => annotation.Type == ContainerMountType.Volume)
                .ShouldBe(1, $"{resourceName} should keep one persistent data volume by default.");
        }
    }

    [Fact]
    public async Task UseVolumes_false_attaches_no_named_data_volumes()
    {
        var builder = await CreateBuilderAsync("--environment=Testing", "UseVolumes=false");

        foreach (var resourceName in PersistentResourceNames)
        {
            GetResource(builder, resourceName)
                .Annotations.OfType<ContainerMountAnnotation>()
                .ShouldNotContain(annotation => annotation.Type == ContainerMountType.Volume);
        }
    }

    [Theory]
    [InlineData("host", "postgres", "travel", "nats", "redis", "keycloak")]
    [InlineData("ai", "postgres", "travel", "nats")]
    public async Task Process_wait_graph_matches_runtime_dependencies(
        string processName,
        params string[] expectedDependencies
    )
    {
        var builder = await CreateBuilderAsync("--environment=Testing", "UseVolumes=false");
        var resource = GetResource(builder, processName);

        var actualDependencies = resource
            .Annotations.OfType<WaitAnnotation>()
            .Where(annotation => annotation.WaitType == WaitType.WaitUntilHealthy)
            .Select(annotation => annotation.Resource.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        actualDependencies.ShouldBe(expectedDependencies.Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("host", 5098)]
    [InlineData("ai", 5159)]
    public async Task Process_exposes_named_internal_readiness_endpoint(
        string processName,
        int expectedPort
    )
    {
        var builder = await CreateBuilderAsync("--environment=Testing", "UseVolumes=false");
        var resource = GetResource(builder, processName);

        var endpoint = resource
            .Annotations.OfType<EndpointAnnotation>()
            .Single(annotation => annotation.Name == "health-internal");
        endpoint.Port.ShouldBe(expectedPort);
        endpoint.TargetPort.ShouldBe(expectedPort);
        endpoint.IsExternal.ShouldBeFalse();
        endpoint.IsProxied.ShouldBeFalse();

        resource.Annotations.OfType<HealthCheckAnnotation>().ShouldHaveSingleItem();
    }

    private static Task<IDistributedApplicationTestingBuilder> CreateBuilderAsync(
        params string[] args
    ) =>
        DistributedApplicationTestingBuilder.CreateAsync<AppHost::Projects.Travel_AppHost>(
            args,
            TestContext.Current.CancellationToken
        );

    private static IResource GetResource(
        IDistributedApplicationTestingBuilder builder,
        string resourceName
    ) => builder.Resources.Single(resource => resource.Name == resourceName);
}
