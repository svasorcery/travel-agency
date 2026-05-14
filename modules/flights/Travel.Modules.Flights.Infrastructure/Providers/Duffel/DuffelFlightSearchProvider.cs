using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ErrorOr;
using Microsoft.Extensions.Logging;
using Travel.Modules.Flights.Core.Errors;
using Travel.Modules.Flights.Core.Providers;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Travel.Modules.Flights.Core.ValueObjects.Offer;
using Travel.Modules.Flights.Infrastructure.Providers.Duffel.Dto;

namespace Travel.Modules.Flights.Infrastructure.Providers.Duffel;

public sealed class DuffelFlightSearchProvider(
    DuffelClient client,
    TimeProvider time,
    ILogger<DuffelFlightSearchProvider> log
) : IFlightSearchProvider
{
    // DTOs use [JsonPropertyName] attributes, so Web defaults (camelCase) plus our attributes are enough.
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public ProviderId Id => ProviderId.Duffel;

    public async Task<ErrorOr<IReadOnlyList<Offer>>> SearchAsync(
        SearchCriteria c,
        CancellationToken ct
    )
    {
        var body = BuildRequest(c);
        try
        {
            var resp = await client.PostAsync("/air/offer_requests?return_offers=true", body, ct);
            if (!resp.IsSuccessStatusCode)
            {
                log.LogWarning("Duffel search failed: {Status}", resp.StatusCode);
                return resp.StatusCode == HttpStatusCode.TooManyRequests
                    ? FlightsErrors.ProviderRateLimited("Duffel")
                    : FlightsErrors.ProviderUnavailable("Duffel");
            }

            var dto =
                await resp.Content.ReadFromJsonAsync<DuffelOfferRequestResponseDto>(JsonOpts, ct)
                ?? throw new InvalidOperationException("Empty Duffel response");

            var results = new List<Offer>(dto.Data.Offers.Length);
            foreach (var o in dto.Data.Offers)
            {
                var mapped = DuffelOfferMapper.Map(o, time);
                if (mapped.IsError)
                {
                    log.LogWarning(
                        "Skipping offer {Id}: {Error}",
                        o.Id,
                        mapped.FirstError.Description
                    );
                    continue;
                }
                results.Add(mapped.Value);
            }
            return results;
        }
        catch (TaskCanceledException)
        {
            return FlightsErrors.ProviderUnavailable("Duffel");
        }
        catch (HttpRequestException)
        {
            return FlightsErrors.ProviderUnavailable("Duffel");
        }
    }

    private static object BuildRequest(SearchCriteria c) =>
        new
        {
            cabin_class = c.CabinClass.Code,
            passengers = new[] { new { type = "adult" } },
            slices = c.IsRoundTrip
                ? new[]
                {
                    new
                    {
                        origin = c.Origin.Value,
                        destination = c.Destination.Value,
                        departure_date = c.DepartureDate.ToString("yyyy-MM-dd"),
                    },
                    new
                    {
                        origin = c.Destination.Value,
                        destination = c.Origin.Value,
                        departure_date = c.ReturnDate!.Value.ToString("yyyy-MM-dd"),
                    },
                }
                : new[]
                {
                    new
                    {
                        origin = c.Origin.Value,
                        destination = c.Destination.Value,
                        departure_date = c.DepartureDate.ToString("yyyy-MM-dd"),
                    },
                },
        };
}
