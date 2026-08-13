extern alias AppHost;

using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Shouldly;
using Xunit;

namespace Travel.Host.Tests.Integration;

public class AppHostTopologyTests
{
    [Fact]
    public async Task Host_waits_for_database_without_blocking_on_lazy_dependencies()
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
        foreach (var lazyDependency in new[] { "redis", "nats", "keycloak" })
        {
            waits.ShouldNotContain(lazyDependency);
        }
    }
}
