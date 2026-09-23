namespace Travel.Modules.Flights.Application.ReadModels;

public interface IBookingStreamCatalog
{
    IAsyncEnumerable<Guid> ReadIdsAsync(CancellationToken ct);
}
