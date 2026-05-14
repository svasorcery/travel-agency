using System.Net.Http.Json;
using System.Text.Json;
using ErrorOr;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Travel.Modules.Flights.Application.Search;
using Travel.Modules.Flights.Core.Errors;
using Travel.Modules.Flights.Core.Providers;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Travel.Modules.Flights.Core.ValueObjects.Offer;
using Travel.Modules.Flights.Infrastructure.Providers.Travelpayouts.Dto;

namespace Travel.Modules.Flights.Infrastructure.Providers.Travelpayouts;

public sealed class TravelpayoutsSearchProvider(
    TravelpayoutsClient client,
    IOptions<TravelpayoutsOptions> opts,
    TravelpayoutsDeeplinkBuilder deeplink,
    IDeeplinkOfferCache cache,
    TimeProvider time,
    ILogger<TravelpayoutsSearchProvider> log
) : IFlightSearchProvider
{
    public ProviderId Id => ProviderId.Travelpayouts;

    public async Task<ErrorOr<IReadOnlyList<Offer>>> SearchAsync(
        SearchCriteria c,
        CancellationToken ct
    )
    {
        var hash = SearchCacheKey.Build(c);
        var cached = await cache.TryGetAsync(hash, ct);
        if (cached is not null)
            return cached.Cast<Offer>().ToList();

        try
        {
            var path =
                $"/aviasales/v3/prices_for_dates?origin={c.Origin}&destination={c.Destination}"
                + $"&departure_at={c.DepartureDate:yyyy-MM-dd}"
                + (c.ReturnDate.HasValue ? $"&return_at={c.ReturnDate:yyyy-MM-dd}" : "")
                + $"&currency={c.Currency.Value.ToLowerInvariant()}&token={opts.Value.ApiToken}&limit=30";

            var resp = await client.GetAsync(path, ct);
            if (!resp.IsSuccessStatusCode)
                return FlightsErrors.ProviderUnavailable("Travelpayouts");

            var dto =
                await resp.Content.ReadFromJsonAsync<PricesForDatesResponseDto>(
                    new JsonSerializerOptions(JsonSerializerDefaults.Web),
                    ct
                ) ?? throw new InvalidOperationException("Empty Travelpayouts response");

            var offers = new List<DeeplinkOffer>();
            foreach (var d in dto.Data)
            {
                var mapped = TravelpayoutsOfferMapper.Map(d, c, deeplink, time);
                if (mapped.IsError)
                {
                    log.LogWarning("Skipping TP offer: {Error}", mapped.FirstError.Description);
                    continue;
                }
                offers.Add(mapped.Value);
            }

            await cache.SetAsync(hash, offers, ct);
            return offers.Cast<Offer>().ToList();
        }
        catch (TaskCanceledException)
        {
            return FlightsErrors.ProviderUnavailable("Travelpayouts");
        }
        catch (HttpRequestException)
        {
            return FlightsErrors.ProviderUnavailable("Travelpayouts");
        }
    }
}
