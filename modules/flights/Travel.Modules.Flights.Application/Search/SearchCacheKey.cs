using System.Security.Cryptography;
using System.Text;
using Travel.Modules.Flights.Core.ValueObjects;

namespace Travel.Modules.Flights.Application.Search;

public static class SearchCacheKey
{
    public static string Build(SearchCriteria c)
    {
        var raw =
            $"{c.Origin}|{c.Destination}|{c.DepartureDate:O}|{c.ReturnDate?.ToString("O") ?? "-"}"
            + $"|{c.PassengerCount}|{c.CabinClass.Code}|{c.Currency.Value}";
        return $"flights:search:{Hash(raw)}";
    }

    private static string Hash(string s)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(s));
        return Convert.ToHexString(bytes).ToLowerInvariant()[..16];
    }
}
