using Travel.Modules.Flights.Application.Search;
using Wolverine.Attributes;

namespace Travel.Modules.Flights.Infrastructure.BackgroundJobs;

public sealed record PurgeExpiredDeeplinkOffers;

public static class PurgeExpiredDeeplinkOffersHandler
{
    [WolverineHandler]
    public static Task Handle(
        PurgeExpiredDeeplinkOffers _,
        IDeeplinkOfferCache cache,
        CancellationToken ct
    ) => cache.PurgeExpiredAsync(ct);
}
