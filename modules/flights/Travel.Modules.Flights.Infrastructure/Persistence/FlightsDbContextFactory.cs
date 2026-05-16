using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Travel.Modules.Flights.Infrastructure.Persistence;

/// <summary>
/// Design-time factory used by <c>dotnet ef migrations</c>.
/// Not used at runtime — the Aspire host registers FlightsDbContext via AddNpgsqlDbContext.
/// </summary>
internal sealed class FlightsDbContextFactory : IDesignTimeDbContextFactory<FlightsDbContext>
{
    public FlightsDbContext CreateDbContext(string[] args)
    {
        var opts = new DbContextOptionsBuilder<FlightsDbContext>()
            .UseNpgsql(
                "Host=localhost;Database=travel;Username=postgres;Password=postgres",
                b => b.MigrationsHistoryTable("__ef_migrations_history", "flights")
            )
            .UseSnakeCaseNamingConvention()
            .Options;

        return new FlightsDbContext(opts);
    }
}
