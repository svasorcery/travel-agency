using Microsoft.EntityFrameworkCore;

namespace Travel.Modules.Support.Infrastructure.Persistence;

public static class SupportDbContextConfiguration
{
    public static void Configure(DbContextOptionsBuilder options)
    {
        options.UseNpgsql(npgsql =>
            npgsql.MigrationsHistoryTable("__ef_migrations_history", "support")
        );
        options.UseSnakeCaseNamingConvention();
    }
}
