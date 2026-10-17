using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Travel.Modules.Hotels.Api.Endpoints;
using Travel.Modules.Hotels.Api.Middleware;
using Travel.Modules.Hotels.Application;
using Travel.Modules.Hotels.Core;
using Travel.Modules.Hotels.Infrastructure.Observations;
using Travel.Modules.Hotels.Infrastructure.Persistence;
using Travel.Modules.Hotels.Infrastructure.Privacy;
using Travel.Modules.Hotels.Infrastructure.Providers.Duffel;
using Travel.Modules.Hotels.Infrastructure.Webhooks;
using Travel.Shared.Abstractions;
using Travel.Shared.Infrastructure.Initialization;
using Wolverine;
using Wolverine.Http;

namespace Travel.Modules.Hotels.Api.Composition;

public static class HotelsModule
{
    public static IHostApplicationBuilder AddHotelsModule(this IHostApplicationBuilder builder)
    {
        var production = builder.Environment.IsProduction();
        builder
            .Services.AddOptions<HotelRequestOptions>()
            .Bind(builder.Configuration.GetSection("Hotels"))
            .Validate(o => o.IsValid(), "Hotels HTTP request bounds are invalid.")
            .ValidateOnStart();
        builder
            .Services.AddOptions<HotelsOptions>()
            .Bind(builder.Configuration.GetSection("Hotels"))
            .Validate(o => o.IsValid(production), "Hotels configuration is not valid.")
            .ValidateOnStart();
        builder
            .Services.AddOptions<HotelPolicy>()
            .Bind(builder.Configuration.GetSection("Hotels:Policy"))
            .Validate(o => o.IsValid(), "Hotels policy is not valid.")
            .ValidateOnStart();
        builder.Services.AddSingleton(s => s.GetRequiredService<IOptions<HotelPolicy>>().Value);
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.AddNpgsqlDbContext<HotelsDbContext>(
            "travel",
            configureSettings: s => s.DisableRetry = true,
            configureDbContextOptions: HotelsDbContextConfiguration.Configure
        );
        builder.Services.AddSingleton<HotelCapacity>();
        builder.Services.AddSingleton<IHotelCapacity>(s => s.GetRequiredService<HotelCapacity>());
        builder.Services.AddSingleton(s =>
        {
            var o = s.GetRequiredService<IOptions<HotelsOptions>>().Value;
            return new HotelSnapshotCache(
                s.GetRequiredService<TimeProvider>(),
                o.CacheEntries,
                o.CacheBytes
            );
        });
        if (!production)
        {
            builder
                .Services.AddHttpClient("hotels-duffel", c => c.Timeout = Timeout.InfiniteTimeSpan)
                .RemoveAllLoggers()
                .ConfigurePrimaryHttpMessageHandler(() =>
                    new SocketsHttpHandler
                    {
                        AllowAutoRedirect = false,
                        UseCookies = false,
                        MaxConnectionsPerServer = 2,
                        MaxResponseHeadersLength = 16,
                        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
                    }
                );
            builder.Services.AddSingleton(s => new DuffelStaysProvider(
                s.GetRequiredService<IHttpClientFactory>().CreateClient("hotels-duffel"),
                s.GetRequiredService<IOptions<HotelsOptions>>(),
                s.GetRequiredService<HotelCapacity>(),
                s.GetRequiredService<TimeProvider>()
            ));
            builder.Services.AddSingleton<IHotelSearchProvider>(s =>
                s.GetRequiredService<DuffelStaysProvider>()
            );
            builder.Services.AddSingleton<IHotelBookingProvider>(s =>
                s.GetRequiredService<DuffelStaysProvider>()
            );
        }
        builder.Services.AddScoped<HotelSearchService>();
        builder.Services.AddScoped<HotelReservationService>();
        builder.Services.AddScoped<HotelReservationStore>();
        builder.Services.AddScoped<IHotelReservationStore>(s =>
            s.GetRequiredService<HotelReservationStore>()
        );
        builder.Services.AddScoped<HotelObservationRunner>();
        builder.Services.AddScoped<IRawWebhookReceiver, DuffelStaysWebhookReceiver>();
        builder
            .Services.AddOptions<HotelPiiProtectionOptions>()
            .Bind(builder.Configuration.GetSection(HotelPiiProtectionOptions.SectionName));
        builder.Services.AddSingleton<HotelPiiProtectionProvider>();
        builder.Services.AddSingleton<IHotelPrivateProtector, HotelPrivateProtector>();
        builder.Services.AddScoped<IInitializer, HotelsEfInitializer>();
        builder.Services.AddSingleton<HotelsSchemaReadiness>();
        builder.Services.AddHostedService<HotelRetentionWorker>();
        ConfigureAuthorization(builder.Services);
        builder.Services.AddWolverineHttp();
        return builder;
    }

    public static IServiceCollection ConfigureAuthorization(IServiceCollection services)
    {
        services.AddAuthorization(o =>
            o.AddPolicy(
                "hotels:book",
                p =>
                    p.RequireAuthenticatedUser()
                        .RequireAssertion(c =>
                            c.User.FindAll("scope")
                                .Any(s =>
                                    s.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                                        .Contains("hotels:book", StringComparer.Ordinal)
                                ) && c.User.FindAll("aud").Any(a => a.Value == "travel-web")
                        )
            )
        );
        return services;
    }

    public static void ConfigureWolverine(WolverineOptions options)
    {
        options.Discovery.IncludeAssembly(typeof(HotelEndpoints).Assembly);
        options.Discovery.IncludeAssembly(typeof(HotelWorkHandler).Assembly);
        options.CodeGeneration.AlwaysUseServiceLocationFor<HotelObservationRunner>();
        options.CodeGeneration.AlwaysUseServiceLocationFor<HotelSearchService>();
        options.CodeGeneration.AlwaysUseServiceLocationFor<HotelReservationService>();
    }

    public static WebApplication UseHotelsResponsePolicy(this WebApplication app)
    {
        app.UseMiddleware<HotelsRequestPolicyMiddleware>();
        return app;
    }

    public static WebApplication UseHotelsModule(this WebApplication app)
    {
        app.Use(
            async (context, next) =>
            {
                if (
                    !context.Request.Path.StartsWithSegments(
                        "/api/hotels",
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    await next(context);
                    return;
                }
                var settings = context
                    .RequestServices.GetRequiredService<IOptions<HotelsOptions>>()
                    .Value;
                var writing =
                    context.Request.Path.Value?.EndsWith(
                        "/quote",
                        StringComparison.OrdinalIgnoreCase
                    ) == true
                    || context.Request.Path.Value?.EndsWith(
                        "/purchase",
                        StringComparison.OrdinalIgnoreCase
                    ) == true;
                if (!settings.Enabled || writing && !settings.BookingEnabled)
                {
                    await Results
                        .Problem(statusCode: 503, title: "Hotels is unavailable.")
                        .ExecuteAsync(context);
                    return;
                }
                await next(context);
            }
        );
        return app;
    }
}
