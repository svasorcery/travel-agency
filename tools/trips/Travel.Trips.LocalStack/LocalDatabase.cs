using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Travel.Modules.Trips.Infrastructure.Persistence;

namespace Travel.Trips.LocalStack;

public sealed record TripDatabaseSnapshot(
    Guid TripId,
    string OwnerIssuer,
    Guid OwnerSubject,
    long Revision,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int PayloadLength,
    string PayloadSha256,
    IReadOnlyDictionary<string, string> KeyFileHashes
);

public static class LocalDatabase
{
    public static void ValidateConnection(string connection, string password)
    {
        NpgsqlConnectionStringBuilder settings;
        try
        {
            settings = new(connection);
        }
        catch (ArgumentException)
        {
            throw new LocalStackRefusal("Invalid owned database configuration.");
        }
        var allowed = new HashSet<string>(
            ["Host", "Port", "Database", "Username", "Password"],
            StringComparer.OrdinalIgnoreCase
        );
        if (
            settings.Host != "postgres"
            || settings.Port != 5432
            || settings.Database != "travel"
            || settings.Username != "postgres"
            || settings.Password != password
            || settings.Keys.Cast<string>().Any(key => !allowed.Contains(key))
        )
            throw new LocalStackRefusal("Database configuration is outside the owned profile.");
    }

    public static void ValidateOwner(IReadOnlyList<string> owners, string id)
    {
        if (owners.Count != 1 || owners[0] != id)
            throw new LocalStackRefusal("Database ownership marker does not match this stack.");
    }

    private static async Task<NpgsqlConnection> OpenOwnedAsync(
        LocalStackState state,
        CancellationToken ct
    )
    {
        var password = LocalProvisioning.ReadSecrets(state).DatabasePassword;
        var settings = new NpgsqlConnectionStringBuilder
        {
            Host = "postgres",
            Port = 5432,
            Database = "travel",
            Username = "postgres",
            Password = password,
        };
        var configured = Environment.GetEnvironmentVariable("ConnectionStrings__travel");
        if (configured is not null)
            ValidateConnection(configured, password);
        var connection = new NpgsqlConnection(settings.ConnectionString);
        try
        {
            await connection.OpenAsync(ct);
            await using var command = new NpgsqlCommand(
                "select id from public.trips_demo_owner limit 2",
                connection
            );
            await using var reader = await command.ExecuteReaderAsync(ct);
            var owners = new List<string>();
            while (await reader.ReadAsync(ct))
                owners.Add(reader.GetString(0));
            ValidateOwner(owners, state.Id);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    public static async Task ApplySchemaAsync(LocalStackState state, CancellationToken ct)
    {
        await using var connection = await OpenOwnedAsync(state, ct);
        await using var context = Context(connection);
        await context.Database.MigrateAsync(ct);
    }

    public static async Task<TripDatabaseSnapshot> CaptureAsync(
        LocalStackState state,
        Guid tripId,
        CancellationToken ct
    )
    {
        if (tripId == Guid.Empty)
            throw new LocalStackRefusal("A nonempty trip id is required.");
        await using var connection = await OpenOwnedAsync(state, ct);
        await using var context = Context(connection);
        var row =
            await context.Plans.AsNoTracking().SingleOrDefaultAsync(x => x.TripId == tripId, ct)
            ?? throw new LocalStackRefusal("The owned trip row does not exist.");
        return Snapshot(row, KeyHashes(state));
    }

    private static TripsDbContext Context(NpgsqlConnection connection)
    {
        var options = new DbContextOptionsBuilder<TripsDbContext>().UseNpgsql(connection);
        TripsDbContextConfiguration.Configure(options);
        return new(options.Options);
    }

    public static TripDatabaseSnapshot Snapshot(
        TripPlanEntity entity,
        IReadOnlyDictionary<string, string> keys
    )
    {
        var payload = Encoding.UTF8.GetBytes(entity.ProtectedPayload);
        return new(
            entity.TripId,
            entity.OwnerIssuer,
            entity.OwnerSubject,
            entity.Revision,
            entity.CreatedAt,
            entity.UpdatedAt,
            payload.Length,
            Convert.ToHexStringLower(SHA256.HashData(payload)),
            keys
        );
    }

    private static SortedDictionary<string, string> KeyHashes(LocalStackState state)
    {
        var root = Path.Combine(state.StateDir, "private", "keys");
        var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var module in new[] { "trips", "flights", "support" })
        {
            var ring = Path.Combine(root, module, "ring");
            LocalStackState.RequireNoLinks(ring);
            var files = Directory.GetFiles(ring, "*.xml");
            if (files.Length is < 1 or > 32)
                throw new LocalStackRefusal("Unexpected owned key ring size.");
            foreach (var file in files.Append(Path.Combine(root, module, "certificate.pfx")))
            {
                LocalStackState.RequireNoLinks(file);
                if (new FileInfo(file).Length is <= 0 or > 1048576)
                    throw new LocalStackRefusal("Unexpected owned key file size.");
                result.Add(
                    Path.GetRelativePath(root, file).Replace('\\', '/'),
                    Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file)))
                );
            }
        }
        return result;
    }
}
