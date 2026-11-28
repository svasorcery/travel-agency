using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Testcontainers.PostgreSql;
using Travel.Modules.Trips.Infrastructure.Persistence;
using Travel.Modules.Trips.Tests.Integration;

namespace Travel.Trips.SchemaMatrix;

internal static class ToolProgram
{
    internal const string Previous = "20261124121853_AddTripHotelLinks";
    private const string Current = "20261127164744_AddSavedOptions";
    private static string stage = "arguments";

    public static async Task<int> Main(string[] args)
    {
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(8));
            var ct = deadline.Token;
            Environment.SetEnvironmentVariable(
                "ASPNETCORE_TEST_CONTENTROOT_TRAVEL_HOST",
                Path.GetFullPath("apps/Travel.Host")
            );
            if (args is ["legacy"])
                await LegacyHostSession.Serve(ct);
            else if (args is ["verify", var previousRoot, var changeDirectory])
                await Verify(previousRoot, changeDirectory, ct);
            else
                throw new InvalidOperationException(
                    "Expected verify BASE_CHECKOUT CHANGE_DIRECTORY."
                );
            return 0;
        }
        catch (Exception ex)
        {
            // Neither the HTTP body, connection, selection nor protection settings enter receipts.
            Console.Error.WriteLine($"Schema matrix failed at {stage} ({ex.GetType().Name}).");
            if (
                ex is InvalidOperationException
                && ex.Message.StartsWith("Expected ", StringComparison.Ordinal)
            )
                Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    internal static TripsDbContext Db(FixtureConfig config)
    {
        var options = new DbContextOptionsBuilder<TripsDbContext>().UseNpgsql(config.Connection);
        TripsDbContextConfiguration.Configure(options);
        return new(options.Options);
    }

    private static async Task Verify(
        string previousRoot,
        string changeDirectory,
        CancellationToken ct
    )
    {
        stage = "owned PostgreSQL startup";
        await using var pg = new PostgreSqlBuilder("pgvector/pgvector:pg17")
            .WithDatabase("trips_schema_matrix")
            .Build();
        await pg.StartAsync(ct);
        using var ring = new TripsTestRing();
        var config = new FixtureConfig(pg.GetConnectionString(), ring.Settings);
        await using var db = Db(config);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(Previous, ct);
        var originalHistory = (await db.Database.GetAppliedMigrationsAsync(ct)).ToArray();
        var up = await Script(
            changeDirectory,
            "up",
            migrator.GenerateScript(Previous, Current),
            ct
        );
        var down = await Script(
            changeDirectory,
            "down",
            migrator.GenerateScript(Current, Previous),
            ct
        );
        stage = "previous Host and schema";
        await using var legacy = await LegacyHostSession.Start(previousRoot, config, ct);
        var original = await legacy.Create(ct);
        await legacy.Open(original, ct);
        stage = "new Host missing migration, saving off";
        await using (var missing = new MatrixHost(config))
        using (var client = missing.CreateClient())
        using (var response = await client.GetAsync("/api/trips", ct))
            MatrixHost.Require(response, HttpStatusCode.ServiceUnavailable);
        Check(
            originalHistory.SequenceEqual(await db.Database.GetAppliedMigrationsAsync(ct)),
            "Unready Host changed migration history."
        );
        Check(
            await db
                .Database.SqlQuery<bool>(
                    $"SELECT to_regclass('trips.saved_option_receipts') IS NULL AND NOT EXISTS (SELECT FROM information_schema.columns WHERE table_schema='trips' AND table_name='trip_plans' AND column_name='protected_option') AS \"Value\""
                )
                .SingleAsync(ct),
            "Unready Host changed schema."
        );

        stage = "expanded schema, live previous Host";
        await db.Database.ExecuteSqlRawAsync(up, ct);
        await legacy.Open(original, ct);
        var second = await legacy.Create(ct);
        await legacy.Open(second, ct);
        Check(await CountManual(db, ct) == 2, "Expanded schema manual count.");

        stage = "expanded schema, new Host saving off";
        Guid third;
        await using (var current = new MatrixHost(config))
        using (var client = current.CreateClient())
        {
            await MatrixHost.OpenManual(client, original, ct);
            await MatrixHost.OpenManual(client, second, ct);
            third = await MatrixHost.CreateManual(client, ct);
            await MatrixHost.OpenManual(client, third, ct);
            using var closed = await client.PostAsJsonAsync(
                "/api/trips/saved-options",
                new { },
                ct
            );
            MatrixHost.Require(closed, HttpStatusCode.NotFound);
        }
        Check(await CountManual(db, ct) == 3, "New Host manual count.");

        stage = "enabled first save and historical read";
        Guid saved;
        await using (var enabled = new MatrixHost(config, true))
        using (var client = enabled.CreateClient())
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/trips/option-preview")
            {
                Content = JsonContent.Create(new { brief = MatrixHost.Brief }),
            };
            request.Headers.Add("X-Trips-Option-Save", "1");
            stage = "enabled option preview";
            using var response = await client.SendAsync(request, ct);
            MatrixHost.Require(response, HttpStatusCode.OK);
            using var preview = await MatrixHost.Read(response, ct);
            var selection = preview.RootElement.GetProperty("selection");
            saved = selection.GetProperty("tripId").GetGuid();
            stage = "enabled first option POST";
            using var created = await client.PostAsJsonAsync(
                "/api/trips/saved-options",
                new { selectionToken = selection.GetProperty("selectionToken").GetString() },
                ct
            );
            MatrixHost.Require(created, HttpStatusCode.Created);
            stage = "committed historical GET";
            using var read = await client.GetAsync($"/api/trips/{saved}/saved-option", ct);
            MatrixHost.Require(read, HttpStatusCode.OK);
            using var history = await MatrixHost.Read(read, ct);
            Check(
                history.RootElement.GetProperty("savedOption").ValueKind
                    == System.Text.Json.JsonValueKind.Object,
                "Historical snapshot missing."
            );
        }
        // All writing requests have completed, and the enabled Host has been stopped.
        stage = "live snapshot guard";
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM trips.saved_option_receipts WHERE trip_id={saved}",
            ct
        );
        await RefusedDown(db, down, PostgresErrorCodes.RaiseException, ct);
        stage = "live receipt guard";
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO trips.saved_option_receipts (trip_id,owner_issuer,owner_subject,expires_at) SELECT trip_id,owner_issuer,owner_subject,clock_timestamp()+interval '15 minutes' FROM trips.trip_plans WHERE trip_id={saved}",
            ct
        );
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM trips.trip_plans WHERE trip_id={saved}",
            ct
        );
        await RefusedDown(db, down, PostgresErrorCodes.RaiseException, ct);
        stage = "unknown state guard";
        await db.Database.ExecuteSqlRawAsync(
            "ALTER TABLE trips.saved_option_receipts RENAME TO fixture_unreadable",
            ct
        );
        await RefusedDown(db, down, PostgresErrorCodes.UndefinedTable, ct);
        await db.Database.ExecuteSqlRawAsync(
            "ALTER TABLE trips.fixture_unreadable RENAME TO saved_option_receipts",
            ct
        );
        // Only a disposable fixture discards this deliberately injected receipt.
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM trips.saved_option_receipts WHERE trip_id={saved}",
            ct
        );
        stage = "verified zero, rollback and previous Host";
        Check(await CountManual(db, ct) == 3, "Manual rows changed during guard tests.");
        await db.Database.ExecuteSqlRawAsync(down, ct);
        Check(
            originalHistory.SequenceEqual(await db.Database.GetAppliedMigrationsAsync(ct)),
            "Rollback history mismatch."
        );
        await legacy.Open(original, ct);
        await legacy.Open(third, ct);
        var fourth = await legacy.Create(ct);
        await legacy.Open(fourth, ct);
        Console.WriteLine(
            "PASS old/old create+read; new/old 503 no migration; old/expanded read+create; new/off read+create; new/on 201+historical GET; snapshot/receipt/unknown rollback refused; zero rollback old GET+POST."
        );
    }

    private static async Task RefusedDown(
        TripsDbContext db,
        string down,
        string expectedState,
        CancellationToken ct
    )
    {
        try
        {
            await db.Database.ExecuteSqlRawAsync(down, ct);
        }
        catch (PostgresException error) when (error.SqlState == expectedState)
        {
            Check(
                (await db.Database.GetAppliedMigrationsAsync(ct)).Contains(Current),
                "Refused rollback changed history."
            );
            return;
        }
        throw new InvalidOperationException("Expected rollback refusal.");
    }

    private static async Task<string> Script(
        string changeDirectory,
        string direction,
        string generated,
        CancellationToken ct
    )
    {
        var checkedIn = await File.ReadAllTextAsync(
            Path.Combine(changeDirectory, $"saved-options-{direction}.sql"),
            ct
        );
        Check(
            checkedIn.ReplaceLineEndings().Trim() == generated.ReplaceLineEndings().Trim(),
            "Stored SQL differs from the migration."
        );
        return checkedIn;
    }

    private static Task<int> CountManual(TripsDbContext db, CancellationToken ct) =>
        db
            .Database.SqlQuery<int>(
                $"SELECT count(*)::int AS \"Value\" FROM trips.trip_plans WHERE protected_option IS NULL AND revision = 1"
            )
            .SingleAsync(ct);

    private static void Check(bool condition, string problem)
    {
        if (!condition)
            throw new InvalidOperationException("Expected matrix invariant. " + problem);
    }
}
