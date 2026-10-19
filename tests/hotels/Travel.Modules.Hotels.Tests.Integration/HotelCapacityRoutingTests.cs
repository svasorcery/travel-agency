using Microsoft.Extensions.Options;
using Shouldly;
using Travel.Modules.Hotels.Infrastructure.Providers.Duffel;

namespace Travel.Modules.Hotels.Tests.Integration;

public sealed class HotelCapacityRoutingTests
{
    [Fact]
    public void Provider_foreground_recovery_and_rate_limit_are_independent()
    {
        var capacity = new HotelCapacity(Options.Create(new HotelsOptions()), TimeProvider.System);
        using var duffel = capacity.TryAcquire("duffel-stays");
        using var hotelbeds = capacity.TryAcquire("hotelbeds");
        duffel.ShouldNotBeNull();
        hotelbeds.ShouldNotBeNull();
        capacity.TryAcquire("duffel-stays").ShouldBeNull();
        using var recovery = capacity.TryAcquire("duffel-stays", true);
        recovery.ShouldNotBeNull();
        capacity.Active.ShouldBe(3);
    }
}
