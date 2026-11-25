using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Instrumentation.AspNetCore;

namespace Travel.Modules.Trips.Api.Composition;

/// <summary>RequestStarting and request scopes precede middleware. Filter before either is formatted.</summary>
internal static class TripFlightLinkDiagnostics
{
    public static void Register(IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.Configure<AspNetCoreTraceInstrumentationOptions>(options =>
        {
            var previous = options.Filter;
            options.Filter = ctx =>
                !PrivatePath(ctx.Request.Path.Value) && (previous?.Invoke(ctx) ?? true);
        });
        var registration = services.LastOrDefault(d =>
            d.ServiceType == typeof(ILoggerFactory) && !d.IsKeyedService
        );
        if (registration == null)
            return;
        services.Remove(registration);
        services.AddSingleton<ILoggerFactory>(s => new LinkLoggerFactory(
            (ILoggerFactory)(
                registration.ImplementationInstance
                ?? registration.ImplementationFactory?.Invoke(s)
                ?? ActivatorUtilities.CreateInstance(s, registration.ImplementationType!)
            ),
            s.GetRequiredService<IHttpContextAccessor>()
        ));
    }

    internal static bool PrivatePath(string? path)
    {
        if (path == null || !path.StartsWith("/api/trips/", StringComparison.OrdinalIgnoreCase))
            return false;
        var end = path.IndexOf('?');
        var route = end < 0 ? path.AsSpan() : path.AsSpan(0, end);
        return route.StartsWith("/api/trips/option-preview", StringComparison.OrdinalIgnoreCase)
            || route.Contains("/flight-links", StringComparison.OrdinalIgnoreCase)
            || route.Contains("/flight-order-candidates", StringComparison.OrdinalIgnoreCase)
            || route.Contains("/hotel-link", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class LinkLoggerFactory(ILoggerFactory inner, IHttpContextAccessor accessor)
        : ILoggerFactory
    {
        public ILogger CreateLogger(string categoryName) =>
            new LinkLogger(inner.CreateLogger(categoryName), accessor);

        public void Dispose() => inner.Dispose();

        public void AddProvider(ILoggerProvider provider) => inner.AddProvider(provider);
    }

    private sealed class LinkLogger(ILogger inner, IHttpContextAccessor accessor) : ILogger
    {
        private bool Sensitive<T>(T state) =>
            PrivatePath(accessor.HttpContext?.Request.Path.Value)
            || state is IEnumerable<KeyValuePair<string, object?>> fields
                && fields.Any(f =>
                    f.Key is "Path" or "RequestPath" && PrivatePath(f.Value?.ToString())
                );

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => Sensitive(state) ? null : inner.BeginScope(state);

        public bool IsEnabled(LogLevel level) => inner.IsEnabled(level);

        public void Log<TState>(
            LogLevel level,
            EventId id,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            if (!Sensitive(state))
                inner.Log(level, id, state, exception, formatter);
        }
    }
}
