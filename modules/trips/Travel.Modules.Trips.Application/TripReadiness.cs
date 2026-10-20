namespace Travel.Modules.Trips.Application;

public interface ITripsReadiness
{
    Task<bool> IsReadyAsync(CancellationToken cancellationToken);
}
