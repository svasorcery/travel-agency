using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Shouldly;
using Travel.Modules.Flights.Infrastructure.Persistence;
using Travel.Modules.Flights.Infrastructure.Persistence.Entities;
using Travel.Modules.Flights.Infrastructure.Persistence.Migrations;

namespace Travel.Modules.Flights.Tests.Integration.Persistence;

public sealed class OrderReadModelMigrationTests
{
    [Fact]
    public void Migration_adds_only_checkpoint_and_down_removes_only_checkpoint()
    {
        var migration = new AddOrderReadModelProjectedStreamVersion();
        var added = migration
            .UpOperations.ShouldHaveSingleItem()
            .ShouldBeOfType<AddColumnOperation>();
        added.Schema.ShouldBe("flights");
        added.Table.ShouldBe("order_read_model");
        added.Name.ShouldBe("projected_stream_version");
        added.ColumnType.ShouldBe("bigint");
        added.IsNullable.ShouldBeFalse();
        added.DefaultValue.ShouldBe(-1L);
        var removed = migration
            .DownOperations.ShouldHaveSingleItem()
            .ShouldBeOfType<DropColumnOperation>();
        removed.Schema.ShouldBe(added.Schema);
        removed.Table.ShouldBe(added.Table);
        removed.Name.ShouldBe(added.Name);
    }

    [Fact]
    public void Passenger_count_migration_changes_only_derived_count_and_its_constraint()
    {
        var migration = new AddOrderPassengerCount();
        migration.UpOperations.Count.ShouldBe(2);
        var added = migration.UpOperations[0].ShouldBeOfType<AddColumnOperation>();
        added.Schema.ShouldBe("flights");
        added.Table.ShouldBe("order_read_model");
        added.Name.ShouldBe("passenger_count");
        added.ColumnType.ShouldBe("integer");
        added.ClrType.ShouldBe(typeof(int));
        added.IsNullable.ShouldBeFalse();
        added.DefaultValue.ShouldBe(1);
        var constraint = migration.UpOperations[1].ShouldBeOfType<AddCheckConstraintOperation>();
        constraint.Schema.ShouldBe(added.Schema);
        constraint.Table.ShouldBe(added.Table);
        constraint.Name.ShouldBe("ck_order_read_model_passenger_count");
        constraint.Sql.ShouldBe("passenger_count BETWEEN 1 AND 9");
        migration.DownOperations.Count.ShouldBe(2);
        var removedConstraint = migration
            .DownOperations[0]
            .ShouldBeOfType<DropCheckConstraintOperation>();
        removedConstraint.Name.ShouldBe(constraint.Name);
        var removed = migration.DownOperations[1].ShouldBeOfType<DropColumnOperation>();
        removed.Schema.ShouldBe(added.Schema);
        removed.Table.ShouldBe(added.Table);
        removed.Name.ShouldBe(added.Name);
    }

    [Fact]
    public void Checkpoint_is_explicit_bigint_concurrency_token_with_untrusted_default()
    {
        using var db = new FlightsDbContext(
            new DbContextOptionsBuilder<FlightsDbContext>()
                .UseNpgsql("Host=localhost;Database=unused")
                .UseSnakeCaseNamingConvention()
                .Options
        );
        var entity = db.GetService<IDesignTimeModel>()
            .Model.FindEntityType(typeof(OrderReadModelEntity))!;
        var property = entity.FindProperty("ProjectedStreamVersion");
        property.ShouldNotBeNull();
        property.ClrType.ShouldBe(typeof(long));
        property.GetColumnType().ShouldBe("bigint");
        property.GetDefaultValue().ShouldBe(-1L);
        property.ValueGenerated.ShouldBe(ValueGenerated.Never);
        property.IsConcurrencyToken.ShouldBeTrue();
        entity
            .GetIndexes()
            .ShouldContain(index =>
                index.IsUnique
                && index.Properties.Count == 1
                && index.Properties[0].Name == nameof(OrderReadModelEntity.AggregateId)
            );
    }
}
