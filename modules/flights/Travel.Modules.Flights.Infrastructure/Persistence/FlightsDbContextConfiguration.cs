using Microsoft.EntityFrameworkCore;

namespace Travel.Modules.Flights.Infrastructure.Persistence;

internal static class FlightsDbContextConfiguration
{
    internal static void Configure(DbContextOptionsBuilder options)
    {
        options.UseNpgsql(npgsql =>
            npgsql.MigrationsHistoryTable("__ef_migrations_history", "flights")
        );
        options.UseSnakeCaseNamingConvention();
    }
}
