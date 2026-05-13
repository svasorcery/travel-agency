using ArchUnitNET.Domain;
using ArchUnitNET.Loader;
using Travel.Shared.Abstractions;

namespace Travel.Tests.Architecture;

public static class ArchitectureTestBase
{
    private static readonly Lazy<global::ArchUnitNET.Domain.Architecture> Lazy = new(() =>
        new ArchLoader()
            .LoadAssemblies(
                typeof(IModuleAssemblyMarker).Assembly,
                typeof(Modules.Flights.Core.FlightsModuleMarker).Assembly,
                typeof(Modules.Hotels.Core.HotelsModuleMarker).Assembly,
                typeof(Modules.Rail.Core.RailModuleMarker).Assembly,
                typeof(Modules.Trips.Core.TripsModuleMarker).Assembly,
                typeof(Modules.Identity.Core.IdentityModuleMarker).Assembly
            )
            .Build()
    );

    public static global::ArchUnitNET.Domain.Architecture Architecture => Lazy.Value;
}
