using ErrorOr;
using Travel.Modules.Flights.Core.Providers;
using Travel.Modules.Flights.Core.Providers.Dtos;
using Travel.Modules.Flights.Infrastructure.Providers.Duffel.Dto;

namespace Travel.Modules.Flights.Infrastructure.Providers.Duffel;

public sealed class DuffelFlightAncillaryProvider(DuffelClient client, TimeProvider time)
    : IFlightAncillaryProvider
{
    public async Task<ErrorOr<AncillaryCatalogFacts>> ReadCatalogAsync(
        string providerOfferRef,
        bool includeSeats,
        CancellationToken ct
    )
    {
        var unavailable = Error.Failure(
            "Flights.AncillaryCatalogUnavailable",
            "Current services are unavailable."
        );
        if (!DuffelAncillaryMapper.Reference(providerOfferRef))
            return unavailable;
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(10), time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, budget.Token);
        try
        {
            using var response = await client.GetAsync(
                $"/air/offers/{providerOfferRef}?return_available_services=true",
                HttpCompletionOption.ResponseHeadersRead,
                linked.Token
            );
            if (!response.IsSuccessStatusCode)
                return unavailable;
            var offer = (
                await DuffelBoundedJson.Read<DuffelOfferResponseDto>(
                    response,
                    2 * 1024 * 1024,
                    linked.Token
                )
            )?.Data;
            if (
                offer is null
                || offer.Id != providerOfferRef
                || offer.ExpiresAt <= time.GetUtcNow()
            )
                return unavailable;
            DuffelSeatMapDto[]? maps = null;
            if (includeSeats)
            {
                try
                {
                    using var seatResponse = await client.GetAsync(
                        $"/air/seat_maps?offer_id={providerOfferRef}",
                        HttpCompletionOption.ResponseHeadersRead,
                        linked.Token
                    );
                    if (seatResponse.IsSuccessStatusCode)
                        maps = (
                            await DuffelBoundedJson.Read<DuffelSeatMapsResponseDto>(
                                seatResponse,
                                4 * 1024 * 1024,
                                linked.Token
                            )
                        )?.Data;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception)
                {
                    maps = null;
                }
            }
            var mapped = DuffelAncillaryMapper.Map(offer, maps, includeSeats, time);
            return mapped.IsError && includeSeats && maps is not null
                ? DuffelAncillaryMapper.Map(offer, null, true, time)
                : mapped;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return unavailable;
        }
    }
}
