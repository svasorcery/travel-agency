namespace Travel.Modules.Flights.Infrastructure.Persistence.Entities;

public sealed class DeeplinkOfferCacheEntity
{
    public Guid Id { get; set; }
    public string CriteriaHash { get; set; } = default!;
    public string OffersJson { get; set; } = default!;
    public DateTimeOffset FetchedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}
