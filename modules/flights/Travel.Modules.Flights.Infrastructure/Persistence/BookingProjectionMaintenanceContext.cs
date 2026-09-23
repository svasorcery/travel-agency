using Travel.Modules.Flights.Application.ReadModels;

namespace Travel.Modules.Flights.Infrastructure.Persistence;

public sealed class BookingProjectionMaintenanceContext : IBookingProjectionMaintenanceContext
{
    private readonly bool _exclusive;

    public BookingProjectionMaintenanceContext() { }

    private BookingProjectionMaintenanceContext(bool exclusive) => _exclusive = exclusive;

    internal static BookingProjectionMaintenanceContext ForExclusiveMaintenance() => new(true);

    public void RequireExclusiveReset()
    {
        if (!_exclusive)
            throw new BookingProjectionTerminalException("ExclusiveMaintenanceRequired");
    }
}
