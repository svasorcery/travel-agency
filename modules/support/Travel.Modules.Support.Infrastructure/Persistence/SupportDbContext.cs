using Microsoft.EntityFrameworkCore;

namespace Travel.Modules.Support.Infrastructure.Persistence;

public sealed class SupportDbContext(DbContextOptions<SupportDbContext> options)
    : DbContext(options)
{
    public DbSet<SupportCaseEntity> Cases => Set<SupportCaseEntity>();
    public DbSet<SupportMessageEntity> Messages => Set<SupportMessageEntity>();
    public DbSet<SupportReceiptEntity> Receipts => Set<SupportReceiptEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("support");
        var cases = modelBuilder.Entity<SupportCaseEntity>();
        cases.ToTable(
            "cases",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_cases_revision",
                    "revision > 0 AND title_written_at_revision > 0"
                );
                table.HasCheckConstraint(
                    "ck_cases_order_link",
                    "(order_id IS NULL AND order_stream_version IS NULL AND order_verified_at IS NULL) OR (order_id IS NOT NULL AND order_stream_version IS NOT NULL AND order_stream_version > 0 AND order_verified_at IS NOT NULL)"
                );
                table.HasCheckConstraint(
                    "ck_cases_resolution",
                    "(state = 'Resolved' AND resolved_at IS NOT NULL AND purge_at IS NOT NULL) OR (state <> 'Resolved' AND resolved_at IS NULL AND purge_at IS NULL)"
                );
            }
        );
        cases.HasKey(x => x.CaseId);
        cases.Property(x => x.Revision).IsConcurrencyToken();
        cases.Property(x => x.ProtectedTitle).HasMaxLength(32768).IsRequired();
        cases.Property(x => x.State).HasConversion<string>().HasMaxLength(32);
        cases.Property(x => x.Category).HasConversion<string>().HasMaxLength(32);
        cases.Property(x => x.Priority).HasConversion<string>().HasMaxLength(16);
        cases.Property(x => x.TriageSource).HasConversion<string>().HasMaxLength(16);
        cases.HasIndex(x => new
        {
            x.OwnerId,
            x.CreatedAt,
            x.CaseId,
        });
        cases.HasIndex(x => new { x.CreatedAt, x.CaseId });
        cases.HasIndex(x => new
        {
            x.State,
            x.PurgeAt,
            x.CaseId,
        });
        var messages = modelBuilder.Entity<SupportMessageEntity>();
        messages.ToTable(
            "messages",
            table => table.HasCheckConstraint("ck_messages_revision", "written_at_revision > 0")
        );
        messages.HasKey(x => new { x.CaseId, x.MessageId });
        messages.Property(x => x.ActorKind).HasConversion<string>().HasMaxLength(16);
        messages.Property(x => x.ProtectedBody).HasMaxLength(32768).IsRequired();
        messages
            .HasIndex(x => new
            {
                x.CaseId,
                x.WrittenAtRevision,
                x.MessageId,
            })
            .IsUnique();
        messages
            .HasOne<SupportCaseEntity>()
            .WithMany()
            .HasForeignKey(x => x.CaseId)
            .OnDelete(DeleteBehavior.Cascade);
        var receipts = modelBuilder.Entity<SupportReceiptEntity>();
        receipts.ToTable(
            "action_receipts",
            table =>
                table.HasCheckConstraint(
                    "ck_receipts_revision",
                    "expected_revision >= 0 AND applied_revision > 0"
                )
        );
        receipts.HasKey(x => new { x.CaseId, x.ActionId });
        receipts.Property(x => x.Kind).HasConversion<string>().HasMaxLength(32);
        receipts.Property(x => x.ActorKind).HasConversion<string>().HasMaxLength(16);
        receipts.Property(x => x.AppliedState).HasConversion<string>().HasMaxLength(32);
        receipts.Property(x => x.PreviousCategory).HasConversion<string>().HasMaxLength(32);
        receipts.Property(x => x.RequestedCategory).HasConversion<string>().HasMaxLength(32);
        receipts.Property(x => x.PreviousPriority).HasConversion<string>().HasMaxLength(16);
        receipts.Property(x => x.RequestedPriority).HasConversion<string>().HasMaxLength(16);
        receipts
            .HasOne<SupportCaseEntity>()
            .WithMany()
            .HasForeignKey(x => x.CaseId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
