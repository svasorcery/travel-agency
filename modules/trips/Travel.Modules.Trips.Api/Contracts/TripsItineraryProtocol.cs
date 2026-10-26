using ErrorOr;
using Microsoft.AspNetCore.Http;
using Travel.Modules.Trips.Core;
using Travel.Modules.Trips.Core.Itineraries;

namespace Travel.Modules.Trips.Api.Contracts;

public static class TripsItineraryProtocol
{
    public const string Header = "X-Trips-Itinerary-Version";
    public const string MoscowHash =
        "631fd5cd9650e3228af87ef8a31219d7ea59a2e4af42bc4328453f32268ffb08";
    public const string SaintPetersburgHash =
        "dd128fa989936720b2e5cd0893a658b4878c49b4be99ba6aad5c89dfe91a3d12";
    public const string SaintPetersburgOffer =
        "/trips-data/saint-petersburg-outdoor/1/saint-petersburg-v1.manifest.json";

    public static ErrorOr<int> Version(HttpRequest request)
    {
        if (!request.Headers.TryGetValue(Header, out var values))
            return 1;
        return values.Count == 1 && values[0] == "2" ? 2 : TripErrors.InvalidInput;
    }

    public static ErrorOr<DestinationSelection> AvailabilityDestination(
        HttpRequest request,
        int version
    )
    {
        if (version == 1)
            return request.Query.Count == 0
                ? CityCatalogue.Find("moscow")
                : TripErrors.InvalidInput;
        if (
            version != 2
            || request.Query.Count != 1
            || request.Query.Keys.Single() != "destinationId"
            || !request.Query.TryGetValue("destinationId", out var values)
            || values.Count != 1
        )
            return TripErrors.InvalidInput;
        var city = CityCatalogue.Find(values[0] ?? "");
        return city.IsError ? TripErrors.InvalidInput : city.Value;
    }

    public static bool Supports(string destinationId, int version) =>
        destinationId == "moscow" || version == 2 && destinationId == "saint-petersburg";

    // Public wire identities are independent of Infrastructure DTOs and source directory discovery.
    // A data release must update the reader pin, this explicit tuple and client tuple together.
    public static bool MatchesReleasedTuple(ValidatedItineraryFacts facts)
    {
        var city = facts.Destination.DestinationId;
        return facts.Destination.TimeZone == "Europe/Moscow"
            && facts.Destination.CatalogueVersion == "1"
            && facts.Version == "1"
            && (
                city == "moscow"
                    && facts.DatasetId == "moscow-outdoor"
                    && facts.DatasetHash == MoscowHash
                    && facts.ModelSource.ProfileVersion == "profile-v1"
                    && facts.ModelSource.SourceUrl
                        == "https://download.bbbike.org/osm/bbbike/Moscow/Moscow.osm.gz"
                || city == "saint-petersburg"
                    && facts.DatasetId == "saint-petersburg-outdoor"
                    && facts.DatasetHash == SaintPetersburgHash
                    && facts.ModelSource.ProfileVersion == "spb-outdoor-profile-v1"
                    && facts.ModelSource.SourceUrl
                        == "https://download.bbbike.org/osm/bbbike/SanktPetersburg/SanktPetersburg.osm.gz"
            );
    }

    public static string Offer(ValidatedItineraryFacts facts) =>
        facts.Destination.DestinationId == "saint-petersburg"
            ? SaintPetersburgOffer
            : ItineraryCandidateResponseEncoder.DerivedDataOffer;
}
