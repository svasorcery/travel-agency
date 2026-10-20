using Microsoft.EntityFrameworkCore;
using Travel.Modules.Trips.Application;
using Travel.Modules.Trips.Core;

namespace Travel.Modules.Trips.Infrastructure.Persistence;

public sealed class TripPlanEntity
{
    public Guid TripId { get; set; }
    public string OwnerIssuer { get; set; } = "";
    public Guid OwnerSubject { get; set; }
    public long Revision { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string ProtectedPayload { get; set; } = "";

    internal TripPlanRecord ToRecord() =>
        new(
            TripId,
            OwnerIdentity.Create(OwnerIssuer, OwnerSubject).Value,
            Revision,
            CreatedAt,
            UpdatedAt,
            ProtectedPayload
        );

    internal static TripPlanEntity From(TripPlanRecord value) =>
        new()
        {
            TripId = value.TripId,
            OwnerIssuer = value.Owner.Issuer,
            OwnerSubject = value.Owner.Subject,
            Revision = value.Revision,
            CreatedAt = value.CreatedAt,
            UpdatedAt = value.UpdatedAt,
            ProtectedPayload = value.ProtectedPayload,
        };
}

public sealed class TripsDbContext(DbContextOptions<TripsDbContext> options) : DbContext(options)
{
    public DbSet<TripPlanEntity> Plans => Set<TripPlanEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("trips");
        var plan = modelBuilder.Entity<TripPlanEntity>();
        plan.ToTable(
            "trip_plans",
            table => table.HasCheckConstraint("ck_trip_plans_revision_positive", "revision > 0")
        );
        plan.HasKey(x => x.TripId);
        plan.Property(x => x.OwnerIssuer).HasMaxLength(512).IsRequired();
        plan.Property(x => x.ProtectedPayload).IsRequired();
        plan.Property(x => x.Revision).IsConcurrencyToken();
        plan.HasIndex(x => new
            {
                x.OwnerIssuer,
                x.OwnerSubject,
                x.UpdatedAt,
                x.TripId,
            })
            .IsDescending(false, false, true, false);
    }
}
