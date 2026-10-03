using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Travel.Modules.Flights.Core.ValueObjects;

namespace Travel.Modules.Flights.Application.Search;

public static class SearchCacheKey
{
    // Criteria-only identity is used by the provider deeplink cache, never normal search.
    public static string Build(SearchCriteria c) => Build(c, []);

    public static string Build(
        SearchCriteria c,
        IReadOnlyList<SearchProviderCapability> capabilities
    )
    {
        var raw = JsonSerializer.Serialize(
            new
            {
                c.RouteMode,
                Legs = c.Legs.Select(l => new
                {
                    Origin = l.Origin.Value,
                    Destination = l.Destination.Value,
                    l.DepartureDate,
                }),
                c.PassengerCount,
                Cabin = c.CabinClass.Code,
                Currency = c.Currency.Value,
                c.Locale,
                Policy = OfferRanker.Policy,
                Providers = capabilities
                    .OrderBy(p => p.Provider, StringComparer.Ordinal)
                    .ThenBy(p => p.Supported)
                    .ThenBy(p => p.ReasonCode, StringComparer.Ordinal),
            }
        );
        return $"flights:search:v4:{OfferRanker.Policy}:{Hash(raw)}";
    }

    private static string Hash(string s)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(s));
        return Convert.ToHexString(bytes).ToLowerInvariant()[..16];
    }
}
