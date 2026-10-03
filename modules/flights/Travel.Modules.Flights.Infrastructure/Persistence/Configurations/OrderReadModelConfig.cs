using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Travel.Modules.Flights.Infrastructure.Persistence.Entities;

namespace Travel.Modules.Flights.Infrastructure.Persistence.Configurations;

public sealed class OrderReadModelConfig : IEntityTypeConfiguration<OrderReadModelEntity>
{
    public void Configure(EntityTypeBuilder<OrderReadModelEntity> b)
    {
        b.ToTable(
            "order_read_model",
            "flights",
            table =>
                table.HasCheckConstraint(
                    "ck_order_read_model_passenger_count",
                    "passenger_count BETWEEN 1 AND 9"
                )
        );
        b.Property(x => x.PassengerCount).HasDefaultValue(1).IsRequired();
        b.HasKey(x => x.Id);
        b.HasIndex(x => x.AggregateId).IsUnique();
        b.Property(x => x.ProjectedStreamVersion)
            .HasColumnType("bigint")
            .HasDefaultValue(-1L)
            .ValueGeneratedNever()
            .IsConcurrencyToken();
        b.HasIndex(x => new { x.UserId, x.BookedAt }).IsDescending(false, true);
        b.Property(x => x.ItineraryJson).HasColumnType("jsonb");
        b.Property(x => x.PassengerInfoJson).HasColumnType("jsonb");
        b.Property(x => x.TicketNumbers).HasColumnType("text[]");
    }
}
