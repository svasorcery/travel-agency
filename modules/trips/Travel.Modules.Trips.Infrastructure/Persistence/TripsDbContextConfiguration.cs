using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Travel.Modules.Trips.Infrastructure.Persistence;

public static class TripsDbContextConfiguration
{
    public static void Configure(DbContextOptionsBuilder options)
    {
        options.UseNpgsql(npgsql =>
            npgsql.MigrationsHistoryTable("__ef_migrations_history", "trips")
        );
        options.UseSnakeCaseNamingConvention();
    }
}

public sealed class TripsDbContextFactory : IDesignTimeDbContextFactory<TripsDbContext>
{
    public TripsDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<TripsDbContext>().UseNpgsql(
            "Host=127.0.0.1;Database=trips_source_generation_only;Username=source_generator"
        );
        TripsDbContextConfiguration.Configure(options);
        return new TripsDbContext(options.Options);
    }
}
