using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Travel.Modules.Hotels.Infrastructure.Persistence;

public static class HotelsDbContextConfiguration
{
    public static void Configure(DbContextOptionsBuilder options)
    {
        options.UseNpgsql(n => n.MigrationsHistoryTable("__ef_migrations_history", "hotels"));
        options.UseSnakeCaseNamingConvention();
    }
}

public sealed class HotelsDbContextFactory : IDesignTimeDbContextFactory<HotelsDbContext>
{
    public HotelsDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<HotelsDbContext>().UseNpgsql(
            "Host=127.0.0.1;Database=hotels_source_generation_only;Username=source_generator"
        );
        HotelsDbContextConfiguration.Configure(builder);
        return new(builder.Options);
    }
}
