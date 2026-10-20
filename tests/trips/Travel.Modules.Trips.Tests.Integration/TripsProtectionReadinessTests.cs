using Microsoft.Extensions.Options;
using Shouldly;
using Travel.Modules.Trips.Infrastructure.Privacy;
using Xunit;

namespace Travel.Modules.Trips.Tests.Integration;

public sealed class TripsProtectionReadinessTests
{
    [Fact]
    public void Missing_configuration_fails_closed_without_creating_keys()
    {
        using var provider = new TripsProtectionProvider(
            Options.Create(new TripsProtectionOptions()),
            TimeProvider.System
        );
        provider.IsReady.ShouldBeFalse();
        provider.Protect([1, 2, 3], "fixture").IsError.ShouldBeTrue();
    }

    [Fact]
    public void Missing_ring_is_not_created_by_readiness()
    {
        var path = Path.Combine(Path.GetTempPath(), "trips-absent-" + Guid.NewGuid().ToString("N"));
        using var provider = new TripsProtectionProvider(
            Options.Create(
                new TripsProtectionOptions
                {
                    KeyRingPath = path,
                    ActiveCertificatePath = Path.Combine(path, "missing.pfx"),
                }
            ),
            TimeProvider.System
        );
        provider.IsReady.ShouldBeFalse();
        Directory.Exists(path).ShouldBeFalse();
    }
}
