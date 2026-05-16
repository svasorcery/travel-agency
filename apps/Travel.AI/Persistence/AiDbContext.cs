using Microsoft.EntityFrameworkCore;
using Travel.AI.Persistence.Entities;

namespace Travel.AI.Persistence;

public sealed class AiDbContext(DbContextOptions<AiDbContext> options) : DbContext(options)
{
    public DbSet<CostLedgerEntry> CostLedger => Set<CostLedgerEntry>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema("ai");
        var e = b.Entity<CostLedgerEntry>();
        e.ToTable("cost_ledger", "ai");
        e.HasKey(x => x.Id);
        e.HasIndex(x => x.OccurredAt);
        e.HasIndex(x => x.CorrelationId);
    }
}
