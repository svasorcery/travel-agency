using Microsoft.EntityFrameworkCore;

namespace Travel.Modules.Hotels.Infrastructure.Persistence;

public sealed class HotelsDbContext(DbContextOptions<HotelsDbContext> options) : DbContext(options)
{
    public DbSet<HotelReservationEntity> Reservations => Set<HotelReservationEntity>();
    public DbSet<HotelInboxEntity> Inbox => Set<HotelInboxEntity>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.HasDefaultSchema("hotels");
        var r = model.Entity<HotelReservationEntity>();
        r.ToTable(
            "reservations",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_hotels_revision",
                    "version > 0 AND quote_revision > 0"
                );
                table.HasCheckConstraint(
                    "ck_hotels_attempt",
                    "(attempt_id IS NULL AND action_id IS NULL AND admitted_at IS NULL) OR (attempt_id IS NOT NULL AND action_id IS NOT NULL AND admitted_at IS NOT NULL)"
                );
            }
        );
        r.HasKey(x => x.Id);
        r.Property(x => x.Version).IsConcurrencyToken();
        r.Property(x => x.Provider).HasMaxLength(64);
        r.Property(x => x.Environment).HasMaxLength(16);
        r.Property(x => x.SourcePreparationReference).HasMaxLength(160);
        r.Property(x => x.ProviderAccount).HasMaxLength(160);
        r.Property(x => x.BookingReference).HasMaxLength(160);
        r.Property(x => x.ProviderRequestReference).HasMaxLength(160);
        r.Property(x => x.OutcomeDiagnostic).HasMaxLength(64);
        r.Property(x => x.TermsJson).HasColumnType("jsonb");
        r.Property(x => x.StateJson).HasColumnType("jsonb");
        r.Property(x => x.ProtectedContext).HasMaxLength(32768);
        r.Property(x => x.ProtectedParty).HasMaxLength(32768);
        r.HasIndex(x => new
        {
            x.OwnerId,
            x.CreatedAt,
            x.Id,
        });
        r.HasIndex(x => new
            {
                x.Provider,
                x.ProviderAccount,
                x.Environment,
                x.SourcePreparationReference,
            })
            .IsUnique();
        r.HasIndex(x => new
            {
                x.Provider,
                x.ProviderAccount,
                x.Environment,
                x.BookingReference,
            })
            .IsUnique();
        r.HasIndex(x => new
            {
                x.OwnerId,
                x.Id,
                x.ActionId,
            })
            .IsUnique();
        r.HasIndex(x => x.AttemptId).IsUnique();
        r.HasIndex(x => new { x.PrivatePurgeAt, x.Id });
        var i = model.Entity<HotelInboxEntity>();
        i.ToTable("observation_inbox");
        i.HasKey(x => x.Id);
        i.Property(x => x.EventId).HasMaxLength(160);
        i.Property(x => x.EventType).HasMaxLength(80);
        i.Property(x => x.Receiver).HasMaxLength(160);
        i.Property(x => x.PreparationReference).HasMaxLength(160);
        i.Property(x => x.ProtectedPayload).HasMaxLength(4 * 1024 * 1024);
        i.HasIndex(x => new { x.Receiver, x.EventId }).IsUnique();
        i.HasIndex(x => x.ReceivedAt);
    }
}

public sealed class HotelReservationEntity
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public int Version { get; set; }
    public int QuoteRevision { get; set; }
    public string Provider { get; set; } = "";
    public string Environment { get; set; } = "";
    public string ProviderAccount { get; set; } = "configured-test-account";
    public string? BookingReference { get; set; }
    public string? SourcePreparationReference { get; set; }
    public string? ProviderRequestReference { get; set; }
    public string? OutcomeDiagnostic { get; set; }
    public string? TermsJson { get; set; }
    public string StateJson { get; set; } = "";
    public DateTimeOffset ValidUntil { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? AttemptId { get; set; }
    public Guid? ActionId { get; set; }
    public DateTimeOffset? AdmittedAt { get; set; }
    public bool SenderFinished { get; set; }
    public string? ProtectedContext { get; set; }
    public string? ProtectedParty { get; set; }
    public DateTimeOffset? PrivatePurgeAt { get; set; }
}

public sealed class HotelInboxEntity
{
    public Guid Id { get; set; }
    public string Receiver { get; set; } = "";
    public string EventId { get; set; } = "";
    public string EventType { get; set; } = "";
    public string? PreparationReference { get; set; }
    public string? ProtectedPayload { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
    public bool Unmatched { get; set; }
}
