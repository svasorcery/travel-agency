using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Travel.Modules.Support.Infrastructure.Persistence;

public sealed class SupportDbContextFactory : IDesignTimeDbContextFactory<SupportDbContext>
{
    public SupportDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<SupportDbContext>().UseNpgsql(
            "Host=127.0.0.1;Database=support_source_generation_only;Username=source_generator"
        );
        SupportDbContextConfiguration.Configure(options);
        return new SupportDbContext(options.Options);
    }
}
