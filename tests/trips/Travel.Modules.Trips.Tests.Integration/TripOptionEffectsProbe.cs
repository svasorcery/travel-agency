using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Travel.IntegrationContracts.AI.TripsBriefExtraction;
using Travel.IntegrationContracts.Flights.Trips;
using Travel.IntegrationContracts.Hotels.Trips;
using Travel.Modules.Flights.Core.Providers;
using Travel.Modules.Hotels.Core;
using Travel.Modules.Trips.Application;
using Travel.Modules.Trips.Application.BriefExtraction;
using Travel.Modules.Trips.Infrastructure.BriefExtraction;
using Wolverine;

namespace Travel.Modules.Trips.Tests.Integration;

// The test fixture runs the production endpoint and middleware. Observe its injected business
// boundaries before any commercial implementation could perform a real action.
internal sealed class TripOptionEffectsProbe : IStartupFilter
{
    internal const string Header = "X-Fictional-Effects-Case";
    private readonly AsyncLocal<string?> current = new();
    private readonly ConcurrentDictionary<(string Case, string Port), int> calls = new();

    internal sealed record ServerCompletion(
        string Method,
        string Path,
        string CacheControl,
        bool RequestAborted
    );

    private readonly ConcurrentDictionary<string, TaskCompletionSource<ServerCompletion>> finished =
        new();

    internal void Register(IServiceCollection services)
    {
        services.AddSingleton<IStartupFilter>(this);
        var store = (ITripPlanStore)
            services.Last(s => s.ServiceType == typeof(ITripPlanStore)).ImplementationInstance!;
        services.AddSingleton(
            TripOptionEffectsProxy.Observe<ITripPlanStore>(
                (method, args) =>
                {
                    Count("workspace." + method.Name);
                    return method.Invoke(store, args);
                }
            )
        );
        RegisterPort<IFlightsTripsOrderAccess>(services);
        RegisterPort<IHotelsTripsReservationAccess>(services);
        RegisterPort<IPaymentGateway>(services);
        RegisterPort<IFlightBookingProvider>(services);
        RegisterPort<IFlightSearchProvider>(services);
        RegisterPort<IHotelBookingProvider>(services);
        RegisterPort<IHotelSearchProvider>(services);
        services.AddSingleton(
            TripOptionEffectsProxy.Observe<IMessageBus>(
                (method, args) =>
                {
                    Count(
                        args?.FirstOrDefault() is TripsBriefRequested ? "modelRpc" : "messageBus"
                    );
                    throw new BlockedTestEffect();
                }
            )
        );
        services.AddTransient<NatsTripBriefExtractionClient>();
        services.AddTransient(provider =>
        {
            var modelClient = provider.GetRequiredService<NatsTripBriefExtractionClient>();
            return TripOptionEffectsProxy.Observe<ITripBriefExtractionClient>(
                (method, args) =>
                {
                    Count("modelPort." + method.Name);
                    return method.Invoke(modelClient, args);
                }
            );
        });
        services.AddHttpClient();
        services.ConfigureHttpClientDefaults(client =>
            client.ConfigurePrimaryHttpMessageHandler(() =>
                new OutboundHttp(() => Count("outboundHttp"))
            )
        );
    }

    private void RegisterPort<T>(IServiceCollection services)
        where T : class =>
        services.AddSingleton(
            TripOptionEffectsProxy.Observe<T>(
                (method, _) =>
                {
                    Count(typeof(T).Name + "." + method.Name);
                    throw new BlockedTestEffect();
                }
            )
        );

    private void Count(string port) =>
        calls.AddOrUpdate((current.Value ?? "outside-request", port), 1, (_, n) => n + 1);

    internal Dictionary<string, int> Counts(string label) =>
        calls.Where(c => c.Key.Case == label).ToDictionary(c => c.Key.Port, c => c.Value);

    internal Task<ServerCompletion> Completion(string label, CancellationToken ct) =>
        Gate(label).Task.WaitAsync(TimeSpan.FromSeconds(15), ct);

    private TaskCompletionSource<ServerCompletion> Gate(string label) =>
        finished.GetOrAdd(label, _ => new(TaskCreationOptions.RunContinuationsAsynchronously));

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
        app =>
        {
            app.Use(
                async (context, advance) =>
                {
                    var label = context.Request.Headers[Header].ToString();
                    current.Value = label;
                    try
                    {
                        await advance();
                    }
                    finally
                    {
                        Gate(label)
                            .TrySetResult(
                                new(
                                    context.Request.Method,
                                    context.Request.Path.Value!,
                                    context.Response.Headers.CacheControl.ToString(),
                                    context.RequestAborted.IsCancellationRequested
                                )
                            );
                        current.Value = null;
                    }
                }
            );
            next(app);
        };

    internal sealed class BlockedTestEffect : Exception
    {
        public BlockedTestEffect() { }

        public BlockedTestEffect(string message)
            : base(message) { }

        public BlockedTestEffect(string message, Exception innerException)
            : base(message, innerException) { }
    }

    private sealed class OutboundHttp(Action count) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct
        )
        {
            count();
            throw new BlockedTestEffect();
        }
    }
}

// DispatchProxy requires a public, inheritable parameterless implementation.
public class TripOptionEffectsProxy : DispatchProxy
{
    private Func<MethodInfo, object?[]?, object?> invoke = null!;

    internal static T Observe<T>(Func<MethodInfo, object?[]?, object?> invoke)
        where T : class
    {
        var proxy = Create<T, TripOptionEffectsProxy>();
        ((TripOptionEffectsProxy)(object)proxy).invoke = invoke;
        return proxy;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
        invoke(targetMethod!, args);
}
