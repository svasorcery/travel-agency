using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Travel.Modules.Support.Api.Endpoints;
using Travel.Modules.Support.Api.Middleware;
using Travel.Modules.Support.Application;
using Travel.Modules.Support.Infrastructure.Classification;
using Travel.Modules.Support.Infrastructure.HealthChecks;
using Travel.Modules.Support.Infrastructure.Persistence;
using Travel.Modules.Support.Infrastructure.Persistence.Initialization;
using Travel.Modules.Support.Infrastructure.Privacy;
using Travel.Modules.Support.Infrastructure.Retention;
using Travel.Shared.Infrastructure.Initialization;
using Wolverine;
using Wolverine.Http;

namespace Travel.Modules.Support.Api.Composition;

public static class SupportModule
{
    public const string OperatorPolicy = "support:operator";

    public static IHostApplicationBuilder AddSupportModule(this IHostApplicationBuilder builder)
    {
        builder.AddNpgsqlDbContext<SupportDbContext>(
            "travel",
            configureSettings: settings => settings.DisableRetry = true,
            configureDbContextOptions: SupportDbContextConfiguration.Configure
        );
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder
            .Services.AddOptions<SupportTextProtectionOptions>()
            .Bind(builder.Configuration.GetSection(SupportTextProtectionOptions.SectionName));
        builder.Services.AddSingleton<SupportTextProtectionProvider>();
        builder.Services.AddSingleton<ISupportTextProtector, DataProtectionSupportTextProtector>();
        builder.Services.AddScoped<ISupportCaseStore, SupportCaseStore>();
        builder.Services.AddScoped<ISupportCaseService, SupportCaseService>();
        builder
            .Services.AddOptions<LayaClassificationOptions>()
            .Bind(builder.Configuration.GetSection("Support:Classification"));
        builder
            .Services.AddHttpClient(
                "support-laya",
                client => client.Timeout = Timeout.InfiniteTimeSpan
            )
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
        // Preserve identity-drift disablement across requests; new serving receipts require a fresh immutable instance.
        builder.Services.AddSingleton<ISupportClassificationProvider>(
            services => new LayaSupportClassificationProvider(
                services.GetRequiredService<IHttpClientFactory>().CreateClient("support-laya"),
                services.GetRequiredService<IOptions<LayaClassificationOptions>>()
            )
        );
        builder.Services.AddSingleton<SupportClassificationAdmission>();
        builder.Services.AddScoped<ISupportClassificationService, SupportClassificationService>();
        builder
            .Services.AddOpenTelemetry()
            .WithMetrics(metrics => metrics.AddMeter(SupportClassificationTelemetry.MeterName));
        builder.Services.AddSingleton<SupportSchemaReadiness>();
        builder.Services.AddSingleton<SupportRetentionStatus>();
        builder.Services.AddScoped<IInitializer, SupportEfInitializer>();
        builder.Services.AddScoped<SupportRetentionRunner>();
        builder.Services.AddHostedService<SupportRetentionWorker>();
        builder
            .Services.AddHealthChecks()
            .AddCheck<SupportHealthCheck>("support", tags: ["dependency"]);
        ConfigureAuthorization(builder.Services);
        builder.Services.AddWolverineHttp();
        return builder;
    }

    public static IServiceCollection ConfigureAuthorization(IServiceCollection services)
    {
        services.AddAuthorization(options =>
            options.AddPolicy(
                OperatorPolicy,
                policy =>
                    policy
                        .RequireAuthenticatedUser()
                        .RequireAssertion(context =>
                            context
                                .User.FindAll("scope")
                                .Any(claim =>
                                    claim
                                        .Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                                        .Contains(OperatorPolicy, StringComparer.Ordinal)
                                )
                        )
                        .RequireClaim("azp", "travel-support-operator")
            )
        );
        return services;
    }

    public static void ConfigureWolverine(WolverineOptions options)
    {
        options.Discovery.IncludeAssembly(typeof(CreateSupportCaseEndpoint).Assembly);
        options.CodeGeneration.AlwaysUseServiceLocationFor<ISupportCaseService>();
        options.CodeGeneration.AlwaysUseServiceLocationFor<ISupportClassificationService>();
    }

    public static WebApplication UseSupportResponsePolicy(this WebApplication app)
    {
        app.UseMiddleware<SupportResponsePolicyMiddleware>();
        return app;
    }

    public static WebApplication UseSupportModule(this WebApplication app)
    {
        app.UseMiddleware<SupportBodyGuardMiddleware>();
        return app;
    }
}
