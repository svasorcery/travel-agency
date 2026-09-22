namespace Travel.Modules.Flights.Application.ReadModels;

public sealed record BookingMaintenanceStreamResult(
    Guid AggregateId,
    bool Materialized,
    IReadOnlyList<ProjectionIssue> Issues
);

public sealed record BookingMaintenanceProgress(
    BookingMaintenanceStreamResult Stream,
    int Succeeded,
    int NonMaterialized,
    int Failed
);

public sealed record BookingMaintenanceReport(
    int Succeeded,
    int NonMaterialized,
    int Failed,
    IReadOnlyList<BookingMaintenanceStreamResult> Streams,
    string? FailureCode = null
);

public interface IOrderReadModelRebuildRunner
{
    Task<BookingMaintenanceReport> RunAsync(
        bool execute,
        bool exclusiveMaintenance,
        CancellationToken ct
    );
}
