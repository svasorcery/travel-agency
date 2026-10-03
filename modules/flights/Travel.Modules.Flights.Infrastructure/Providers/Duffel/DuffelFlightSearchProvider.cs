using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ErrorOr;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Travel.Modules.Flights.Core.Errors;
using Travel.Modules.Flights.Core.Providers;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Travel.Modules.Flights.Core.ValueObjects.Offer;
using Travel.Modules.Flights.Infrastructure.Observability;
using Travel.Modules.Flights.Infrastructure.Providers.Duffel.Dto;

namespace Travel.Modules.Flights.Infrastructure.Providers.Duffel;

public sealed class DuffelFlightSearchProvider(
    DuffelClient client,
    IOptions<DuffelOptions> opts,
    TimeProvider time,
    ILogger<DuffelFlightSearchProvider> log
) : IFlightSearchProvider
{
    // DTOs use [JsonPropertyName] attributes, so Web defaults (camelCase) plus our attributes are enough.
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public ProviderId Id => ProviderId.Duffel;

    public FlightSearchSupport GetSupport(SearchCriteria criteria) => FlightSearchSupport.Available;

    public async Task<ErrorOr<IReadOnlyList<Offer>>> SearchAsync(
        SearchCriteria c,
        CancellationToken ct
    )
    {
        // Search calls have a stricter 4 s per-call budget (spec §19 / §3 dec.3 / §6.1).
        // The client-wide 10 s timeout covers order/booking calls; this linked CTS enforces
        // the tighter search deadline without affecting the outer caller's token.
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linkedCts.CancelAfter(TimeSpan.FromSeconds(opts.Value.SearchTimeoutSeconds));
        var searchCt = linkedCts.Token;

        var body = BuildRequest(c);
        using var span = FlightsActivitySource.Source.StartActivity("duffel.search");
        span?.SetTag("provider.id", "duffel");
        span?.SetTag("search.origin", c.Origin.Value);
        span?.SetTag("search.destination", c.Destination.Value);
        try
        {
            using var resp = await client.PostAsync(
                "/air/offer_requests?return_offers=true",
                body,
                searchCt
            );
            span?.SetTag("http.status_code", (int)resp.StatusCode);
            if (!resp.IsSuccessStatusCode)
            {
                log.LogWarning("Duffel search failed: {Status}", resp.StatusCode);
                span?.SetStatus(ActivityStatusCode.Error, resp.StatusCode.ToString());
                return resp.StatusCode == HttpStatusCode.TooManyRequests
                    ? FlightsErrors.ProviderRateLimited("Duffel")
                    : FlightsErrors.ProviderUnavailable("Duffel");
            }

            var dto = await resp.Content.ReadFromJsonAsync<DuffelOfferRequestResponseDto>(
                JsonOpts,
                searchCt
            );
            if (dto?.Data?.Offers is null)
                return FlightsErrors.ProviderUnavailable("Duffel");

            var results = new List<Offer>(dto.Data.Offers.Length);
            foreach (var o in dto.Data.Offers)
            {
                var mapped = DuffelOfferMapper.Map(o, time);
                if (mapped.IsError || !MatchesRequest(mapped.Value, c))
                {
                    log.LogWarning("Duffel search inventory is invalid.");
                    return FlightsErrors.ProviderUnavailable("Duffel");
                }
                results.Add(mapped.Value);
            }
            return results;
        }
        catch (OperationCanceledException)
        {
            return FlightsErrors.ProviderUnavailable("Duffel");
        }
        catch (HttpRequestException)
        {
            return FlightsErrors.ProviderUnavailable("Duffel");
        }
        catch (JsonException)
        {
            return FlightsErrors.ProviderUnavailable("Duffel");
        }
    }

    private static bool MatchesRequest(BookableOffer offer, SearchCriteria criteria)
    {
        var slices = offer.Itinerary.Slices;
        if (
            offer.Party?.PassengerCount != criteria.PassengerCount
            || slices.Count != criteria.Legs.Count
        )
            return false;
        for (var i = 0; i < slices.Count; i++)
        {
            if (DateOnly.FromDateTime(slices[i].DepartAt.Date) != criteria.Legs[i].DepartureDate)
                return false;
            if (
                criteria.RouteMode == SearchRouteMode.ExplicitAirportLegs
                && (
                    slices[i].Origin != criteria.Legs[i].Origin
                    || slices[i].Destination != criteria.Legs[i].Destination
                    || slices[i].Segments.Any(s => s.Cabin != criteria.CabinClass)
                )
            )
                return false;
        }
        return criteria.RouteMode != SearchRouteMode.LegacyLocations
            || (criteria.IsRoundTrip ? offer.Itinerary.IsRoundTrip : offer.Itinerary.IsOneWay);
    }

    private static object BuildRequest(SearchCriteria c) =>
        new
        {
            cabin_class = c.CabinClass.Code,
            passengers = Enumerable
                .Range(0, c.PassengerCount)
                .Select(_ => new { type = "adult" })
                .ToArray(),
            slices = c
                .Legs.Select(l => new
                {
                    origin = l.Origin.Value,
                    destination = l.Destination.Value,
                    departure_date = l.DepartureDate.ToString(
                        "yyyy-MM-dd",
                        CultureInfo.InvariantCulture
                    ),
                })
                .ToArray(),
        };
}
