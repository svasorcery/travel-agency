using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.ServiceDiscovery;
using OpenTelemetry;
using OpenTelemetry.Instrumentation.Http;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Travel.ServiceDefaults.Health;
using Travel.ServiceDefaults.Telemetry;
using Travel.ServiceDefaults.Web;

namespace Microsoft.Extensions.Hosting;

// Adds common Aspire services: service discovery, resilience, health checks, and OpenTelemetry.
// This project should be referenced by each service project in your solution.
// To learn more about using this project, see https://aka.ms/aspire/service-defaults
public static class Extensions
{
    private const string HealthEndpointPath = "/health";
    private const string LiveTag = "live";
    private const string ReadyTag = "ready";
    private const string DependencyTag = "dependency";

    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.Services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        builder.Services.AddProblemDetails(PlatformProblemDetails.Configure);
        builder.Services.AddExceptionHandler<PlatformExceptionHandler>();
        builder.Services.AddOpenApi();

        builder.ConfigureOpenTelemetry();

        builder.AddDefaultHealthChecks();

        builder.Services.AddServiceDiscovery();

        builder.Services.ConfigureHttpClientDefaults(http => http.AddServiceDiscovery());

        // Uncomment the following to restrict the allowed schemes for service discovery.
        // builder.Services.Configure<ServiceDiscoveryOptions>(options =>
        // {
        //     options.AllowedSchemes = ["https"];
        // });

