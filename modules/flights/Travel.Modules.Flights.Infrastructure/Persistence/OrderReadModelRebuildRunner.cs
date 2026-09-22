using Travel.Modules.Flights.Application.ReadModels;

namespace Travel.Modules.Flights.Infrastructure.Persistence;

public sealed class OrderReadModelRebuildRunner(
    IBookingStreamCatalog catalog,
    IOrderReadModelReconciler reconciler,
    IProgress<BookingMaintenanceProgress>? progress = null
) : IOrderReadModelRebuildRunner
{
    public async Task<BookingMaintenanceReport> RunAsync(
        bool execute,
        bool exclusiveMaintenance,
        CancellationToken ct
    )
    {
        if (execute && !exclusiveMaintenance)
            throw new BookingProjectionTerminalException("ExclusiveMaintenanceRequired");
        var streams = new List<BookingMaintenanceStreamResult>();
        var succeeded = 0;
        var nonMaterialized = 0;
        var failed = 0;
        string? failureCode = null;
        try
        {
            await foreach (var id in catalog.ReadIdsAsync(ct))
            {
                BookingMaintenanceStreamResult result;
                try
                {
                    if (execute)
                    {
                        var reset = await reconciler.ReconcileAsync(
                            id,
                            OrderReadModelReconcileMode.Reset,
                            ct
                        );
                        result = new(id, reset.Materialized, []);
                    }
                    else
                    {
                        var validation = await reconciler.ValidateAsync(id, ct);
                        result = new(id, validation.WouldMaterialize, validation.Issues);
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (BookingProjectionTerminalException error)
                {
                    result = new(id, false, [new(error.Message, false)]);
                }
                catch (Exception)
                {
                    // Do not expose provider payloads, SQL, credentials or passenger data.
                    result = new(id, false, [new("ProjectionStorageFailure", false)]);
                }
                streams.Add(result);
                if (result.Issues.Count != 0)
                    failed++;
                else if (result.Materialized)
                    succeeded++;
                else
                    nonMaterialized++;
                progress?.Report(new(result, succeeded, nonMaterialized, failed));
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            failureCode = "CatalogReadFailed";
        }
        return new(succeeded, nonMaterialized, failed, streams, failureCode);
    }
}
