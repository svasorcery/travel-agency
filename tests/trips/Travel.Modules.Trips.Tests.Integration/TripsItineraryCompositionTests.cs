using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Travel.Modules.Trips.Api.Composition;
using Travel.Modules.Trips.Application;
using Travel.Modules.Trips.Application.Itineraries;
using Travel.Modules.Trips.Infrastructure.Itineraries;
using Xunit;

namespace Travel.Modules.Trips.Tests.Integration;

public sealed class TripsItineraryCompositionTests
{
    [Theory]
    [InlineData(false, false, "Development", false)]
    [InlineData(false, true, "Development", false)]
    [InlineData(true, false, "Development", false)]
    [InlineData(true, true, "Development", true)]
    [InlineData(true, true, "Production", false)]
    [InlineData(true, false, "Production", false)]
    public void Effective_flag_fences_all_demo_registrations(
        bool trips,
        bool requested,
        string environment,
        bool effective
    )
    {
        var builder = Host.CreateApplicationBuilder(
            new HostApplicationBuilderSettings { EnvironmentName = environment }
        );
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Trips:Enabled"] = trips.ToString(),
                ["Trips:Itinerary:Enabled"] = requested.ToString(),
                ["ConnectionStrings:travel"] =
                    "Host=unused;Database=unused;Username=unused;Password=unused",
            }
        );
        builder.AddTripsModule();
        builder
            .Services.Single(d => d.ServiceType == typeof(TripsItineraryFeature))
            .ImplementationInstance.ShouldBe(new TripsItineraryFeature(effective));
        foreach (
            var type in new[]
            {
                typeof(IItineraryFactSource),
                typeof(IItineraryCandidateResponseEncoder),
                typeof(ItineraryAdmissionGate),
                typeof(ItineraryCandidateService),
            }
        )
            builder.Services.Count(d => d.ServiceType == type).ShouldBe(effective ? 1 : 0);
        builder
            .Services.Any(d => d.ImplementationType == typeof(CuratedMoscowFacts))
            .ShouldBeFalse();
        builder.Services.Any(d => d.ServiceType == typeof(TripPlanService)).ShouldBe(trips);
        if (effective)
        {
            builder
                .Services.Single(d => d.ServiceType == typeof(ItineraryAdmissionGate))
                .Lifetime.ShouldBe(ServiceLifetime.Singleton);
            builder
                .Services.Single(d => d.ServiceType == typeof(ItineraryCandidateService))
                .Lifetime.ShouldBe(ServiceLifetime.Scoped);
        }
    }
}
