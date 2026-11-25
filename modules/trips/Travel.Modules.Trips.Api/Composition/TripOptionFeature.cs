using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Travel.Modules.Trips.Application.Itineraries;
using Travel.Modules.Trips.Application.Options;
using Travel.Modules.Trips.Infrastructure.Itineraries;

namespace Travel.Modules.Trips.Api.Composition;

public sealed record TripOptionFeature(bool Enabled)
{
    public static void Register(IHostApplicationBuilder builder, bool itineraryEnabled)
    {
        var config = builder.Configuration;
        var version = config["Trips:OptionPreview:Catalogue"];
        var enabled =
            itineraryEnabled
            && builder.Environment.IsEnvironment("Testing")
            && config.GetValue<bool>("Trips:OptionPreview:Enabled")
            && config["Trips:OptionPreview:Profile"] == "spb-option"
            && version is "A" or "B";
        builder.Services.AddSingleton(new TripOptionFeature(enabled));
        if (!enabled)
            return;
        var scenario = new TripOptionScenario(version!, TripOptionScenario.HistoricalInstant);
        var release = CuratedItineraryRelease.ReviewedSaintPetersburgV1;
        bool Withdrawn() =>
            config.GetValue<bool>("Trips:Itinerary:Withdrawn")
            || config.GetValue<bool>("Trips:Itinerary:Destinations:saint-petersburg:Withdrawn");
        builder.Services.AddSingleton(scenario);
        builder.Services.AddSingleton(services => new TripOptionService(
            scenario,
            new CuratedItineraryFacts(
                Path.Combine(
                    AppContext.BaseDirectory,
                    "Trips",
                    "Itineraries",
                    release.DatasetId,
                    release.Version
                ),
                scenario,
                Withdrawn,
                release
            ),
            services.GetRequiredService<ItineraryAdmissionGate>(),
            Withdrawn
        ));
    }
}
