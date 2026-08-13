extern alias AppHost;

using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Shouldly;
using Xunit;

namespace Travel.Host.Tests.Integration;

public class AppHostTopologyTests
{
    [Theory]
    [InlineData("host", "travel", "redis", "nats", "keycloak")]
    [InlineData("ai", "travel", "redis", "nats")]
    public async Task Projects_wait_for_required_infrastructure_before_starting(
        string projectName,
        params string[] dependencies
    )
    {
        var builder =
            await DistributedApplicationTestingBuilder.CreateAsync<AppHost::Projects.Travel_AppHost>(
                cancellationToken: TestContext.Current.CancellationToken
            );

        var project = builder.Resources.Single(resource => resource.Name == projectName);
        var waits = project
            .Annotations.OfType<WaitAnnotation>()
            .Where(annotation => annotation.WaitType == WaitType.WaitUntilHealthy)
            .Select(annotation => annotation.Resource.Name)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var dependency in dependencies)
        {
            waits.ShouldContain(dependency);
        }
    }
}
