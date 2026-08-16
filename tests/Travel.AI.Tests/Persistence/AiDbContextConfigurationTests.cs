using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Shouldly;
using Travel.AI.Persistence;
using Travel.AI.Persistence.Entities;
using Xunit;

namespace Travel.AI.Tests.Persistence;

public sealed class AiDbContextConfigurationTests
{
    private const string ConnectionString =
        "Host=localhost;Database=travel;Username=postgres;Password=postgres";

    [Fact]
    public void Runtime_and_design_time_options_use_the_same_provider_metadata()
    {
        using var runtimeContext = new AiDbContext(
            new DbContextOptionsBuilder<AiDbContext>()
                .UseNpgsql(ConnectionString, AiDbContextConfiguration.ConfigureNpgsql)
                .UseSnakeCaseNamingConvention()
                .Options
        );
        using var designTimeContext = CreateDesignTimeContext();

        var runtimeMetadata = CaptureProviderMetadata(runtimeContext);
        var designTimeMetadata = CaptureProviderMetadata(designTimeContext);

        AssertRequiredMetadata(runtimeMetadata);
        AssertRequiredMetadata(designTimeMetadata);
        runtimeMetadata.ShouldBe(designTimeMetadata);
    }

    private static AiDbContext CreateDesignTimeContext()
    {
        var factoryType = typeof(AiDbContext).Assembly.GetType(
            "Travel.AI.Persistence.AiDbContextFactory",
            throwOnError: true
        )!;
        var factory = Activator.CreateInstance(factoryType, nonPublic: true)!;
        var createDbContext = factoryType.GetMethod("CreateDbContext")!;

        return (AiDbContext)createDbContext.Invoke(factory, [Array.Empty<string>()])!;
    }

    private static ProviderMetadata CaptureProviderMetadata(AiDbContext context)
    {
        var table = StoreObjectIdentifier.Table("cost_ledger", "ai");
        var messageIdentityColumn = context
            .Model.FindEntityType(typeof(CostLedgerEntry))!
            .FindProperty(nameof(CostLedgerEntry.MessageIdentity))!
            .GetColumnName(table)!;

        return new ProviderMetadata(
            context.Database.ProviderName!,
            context.Model.GetDefaultSchema()!,
            messageIdentityColumn,
            context.GetService<IMigrationsAssembly>().Assembly.GetName().Name!,
            context.GetService<IHistoryRepository>().GetCreateIfNotExistsScript()
        );
    }

    private static void AssertRequiredMetadata(ProviderMetadata metadata)
    {
        metadata.ProviderName.ShouldBe("Npgsql.EntityFrameworkCore.PostgreSQL");
        metadata.DefaultSchema.ShouldBe("ai");
        metadata.MessageIdentityColumn.ShouldBe("message_identity");
        metadata.MigrationsAssembly.ShouldBe("Travel.AI");
        metadata.MigrationsHistoryCreateScript.ShouldContain(
            "CREATE TABLE IF NOT EXISTS ai.__ef_migrations_history"
        );
    }

    private sealed record ProviderMetadata(
        string ProviderName,
        string DefaultSchema,
        string MessageIdentityColumn,
        string MigrationsAssembly,
        string MigrationsHistoryCreateScript
    );
}
