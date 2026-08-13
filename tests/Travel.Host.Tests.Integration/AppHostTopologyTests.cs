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
}