        return builder;
    }

    public static IHttpClientBuilder AddPlatformHttpResilience(this IHttpClientBuilder client)
    {
        client.AddStandardResilienceHandler();
        return client;
    }

    public static WebApplication UsePlatformWebDefaults(this WebApplication app)
    {
        app.UseExceptionHandler();
        app.UseStatusCodePages(async statusCodeContext =>
        {
            var httpContext = statusCodeContext.HttpContext;
            if (
                httpContext.Response.StatusCode
                    is not (
                        StatusCodes.Status401Unauthorized
                        or StatusCodes.Status403Forbidden
                        or StatusCodes.Status404NotFound
                    )
                || httpContext.Request.Path.StartsWithSegments(HealthEndpointPath)
            )
                return;

            await Results
                .Problem(statusCode: httpContext.Response.StatusCode)
                .ExecuteAsync(httpContext);
        });

        if (!app.Environment.IsProduction())
        {
            app.MapOpenApi().AllowAnonymous();
        }

        return app;
    }

    public static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                IConfigureOptions<HttpClientTraceInstrumentationOptions>,
                HttpUrlRedactionOptionsConfigurator
            >()
        );

        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder
            .Services.AddOpenTelemetry()
            .WithMetrics(metrics =>
            {
                metrics
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();
            })
            .WithTracing(tracing =>
            {
                tracing
                    .AddSource(builder.Environment.ApplicationName)
                    .AddAspNetCoreInstrumentation(tracing =>
                        // Exclude health check requests from tracing
                        tracing.Filter = context =>
                            !context.Request.Path.StartsWithSegments(HealthEndpointPath)
                    )
                    // Uncomment the following line to enable gRPC instrumentation (requires the OpenTelemetry.Instrumentation.GrpcNetClient package)
                    //.AddGrpcClientInstrumentation()
                    .AddHttpClientInstrumentation();
            });

        builder.AddOpenTelemetryExporters();

        return builder;
    }

    private static TBuilder AddOpenTelemetryExporters<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        var useOtlpExporter = !string.IsNullOrWhiteSpace(
            builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]
        );

        if (useOtlpExporter)
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }

        // Uncomment the following lines to enable the Azure Monitor exporter (requires the Azure.Monitor.OpenTelemetry.AspNetCore package)
        //if (!string.IsNullOrEmpty(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
        //{
        //    builder.Services.AddOpenTelemetry()
        //       .UseAzureMonitor();
        //}

        return builder;
    }

    public static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder
            .Services.AddHealthChecks()
            // Add a default liveness check to ensure app is responsive
            .AddCheck("self", () => HealthCheckResult.Healthy(), [LiveTag]);

        return builder;
    }

    public static WebApplicationBuilder AddInternalHealthEndpoints(
        this WebApplicationBuilder builder,
        int nonProductionPort
    )
    {
        var publicBinding = GetEffectivePublicBinding(builder.Configuration);
        var rawPort = builder.Configuration[$"{HealthEndpointOptions.SectionName}:InternalPort"];
        var hasConfiguredPort = int.TryParse(rawPort, out var configuredPort);
        var effectivePort =
            hasConfiguredPort ? configuredPort
            : builder.Environment.IsProduction() ? -1
            : nonProductionPort;

        builder
            .Services.AddOptions<HealthEndpointOptions>()
            .Configure(options => options.InternalPort = effectivePort)
            .Validate(
                options => options.InternalPort is > 0 and <= 65535,
                "HealthEndpoints:InternalPort must be an integer from 1 through 65535."
            )
            .Validate(
                options => !publicBinding.Urls.Any(url => GetPort(url) == options.InternalPort),
                "HealthEndpoints:InternalPort must not collide with a public listener port."
            )
            .ValidateOnStart();

        if (effectivePort is > 0 and <= 65535)
        {
            if (publicBinding.UsesKestrelEndpoints)
            {
                builder.WebHost.ConfigureKestrel(options =>
                    options.Listen(IPAddress.Loopback, effectivePort)
                );
            }
            else
            {
                builder.WebHost.UseUrls([
                    .. publicBinding.Urls,
                    $"http://127.0.0.1:{effectivePort}",
                ]);
            }
        }

        return builder;
    }

    public static IServiceCollection AddRequiredTcpDependencyHealthCheck(
        this IServiceCollection services,
        string name,
        string host,
        int port
    )
    {
        services
            .AddHealthChecks()
            .AddCheck(
                name,
                new TcpDependencyHealthCheck(name, host, port),
                failureStatus: HealthStatus.Unhealthy,
                tags: [ReadyTag]
            );
        return services;
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        var internalPort = app
            .Services.GetRequiredService<IOptions<HealthEndpointOptions>>()
            .Value.InternalPort;

        app.Use(
            async (context, next) =>
            {
                if (
                    context.Request.Path.StartsWithSegments(HealthEndpointPath)
                    && context.Connection.LocalPort != internalPort
                )
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    return;
                }

                await next(context);
            }
        );

        MapInternalHealthCheck(app, "/health/live", LiveTag);
        MapInternalHealthCheck(app, "/health/ready", ReadyTag);
        MapInternalHealthCheck(app, "/health/dependencies", DependencyTag);

        return app;
    }

    private static void MapInternalHealthCheck(WebApplication app, string path, string tag)
    {
        var endpoint = app.MapHealthChecks(
            path,
            new HealthCheckOptions
            {
                Predicate = registration => registration.Tags.Contains(tag),
                ResponseWriter = WriteHealthResponseAsync,
            }
        );
        endpoint.AllowAnonymous();
    }

    private static Task WriteHealthResponseAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";
        return JsonSerializer.SerializeAsync(
            context.Response.Body,
            new
            {
                status = report.Status.ToString(),
                checks = report.Entries.ToDictionary(
                    entry => entry.Key,
                    entry => new
                    {
                        status = entry.Value.Status.ToString(),
                        description = entry.Value.Description,
                    }
                ),
            },
            cancellationToken: context.RequestAborted
        );
    }

    private static PublicBinding GetEffectivePublicBinding(IConfiguration configuration)
    {
        var kestrelEndpoints = configuration
            .GetSection("Kestrel:Endpoints")
            .GetChildren()
            .ToArray();
        if (kestrelEndpoints.Length > 0)
        {
            return new PublicBinding(
                kestrelEndpoints
                    .Select(endpoint => endpoint["Url"])
                    .Where(url => !string.IsNullOrWhiteSpace(url))
                    .Select(url => url!.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                UsesKestrelEndpoints: true
            );
        }

        var urls = FirstConfiguredValue(configuration["urls"], configuration["ASPNETCORE_URLS"]);
        if (urls is not null)
            return new PublicBinding(SplitUrls(urls), UsesKestrelEndpoints: false);

        var portUrls = new List<string>();
        AddPortUrls(
            portUrls,
            FirstConfiguredValue(
                configuration["HTTP_PORTS"],
                configuration["ASPNETCORE_HTTP_PORTS"]
            ),
            "http"
        );
        AddPortUrls(
            portUrls,
            FirstConfiguredValue(
                configuration["HTTPS_PORTS"],
                configuration["ASPNETCORE_HTTPS_PORTS"]
            ),
            "https"
        );
        if (portUrls.Count > 0)
            return new PublicBinding(portUrls.ToArray(), UsesKestrelEndpoints: false);

        return new PublicBinding(["http://localhost:5000"], UsesKestrelEndpoints: false);
    }

    private static string? FirstConfiguredValue(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private static string[] SplitUrls(string value) =>
        value
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static void AddPortUrls(List<string> urls, string? ports, string scheme)
    {
        if (string.IsNullOrWhiteSpace(ports))
            return;

        foreach (
            var port in ports.Split(
                ';',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
            )
        )
            urls.Add($"{scheme}://0.0.0.0:{port}");
    }

    private static int GetPort(string url)
    {
        var normalized = url.Trim()
            .Replace("://+:", "://0.0.0.0:", StringComparison.OrdinalIgnoreCase)
            .Replace("://*:", "://0.0.0.0:", StringComparison.OrdinalIgnoreCase);
        return Uri.TryCreate(normalized, UriKind.Absolute, out var uri) ? uri.Port : -1;
    }

    private sealed record PublicBinding(string[] Urls, bool UsesKestrelEndpoints);

    private sealed class TcpDependencyHealthCheck(string name, string host, int port) : IHealthCheck
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(1);

        public async Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default
        )
        {
            if (string.IsNullOrWhiteSpace(host) || port is <= 0 or > 65535)
                return HealthCheckResult.Unhealthy($"{name} endpoint is not configured.");

            try
            {
                using var client = new TcpClient();
                await client
                    .ConnectAsync(host, port, cancellationToken)
                    .AsTask()
                    .WaitAsync(Timeout, cancellationToken);
                return HealthCheckResult.Healthy($"{name} endpoint is reachable.");
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                return HealthCheckResult.Unhealthy($"{name} endpoint is not reachable.");
            }
        }
    }
}
