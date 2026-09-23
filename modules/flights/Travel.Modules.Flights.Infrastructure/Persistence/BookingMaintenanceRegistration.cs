using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Travel.Modules.Flights.Application.Observability;
using Travel.Modules.Flights.Application.ReadModels;
using Travel.Modules.Flights.Infrastructure.Diagnostics;
using Travel.Modules.Flights.Infrastructure.Observability;

namespace Travel.Modules.Flights.Infrastructure.Persistence;

internal static class BookingMaintenanceRegistration
{
    internal static IServiceCollection AddBookingMaintenancePersistence(
        this IServiceCollection services,
        string connection,
        bool exclusive
    )
    {
        services.AddMetrics();
        services.AddSingleton<FlightsMetrics>();
        services.AddSingleton<IBookingProjectionMetrics>(sp =>
            sp.GetRequiredService<FlightsMetrics>()
        );
        services.AddDbContext<FlightsDbContext>(options =>
        {
            options.UseNpgsql(connection);
            FlightsDbContextConfiguration.Configure(options);
        });
        services.AddSingleton<IBookingProjectionMaintenanceContext>(
            exclusive
                ? BookingProjectionMaintenanceContext.ForExclusiveMaintenance()
                : new BookingProjectionMaintenanceContext()
        );
        services.AddScoped<IOrderReadModelReconciler, OrderReadModelReconciler>();
        services.AddScoped<IBookingStreamCatalog, BookingStreamCatalog>();
        services.AddScoped<IOrderReadModelRebuildRunner, OrderReadModelRebuildRunner>();
        services.AddScoped<IBookingConsistencyDiagnostics, BookingConsistencyDiagnostics>();
        return services;
    }
}
