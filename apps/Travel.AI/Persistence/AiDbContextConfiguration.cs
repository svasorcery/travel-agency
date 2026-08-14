using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

namespace Travel.AI.Persistence;

public static class AiDbContextConfiguration
{
    public static void ConfigureNpgsql(NpgsqlDbContextOptionsBuilder options) =>
        options.MigrationsHistoryTable("__ef_migrations_history", "ai");
}
