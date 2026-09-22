using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Travel.Modules.Flights.Application.ReadModels;
using Travel.Modules.Flights.Infrastructure.Persistence;
using IDocumentStore = Marten.IDocumentStore;

namespace Travel.Modules.Flights.Api.Composition;

public sealed record BookingMaintenanceRequest(
    string Operation,
    Guid? Id,
    bool Execute,
    bool ExclusiveMaintenance
)
{
    public static BookingMaintenanceRequest? Parse(string[] args)
    {
        if (args.Length == 0)
            return null;
        var operation = args[0];
        if (operation is not ("validate" or "inspect" or "rebuild" or "replay"))
            return null;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        Guid? id = null;
        for (var i = 1; i < args.Length; i++)
        {
            var flag = args[i];
            if (!seen.Add(flag))
                return null;
            if (flag is "--execute" or "--exclusive-maintenance")
                continue;
            if (
                flag
                != (
                    operation == "inspect" ? "--aggregate-id"
                    : operation == "replay" ? "--message-id"
                    : ""
                )
            )
                return null;
            if (
                ++i == args.Length
                || !Guid.TryParse(args[i], out var parsed)
                || parsed == Guid.Empty
            )
                return null;
            id = parsed;
        }
        var execute = seen.Contains("--execute");
        var exclusive = seen.Contains("--exclusive-maintenance");
        return operation switch
        {
            "validate" when seen.Count == 0 => new(operation, null, false, false),
            "inspect" when id.HasValue && !execute && !exclusive => new(
                operation,
                id,
                false,
                false
            ),
            "rebuild" when execute && exclusive => new(operation, null, true, true),
            "replay" when id.HasValue && execute && !exclusive => new(operation, id, true, false),
            _ => null,
        };
    }
}

public static class BookingReadModelMaintenance
{
    public static IServiceCollection AddBookingReadModelMaintenance(
        this IServiceCollection services,
        string connection,
        bool exclusive,
        TextWriter? progress = null
    )
    {
        services.AddBookingMaintenancePersistence(connection, exclusive);
        if (progress is not null)
            services.AddSingleton<IProgress<BookingMaintenanceProgress>>(
                new MaintenanceProgressWriter(progress)
            );
        return services;
    }

    public static async Task ValidateSchemaAsync(IServiceProvider services, CancellationToken ct)
    {
        var db = services.GetRequiredService<FlightsDbContext>();
        if ((await db.Database.GetPendingMigrationsAsync(ct)).Any())
            throw new InvalidOperationException("SchemaPrerequisite");
        await services
            .GetRequiredService<IDocumentStore>()
            .Storage.Database.AssertDatabaseMatchesConfigurationAsync(ct);
        // Also verify the actual projection columns, not only migration history.
        _ = await db.Orders.AsNoTracking().Take(1).ToListAsync(ct);
    }

    public static async Task<int> ExecuteAsync(
        IServiceProvider services,
        BookingMaintenanceRequest request,
        TextWriter output,
        CancellationToken ct
    )
    {
        if (request.Operation is "validate" or "rebuild")
        {
            var report = await services
                .GetRequiredService<IOrderReadModelRebuildRunner>()
                .RunAsync(request.Execute, request.ExclusiveMaintenance, ct);
            await output.WriteLineAsync(JsonSerializer.Serialize(report));
            return report.Failed == 0 && report.FailureCode is null ? 0 : 1;
        }
        var diagnostics = services.GetRequiredService<IBookingConsistencyDiagnostics>();
        if (request.Operation == "inspect")
        {
            var report = await diagnostics.InspectAsync(request.Id!.Value, ct);
            await output.WriteLineAsync(JsonSerializer.Serialize(report));
            return report.Validation.Issues.Count == 0 ? 0 : 1;
        }
        var replay = await diagnostics.ReplayAsync(request.Id!.Value, ct);
        await output.WriteLineAsync(JsonSerializer.Serialize(replay));
        return replay.Replayable ? 0 : 1;
    }

    private sealed class MaintenanceProgressWriter(TextWriter output)
        : IProgress<BookingMaintenanceProgress>
    {
        public void Report(BookingMaintenanceProgress value)
        {
            // Synchronous and ordered: each completed stream is visible before advancing.
            output.WriteLine(JsonSerializer.Serialize(value));
            output.Flush();
        }
    }
}
