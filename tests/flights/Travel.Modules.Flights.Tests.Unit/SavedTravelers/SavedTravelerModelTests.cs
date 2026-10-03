using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Shouldly;
using Travel.Modules.Flights.Infrastructure.Persistence;
using Travel.Modules.Flights.Infrastructure.Persistence.Entities;

namespace Travel.Modules.Flights.Tests.Unit.SavedTravelers;

public sealed class SavedTravelerModelTests
{
    [Fact]
    public void Profile_model_contains_only_ciphertext_and_explicit_concurrency_metadata()
    {
        // Materialize metadata only. No connection/open/schema operation.
        using var db = new FlightsDbContext(
            new DbContextOptionsBuilder<FlightsDbContext>()
                .UseNpgsql(
                    "Host=fictional.invalid;Database=metadata_only;Username=fictional;Password=fictional"
                )
                .UseSnakeCaseNamingConvention()
                .Options
        );
        var entity = db.GetService<IDesignTimeModel>()
            .Model.FindEntityType(typeof(SavedTravelerEntity))!;
        entity.GetTableName().ShouldBe("saved_travelers");
        entity.GetSchema().ShouldBe("flights");
        entity.FindPrimaryKey()!.GetName().ShouldBe("pk_saved_travelers");
        entity
            .GetProperties()
            .Select(x => x.Name)
            .OrderBy(x => x)
            .ShouldBe(
                new[]
                {
                    "CreatedAt",
                    "Id",
                    "OwnerUserId",
                    "ProtectedDetailsJson",
                    "Revision",
                    "UpdatedAt",
                }
            );
        entity.FindProperty("Revision")!.IsConcurrencyToken.ShouldBeTrue();
        entity.FindProperty("Id")!.ValueGenerated.ShouldBe(ValueGenerated.Never);
        entity.FindProperty("Revision")!.ValueGenerated.ShouldBe(ValueGenerated.Never);
        foreach (var property in entity.GetProperties())
        {
            property.IsNullable.ShouldBeFalse();
            property.GetDefaultValueSql().ShouldBeNull();
        }
        entity.FindProperty("ProtectedDetailsJson")!.GetColumnType().ShouldBe("jsonb");
        var index = entity.GetIndexes().Single();
        index.GetDatabaseName().ShouldBe("ix_saved_travelers_owner_created_id");
        index.Properties.Select(x => x.Name).ShouldBe(["OwnerUserId", "CreatedAt", "Id"]);
        index.IsDescending.ShouldBe([false, true, true]);
        var check = entity.GetCheckConstraints().Single();
        check.Name.ShouldBe("ck_saved_travelers_identifiers");
        check.Sql.ShouldContain("id <>");
        check.Sql.ShouldContain("owner_user_id <>");
        check.Sql.ShouldContain("revision <>");
        entity.GetForeignKeys().ShouldBeEmpty();
    }
}
