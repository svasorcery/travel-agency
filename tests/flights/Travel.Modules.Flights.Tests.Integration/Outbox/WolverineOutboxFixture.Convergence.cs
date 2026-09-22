using JasperFx;
using Marten;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Travel.Modules.Flights.Api.Composition;
using Travel.Modules.Flights.Application.Commands;
using Travel.Modules.Flights.Application.Contracts;
using Travel.Modules.Flights.Application.Handlers.Booking;
using Travel.Modules.Flights.Application.Notifications;
using Travel.Modules.Flights.Application.ReadModels;
using Travel.Modules.Flights.Core.Providers;
using Travel.Modules.Flights.Infrastructure.Persistence;
using Travel.Modules.Flights.Tests.Integration.Booking;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.Marten;
using Wolverine.Runtime;

namespace Travel.Modules.Flights.Tests.Integration.Outbox;

public sealed partial class WolverineOutboxFixture
{
    // Separate host factory: existing probe-only fixtures retain their smaller discovery surface.
    // Here module DI, handler discovery, routing and error policies are production contributions.
    internal static async Task<IHost> StartConvergenceHostAsync(
        string connectionString,
        ConvergenceFaults faults,
        ConvergenceExternalServices external,
        bool recoveryEnabled,
        CancellationToken ct
    )
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(
            new HostApplicationBuilderSettings { EnvironmentName = Environments.Development }
        );
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:travel"] = connectionString,
                ["Flights:FeatureFlags:Travelpayouts:Enabled"] = "false",
            }
        );
        builder.Services.AddSingleton(TimeProvider.System);
        // The module facade also registers HTTP metadata. Supply the Host-owned routing
        // services without opening an HTTP listener in this messaging-only test host.
        builder.Services.AddRouting();
        builder.AddFlightsModule();
        builder.Services.AddSingleton(faults);
        builder.Services.ConfigureDbContext<FlightsDbContext>(
            options => options.AddInterceptors(faults),
            ServiceLifetime.Singleton
        );
        builder.Services.RemoveAll<IFlightBookingProvider>();
        builder.Services.AddSingleton<IFlightBookingProvider>(external);
        builder.Services.Replace(ServiceDescriptor.Singleton<IPaymentGateway>(external));
        builder.Services.Replace(ServiceDescriptor.Singleton<IEmailSender>(external));
        builder.Services.Replace(ServiceDescriptor.Singleton<IUserDirectory>(external));
        builder
            .Services.AddMarten(options =>
            {
                options.Connection(connectionString);
                options.AutoCreateSchemaObjects = AutoCreate.All;
                FlightsModule.ConfigureMarten(options);
            })
            .UseLightweightSessions()
            .IntegrateWithWolverine();
        builder.UseWolverine(options =>
        {
            // Keep production assembly discovery; exclude the test assembly's probe handlers.
            options.ApplicationAssembly = typeof(ConfirmOrderHandler).Assembly;
            FlightsModule.ConfigureWolverine(options);
            options.Policies.AutoApplyTransactions();
            options.Policies.UseDurableLocalQueues();
            options.UseEntityFrameworkCoreTransactions();
            options.Policies.AddMiddleware<ConvergenceReconcileMiddleware>(chain =>
                chain.MessageType == typeof(ReconcileOrderReadModel)
            );
            options.Policies.AddMiddleware<ConvergenceWebhookMiddleware>(chain =>
                chain.MessageType == typeof(ProcessDuffelWebhookCommand)
            );
            options.Policies.AddMiddleware<ConvergenceNotificationMiddleware>(chain =>
                chain.MessageType == typeof(OrderConfirmedNotification)
                || chain.MessageType == typeof(OrderTicketedNotification)
            );
            options.Services.RunWolverineInSoloMode();
            options.Durability.DurabilityAgentEnabled = recoveryEnabled;
            options.Durability.ScheduledJobPollingTime = TimeSpan.FromMilliseconds(100);
        });
        builder.Services.DisableAllExternalWolverineTransports();
        var host = builder.Build();
        try
        {
            // Cold host A starts with recovery disabled; prepare Wolverine's existing
            // durable schema explicitly, without ever running its recovery agent.
            await host
                .Services.GetRequiredService<IWolverineRuntime>()
                .Storage.Admin.MigrateAsync();
            await using (var scope = host.Services.CreateAsyncScope())
                await scope
                    .ServiceProvider.GetRequiredService<FlightsDbContext>()
                    .Database.MigrateAsync(ct);
            await host.StartAsync(ct);
            return host;
        }
        catch
        {
            host.Dispose();
            throw;
        }
    }
}
