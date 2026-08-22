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
        var options = new DbContextOptionsBuilder<FlightsDbContext>().UseNpgsql(
            "Host=localhost;Database=travel;Username=postgres;Password=postgres"
        );
        FlightsDbContextConfiguration.Configure(options);

        return new FlightsDbContext(options.Options);
    }
}
