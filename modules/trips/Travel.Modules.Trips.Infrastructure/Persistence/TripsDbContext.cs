using Microsoft.EntityFrameworkCore;
using Travel.Modules.Trips.Application;
using Travel.Modules.Trips.Core;
using Travel.Modules.Trips.Infrastructure.Privacy;

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
    public string? ProtectedOption { get; set; }

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

// A receipt outlives deletion of its plan until the selection expires.
public sealed class SavedOptionReceipt
{
    public Guid TripId { get; set; }
    public string OwnerIssuer { get; set; } = "";
    public Guid OwnerSubject { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}

public sealed class TripsDbContext(DbContextOptions<TripsDbContext> options) : DbContext(options)
{
    public DbSet<TripPlanEntity> Plans => Set<TripPlanEntity>();
    public DbSet<TripFlightLinkEntity> FlightLinks => Set<TripFlightLinkEntity>();
    public DbSet<TripHotelLinkEntity> HotelLinks => Set<TripHotelLinkEntity>();
    public DbSet<SavedOptionReceipt> SavedOptionReceipts => Set<SavedOptionReceipt>();

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
        var receipt = modelBuilder.Entity<SavedOptionReceipt>();
        receipt.ToTable("saved_option_receipts");
        receipt.HasKey(x => x.TripId);
        receipt.Property(x => x.OwnerIssuer).HasMaxLength(512).IsRequired();
        receipt.HasIndex(x => x.ExpiresAt);
        plan.Property(x => x.Revision).IsConcurrencyToken();
        plan.HasIndex(x => new
            {
                x.OwnerIssuer,
                x.OwnerSubject,
                x.UpdatedAt,
                x.TripId,
            })
            .IsDescending(false, false, true, false);
        var links = modelBuilder.Entity<TripFlightLinkEntity>();
        links.ToTable(
            "trip_flight_links",
            table =>
                table.HasCheckConstraint(
                    "ck_trip_flight_links_revision_positive",
                    "links_revision > 0"
                )
        );
        links.HasKey(x => x.TripId);
        links
            .HasOne<TripPlanEntity>()
            .WithOne()
            .HasForeignKey<TripFlightLinkEntity>(x => x.TripId)
            .OnDelete(DeleteBehavior.Cascade);
        links.Property(x => x.OwnerIssuer).HasMaxLength(512).IsRequired();
        links.Property(x => x.LinksRevision).IsConcurrencyToken();
        links.Property(x => x.ProtectedPayload).HasMaxLength(8192).IsRequired();

        var hotelLink = modelBuilder.Entity<TripHotelLinkEntity>();
        hotelLink.ToTable(
            "trip_hotel_links",
            table =>
                table.HasCheckConstraint("ck_trip_hotel_links_revision_positive", "revision > 0")
        );
        hotelLink.HasKey(x => x.TripId);
        hotelLink
            .HasOne<TripPlanEntity>()
            .WithOne()
            .HasForeignKey<TripHotelLinkEntity>(x => x.TripId)
            .OnDelete(DeleteBehavior.Cascade);
        hotelLink.Property(x => x.OwnerIssuer).HasMaxLength(512).IsRequired();
        hotelLink.Property(x => x.Revision).IsConcurrencyToken();
        hotelLink
            .Property(x => x.ProtectedPayload)
            .HasMaxLength(TripHotelLinkProtector.MaximumEnvelopeLength)
            .IsRequired();
    }
}
