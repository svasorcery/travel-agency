using Microsoft.EntityFrameworkCore;

namespace Travel.Host.Persistence;

public sealed class HostDbContext(DbContextOptions<HostDbContext> options) : DbContext(options)
{
    public async Task<string> GetServerVersionAsync(CancellationToken ct)
    {
        // Use raw SQL since we don't have any model-mapped entities yet.
        // Note: GetDbConnection() returns the EF-managed connection — do NOT dispose it.
        var conn = Database.GetDbConnection();
        await conn.OpenAsync(ct);
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT current_setting('server_version')";
            var result = await cmd.ExecuteScalarAsync(ct);
            return result?.ToString() ?? "unknown";
        }
        finally
        {
            await conn.CloseAsync();
        }
    }
}
