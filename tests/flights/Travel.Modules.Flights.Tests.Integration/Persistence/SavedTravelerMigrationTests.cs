using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Shouldly;
using Travel.Modules.Flights.Infrastructure.Persistence.Migrations;

namespace Travel.Modules.Flights.Tests.Integration.Persistence;

public sealed class SavedTravelerMigrationTests
{
    [Fact]
    public void Source_migration_creates_only_ciphertext_profile_table_and_owner_page_index()
    {
        var migration = new AddSavedTravelers();
        migration.UpOperations.Count.ShouldBe(2);
        var table = migration.UpOperations[0].ShouldBeOfType<CreateTableOperation>();
        table.Schema.ShouldBe("flights");
        table.Name.ShouldBe("saved_travelers");
        table.PrimaryKey!.Name.ShouldBe("pk_saved_travelers");
        table.PrimaryKey.Columns.ShouldBe(["id"]);
        table.ForeignKeys.ShouldBeEmpty();
        table
            .Columns.Select(c => c.Name)
            .ShouldBe([
                "id",
                "owner_user_id",
                "revision",
                "protected_details_json",
                "created_at",
                "updated_at",
            ]);
        table
            .Columns.All(c => !c.IsNullable && c.DefaultValue is null && c.DefaultValueSql is null)
            .ShouldBeTrue();
        table.Columns.Single(c => c.Name == "protected_details_json").ColumnType.ShouldBe("jsonb");
        table
            .CheckConstraints.ShouldHaveSingleItem()
            .Name.ShouldBe("ck_saved_travelers_identifiers");
        var index = migration.UpOperations[1].ShouldBeOfType<CreateIndexOperation>();
        index.Schema.ShouldBe(table.Schema);
        index.Table.ShouldBe(table.Name);
        index.Name.ShouldBe("ix_saved_travelers_owner_created_id");
        index.Columns.ShouldBe(["owner_user_id", "created_at", "id"]);
        index.IsDescending.ShouldBe([false, true, true]);
    }

    [Fact]
    public void Down_removes_only_profiles_and_preserves_booking_tables()
    {
        var operation = new AddSavedTravelers()
            .DownOperations.ShouldHaveSingleItem()
            .ShouldBeOfType<DropTableOperation>();
        operation.Schema.ShouldBe("flights");
        operation.Name.ShouldBe("saved_travelers");
    }

    [Fact]
    public void Offline_SQL_is_schema_qualified_and_does_not_rewrite_booking_data()
    {
        using var db = FlightsDbContextOptionsParityTests.CreateDesignTimeContext();
        var migrator = db.GetService<IMigrator>();
        var up = migrator.GenerateScript(
            "20261003094726_AddOrderPassengerCount",
            "20261003115745_AddSavedTravelers",
            MigrationsSqlGenerationOptions.Idempotent
        );
        up.ShouldContain("CREATE TABLE flights.saved_travelers");
        up.ShouldContain("flights.__ef_migrations_history");
        up.ShouldContain("created_at DESC, id DESC");
        up.ShouldNotContain("order_read_model");
        up.ShouldNotContain("mt_events");
        up.ShouldNotContain("webhook_inbox");
        var down = migrator.GenerateScript(
            "20261003115745_AddSavedTravelers",
            "20261003094726_AddOrderPassengerCount"
        );
        down.ShouldContain("DROP TABLE flights.saved_travelers");
        down.ShouldNotContain("order_read_model");
    }
}
