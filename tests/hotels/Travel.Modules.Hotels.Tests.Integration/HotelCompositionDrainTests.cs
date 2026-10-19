using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Travel.Modules.Hotels.Api.Composition;
using Travel.Modules.Hotels.Core;

namespace Travel.Modules.Hotels.Tests.Integration;

public sealed class HotelCompositionDrainTests
{
    [Fact]
    public void M2_off_keeps_isolated_M1_search_but_retains_both_booking_readers_without_starting_a_host()
    {
        var builder = Host.CreateApplicationBuilder(
            new HostApplicationBuilderSettings
            {
                EnvironmentName = "Testing",
                DisableDefaults = true,
            }
        );
        builder.AddHotelsModule();
        using var services = builder.Services.BuildServiceProvider();
        services.GetServices<IHotelSearchProvider>().Select(s => s.Key).ShouldBe(["duffel-stays"]);
        services
            .GetServices<IHotelBookingProvider>()
            .Select(s => s.Key)
            .Order(StringComparer.Ordinal)
            .ShouldBe(["duffel-stays", "hotelbeds"]);
        services
            .GetServices<IHotelBookingProvider>()
            .ShouldAllBe(p => p is IHotelReservationReader);
    }
}
