using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Instrumentation.AspNetCore;
using Travel.Modules.Rail.Api.Endpoints;
using Travel.Modules.Rail.Api.Middleware;
using Travel.Modules.Rail.Application;
using Travel.Modules.Rail.Infrastructure;
using Travel.Shared.Infrastructure.Telemetry;
using Wolverine;

namespace Travel.Modules.Rail.Api.Composition;

public static class RailModule
{
    public static IHostApplicationBuilder AddRailModule(this IHostApplicationBuilder builder)
    {
        var production = builder.Environment.IsProduction();
        builder
            .Services.AddOptions<RailOptions>()
            .Bind(builder.Configuration.GetSection("Rail"))
            .Validate(
                x => !production || !x.Fictional,
                "Fictional Rail is unavailable in Production."
            )
            .ValidateOnStart();
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<RailRuntime>();
        builder.Services.AddSingleton<IRailRuntimeProfile>(s =>
            s.GetRequiredService<RailRuntime>()
        );
        builder.Services.AddSingleton<RailQuotaLedger>();
        builder
            .Services.AddHttpClient("rail-yandex", c => c.Timeout = Timeout.InfiniteTimeSpan)
            .RemoveAllLoggers()
            .ConfigurePrimaryHttpMessageHandler(() =>
                new SocketsHttpHandler
                {
                    AllowAutoRedirect = false,
                    UseCookies = false,
                    MaxResponseHeadersLength = 16,
                    MaxConnectionsPerServer = 4,
                    AutomaticDecompression =
                        System.Net.DecompressionMethods.GZip
                        | System.Net.DecompressionMethods.Deflate
                        | System.Net.DecompressionMethods.Brotli,
                }
            );
        if (!production)
            builder.Services.AddSingleton<FictionalRailTransport>();
        builder.Services.AddSingleton(s => new YandexRailClient(
            s.GetRequiredService<IHttpClientFactory>().CreateClient("rail-yandex"),
            s.GetRequiredService<RailRuntime>(),
            s.GetRequiredService<RailQuotaLedger>(),
            s.GetRequiredService<TimeProvider>()
        ));
        builder.Services.AddSingleton<IRailReferenceLoader>(s => new RailReferenceTransport(
            s.GetRequiredService<YandexRailClient>(),
            s.GetRequiredService<RailRuntime>(),
            production ? null : s.GetRequiredService<FictionalRailTransport>(),
            s.GetRequiredService<RailQuotaLedger>(),
            s.GetRequiredService<TimeProvider>()
        ));
        builder.Services.AddSingleton<IRailScheduleProvider>(s => new RailScheduleTransport(
            s.GetRequiredService<YandexRailClient>(),
            s.GetRequiredService<RailRuntime>(),
            production ? null : s.GetRequiredService<FictionalRailTransport>(),
            s.GetRequiredService<RailQuotaLedger>(),
            s.GetRequiredService<TimeProvider>()
        ));
        builder.Services.AddSingleton<IRailSearchCache, RailSearchCache>();
        builder.Services.AddSingleton<RailReferenceService>();
        builder.Services.AddSingleton<RailSearchService>();
        builder.Services.AddOpenApi(options =>
            options.AddOperationTransformer<RailOpenApiMetadata>()
        );
        builder.Services.AddSingleton<IHttpUrlRedactionContributor, RailUrlRedactionContributor>();
        builder.Services.Configure<AspNetCoreTraceInstrumentationOptions>(options =>
        {
            var requestEnricher = options.EnrichWithHttpRequest;
            options.EnrichWithHttpRequest = (activity, request) =>
            {
                requestEnricher?.Invoke(activity, request);
                if (
                    request.Path.StartsWithSegments("/api/rail", StringComparison.OrdinalIgnoreCase)
                )
                {
                    activity.SetTag("url.query", null);
                    activity.SetTag(
                        "url.full",
                        $"{request.Scheme}://{request.Host}{request.PathBase}{request.Path}"
                    );
                }
            };
        });
        // Hosting emits RequestStarting before middleware. Wrap factory-created
        // loggers before formatting; the original factory retains provider aliases,
        // filters, scopes and all non-Rail events.
        var logging = builder.Services.Last(x => x.ServiceType == typeof(ILoggerFactory));
        builder.Services.Remove(logging);
        builder.Services.AddSingleton<ILoggerFactory>(services => new RailLogFactory(
            (ILoggerFactory)(
                logging.ImplementationInstance
                ?? logging.ImplementationFactory?.Invoke(services)
                ?? ActivatorUtilities.CreateInstance(services, logging.ImplementationType!)
            )
        ));
        return builder;
    }

    public static void ConfigureWolverine(WolverineOptions options)
    {
        options.Discovery.IncludeAssembly(typeof(RailEndpoints).Assembly);
        options.CodeGeneration.AlwaysUseServiceLocationFor<RailReferenceService>();
        options.CodeGeneration.AlwaysUseServiceLocationFor<RailSearchService>();
    }

    public static WebApplication UseRailResponsePolicy(this WebApplication app)
    {
        app.UseMiddleware<RailResponsePolicy>();
        return app;
    }
}
