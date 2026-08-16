using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Travel.AI.Persistence;

/// <summary>
/// Design-time factory used by <c>dotnet ef migrations</c>.
/// Not used at runtime — the Aspire host registers AiDbContext via AddNpgsqlDbContext.
/// </summary>
internal sealed class AiDbContextFactory : IDesignTimeDbContextFactory<AiDbContext>
{
    public AiDbContext CreateDbContext(string[] args)
    {
        var opts = new DbContextOptionsBuilder<AiDbContext>()
            .UseNpgsql(
                "Host=localhost;Database=travel;Username=postgres;Password=postgres",
                AiDbContextConfiguration.ConfigureNpgsql
            )
            .UseSnakeCaseNamingConvention()
            .Options;

        return new AiDbContext(opts);
    }
}
