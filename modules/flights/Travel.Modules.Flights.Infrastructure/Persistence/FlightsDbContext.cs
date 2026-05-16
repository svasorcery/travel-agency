using Microsoft.EntityFrameworkCore;
using Travel.Modules.Flights.Infrastructure.Persistence.Entities;

namespace Travel.Modules.Flights.Infrastructure.Persistence;

public sealed class FlightsDbContext(DbContextOptions<FlightsDbContext> options)
    : DbContext(options)
{
    public DbSet<IdempotencyKeyEntity> IdempotencyKeys => Set<IdempotencyKeyEntity>();
    public DbSet<WebhookInboxEntity> WebhookInbox => Set<WebhookInboxEntity>();
    public DbSet<DeeplinkOfferCacheEntity> DeeplinkOffersCache => Set<DeeplinkOfferCacheEntity>();
    public DbSet<OrderReadModelEntity> Orders => Set<OrderReadModelEntity>();

    // Snake_case naming is the single source of truth for this context.  The host and
    // factory registrations also call UseSnakeCaseNamingConvention(), making this
    // idempotent; having it here ensures the convention applies even when the context
    // is constructed directly (e.g. in integration tests using EnsureCreatedAsync).
    protected override void OnConfiguring(DbContextOptionsBuilder b) =>
        b.UseSnakeCaseNamingConvention();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema("flights");
        b.ApplyConfigurationsFromAssembly(typeof(FlightsDbContext).Assembly);
    }
}
