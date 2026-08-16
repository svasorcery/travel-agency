using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

namespace Travel.Modules.Flights.Infrastructure.Persistence;

public static class FlightsDbContextConfiguration
{
    public static void ConfigureNpgsql(NpgsqlDbContextOptionsBuilder options) =>
        options.MigrationsHistoryTable("__ef_migrations_history", "flights");
}
