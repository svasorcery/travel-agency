using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Travel.Modules.Trips.Api.Endpoints;
using Travel.Modules.Trips.Application.Itineraries;
using Travel.Modules.Trips.Application.Options;
using Travel.Modules.Trips.Core.Options;
using Travel.Modules.Trips.Infrastructure.Itineraries;

namespace Travel.Modules.Trips.Api.Composition;

public sealed record TripOptionFeature(bool Enabled)
{
    public bool SaveEnabled { get; private init; }
    internal Func<TripOptionPreview, bool> CanCreate { get; private init; } = _ => false;

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
        bool Withdrawn() =>
            config.GetValue<bool>("Trips:Itinerary:Withdrawn")
            || config.GetValue<bool>("Trips:Itinerary:Destinations:saint-petersburg:Withdrawn");
        var saving = enabled && config.GetValue<bool>("Trips:SavedOptions:Enabled");
        builder.Services.AddSingleton(
            new TripOptionFeature(enabled)
            {
                SaveEnabled = saving,
                CanCreate = p =>
                    saving
                    && !Withdrawn()
                    && p.CatalogueVersion == config["Trips:OptionPreview:Catalogue"]
                    && p.PolicyVersion == TripOptionPreview.Policy
                    && p.DatasetHash == Contracts.TripsItineraryProtocol.SaintPetersburgHash,
            }
        );
        if (!enabled)
            return;
        if (saving)
            builder.Services.AddSingleton<SavedOptionPreviewReadiness>();
        var release = CuratedItineraryRelease.ReviewedSaintPetersburgV1;
        builder.Services.AddSingleton(
            new TripOptionScenario(version!, TripOptionScenario.HistoricalInstant)
        );
        builder.Services.AddSingleton(services =>
        {
            var scenario = services.GetRequiredService<TripOptionScenario>();
            return new TripOptionService(
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
            );
        });
    }
}
