using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Travel.Modules.Flights.Infrastructure.Persistence.Entities;

namespace Travel.Modules.Flights.Infrastructure.Persistence.Configurations;

public sealed class SavedTravelerConfig : IEntityTypeConfiguration<SavedTravelerEntity>
{
    public void Configure(EntityTypeBuilder<SavedTravelerEntity> b)
    {
        b.ToTable(
            "saved_travelers",
            "flights",
            table =>
                table.HasCheckConstraint(
                    "ck_saved_travelers_identifiers",
                    "id <> '00000000-0000-0000-0000-000000000000'::uuid AND owner_user_id <> '00000000-0000-0000-0000-000000000000'::uuid AND revision <> '00000000-0000-0000-0000-000000000000'::uuid"
                )
        );
        b.HasKey(x => x.Id).HasName("pk_saved_travelers");
        b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(x => x.OwnerUserId).HasColumnName("owner_user_id").IsRequired();
        b.Property(x => x.Revision)
            .HasColumnName("revision")
            .IsRequired()
            .IsConcurrencyToken()
            .ValueGeneratedNever();
        b.Property(x => x.ProtectedDetailsJson)
            .HasColumnName("protected_details_json")
            .HasColumnType("jsonb")
            .IsRequired();
        b.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        b.Property(x => x.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        b.HasIndex(x => new
            {
                x.OwnerUserId,
                x.CreatedAt,
                x.Id,
            })
            .HasDatabaseName("ix_saved_travelers_owner_created_id")
            .IsDescending(false, true, true);
    }
}
