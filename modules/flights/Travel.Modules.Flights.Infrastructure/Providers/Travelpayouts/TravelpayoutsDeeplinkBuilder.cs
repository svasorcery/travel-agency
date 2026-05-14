using Microsoft.Extensions.Options;
using Travel.Modules.Flights.Core.ValueObjects;

namespace Travel.Modules.Flights.Infrastructure.Providers.Travelpayouts;

public sealed class TravelpayoutsDeeplinkBuilder(IOptions<TravelpayoutsOptions> opts)
{
    // Format: https://www.aviasales.ru/search/{ORIGIN}{DDMM}{DESTINATION}{RETURN_DDMM?}1?marker={marker}&utm_source=travel-platform
    // where DDMM = day + month zero-padded (e.g. 1507 for 15 July)
    public Uri Build(SearchCriteria c)
    {
        var dep = c.DepartureDate;
        var depPart = $"{dep.Day:D2}{dep.Month:D2}";

        var returnPart = string.Empty;
        if (c.ReturnDate.HasValue)
        {
            var ret = c.ReturnDate.Value;
            returnPart = $"{ret.Day:D2}{ret.Month:D2}";
        }

        var marker = opts.Value.PartnerMarker;
        var path =
            $"https://www.aviasales.ru/search/{c.Origin}{depPart}{c.Destination}{returnPart}1";
        var url = $"{path}?marker={Uri.EscapeDataString(marker)}&utm_source=travel-platform";

        return new Uri(url);
    }
}
