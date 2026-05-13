using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Travel.Modules.Flights.Infrastructure.Persistence.Entities;

namespace Travel.Modules.Flights.Infrastructure.Persistence.Configurations;

public sealed class IdempotencyKeyConfig : IEntityTypeConfiguration<IdempotencyKeyEntity>
{
    public void Configure(EntityTypeBuilder<IdempotencyKeyEntity> b)
    {
        b.ToTable("idempotency_keys", "flights");
        b.HasKey(x => x.Key);
        b.HasIndex(x => x.ExpiresAt);
    }
}
