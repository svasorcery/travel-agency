using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Travel.Modules.Flights.Infrastructure.Persistence.Entities;

namespace Travel.Modules.Flights.Infrastructure.Persistence.Configurations;

public sealed class WebhookInboxConfig : IEntityTypeConfiguration<WebhookInboxEntity>
{
    public void Configure(EntityTypeBuilder<WebhookInboxEntity> b)
    {
        b.ToTable("webhook_inbox", "flights");
        b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.Source, x.EventId }).IsUnique();
        b.Property(x => x.RawPayload).HasColumnType("jsonb");
    }
}
