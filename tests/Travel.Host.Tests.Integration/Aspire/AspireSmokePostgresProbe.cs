using System.Net.Sockets;
using System.Security.Authentication;
using Aspire.Hosting;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace Travel.Host.Tests.Integration.Aspire;

internal static class AspireSmokePostgresProbe
{
    internal const string LogPrefix = "smoke-pg-state=%e ";
    private const string PrefixMarker = "smoke-pg-state=";

    // Only allowlisted PostgreSQL status codes leave the fixture. An unknown marker can
    // occur in server-controlled free text and must never be copied to the diagnostic.
    internal static string SafeState(string? state) =>
        state switch
        {
            "00000"
            or "08000"
            or "08001"
            or "08004"
            or "08006"
            or "08P01"
            or "28000"
            or "28P01"
            or "3D000"
            or "42501"
            or "53300"
            or "57P01"
            or "57P02"
            or "57P03"
            or "58P01"
            or "58030"
            or "XX000" => state,
            _ => "other",
        };

    internal static string FatalState(string line)
    {
        var start = line.IndexOf(PrefixMarker, StringComparison.Ordinal);
        if (start < 0)
            return "other";
        var suffix = line.AsSpan(start + PrefixMarker.Length);
        return suffix.Length > 5 && suffix[5] == ' ' ? SafeState(suffix[..5].ToString()) : "other";
    }

    internal static string Failure(Exception? exception)
    {
        if (exception is null)
            return "no-exception";
        return exception switch
        {
            PostgresException pg => "postgres-" + SafeState(pg.SqlState),
            SocketException socket => "socket-" + socket.SocketErrorCode,
            OperationCanceledException => "cancelled",
            TimeoutException => "timeout",
            AuthenticationException => "tls-authentication",
            _ when exception.InnerException is { } inner => Failure(inner),
            ArgumentException => "invalid-argument",
            FormatException => "format",
            NpgsqlException => "npgsql",
            IOException => "io",
            _ => "other",
        };
    }

    // The operation token may already be cancelled when StartAsync fails. Share a small,
    // independent deadline across these read-only probes and retain the original failure.
    internal static async Task<string> OnStartupFailureAsync(DistributedApplication app)
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        var checks = await Task.WhenAll(
            Observe(ProbeDatabaseAsync(app, "postgres", budget.Token), budget.Token),
            Observe(ProbeDatabaseAsync(app, "travel", budget.Token), budget.Token),
            Observe(ProbeHealthAsync(app, budget.Token), budget.Token)
        );
        return $"PostgresProbe[postgres={checks[0]}, travel={checks[1]}, registered={checks[2]}]";
    }

    internal static async Task<string> Observe(Task<string> pending, CancellationToken ct)
    {
        try
        {
            return await pending.WaitAsync(ct);
        }
        catch (Exception e)
        {
            return Failure(e);
        }
    }

    private static async Task<string> ProbeDatabaseAsync(
        DistributedApplication app,
        string resource,
        CancellationToken ct
    )
    {
        try
        {
            var value = await app.GetConnectionStringAsync(resource, ct);
            if (string.IsNullOrWhiteSpace(value))
                return "no-connection-string";
            var settings = new NpgsqlConnectionStringBuilder(value)
            {
                Database = resource,
                Timeout = 2,
                CommandTimeout = 2,
                Pooling = false,
            };
            await using var connection = new NpgsqlConnection(settings.ConnectionString);
            await connection.OpenAsync(ct);
            await using var query = new NpgsqlCommand("SELECT 1", connection);
            return await query.ExecuteScalarAsync(ct) is 1 ? "ok" : "unexpected-result";
        }
        catch (Exception e)
        {
            return Failure(e);
        }
    }

    private static async Task<string> ProbeHealthAsync(
        DistributedApplication app,
        CancellationToken ct
    )
    {
        try
        {
            const string key = "postgres_check";
            var health = app.Services.GetRequiredService<HealthCheckService>();
            var report = await health.CheckHealthAsync(entry => entry.Name == key, ct);
            if (!report.Entries.TryGetValue(key, out var entry))
                return "not-registered";
            return entry.Status == HealthStatus.Healthy
                ? "healthy"
                : entry.Status + "/" + Failure(entry.Exception);
        }
        catch (Exception e)
        {
            return Failure(e);
        }
    }
}
