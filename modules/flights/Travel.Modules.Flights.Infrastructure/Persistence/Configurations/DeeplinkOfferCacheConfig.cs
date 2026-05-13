using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Travel.Modules.Flights.Infrastructure.Persistence.Entities;

namespace Travel.Modules.Flights.Infrastructure.Persistence.Configurations;

public sealed class DeeplinkOfferCacheConfig : IEntityTypeConfiguration<DeeplinkOfferCacheEntity>
{
    public void Configure(EntityTypeBuilder<DeeplinkOfferCacheEntity> b)
    {
        b.ToTable("deeplink_offers_cache", "flights");
        b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.CriteriaHash, x.ExpiresAt });
        b.Property(x => x.OffersJson).HasColumnType("jsonb");
    }
}
