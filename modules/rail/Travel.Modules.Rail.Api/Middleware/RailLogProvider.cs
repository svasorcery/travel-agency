using Microsoft.Extensions.Logging;

namespace Travel.Modules.Rail.Api.Middleware;

public sealed class RailLogFactory(ILoggerFactory inner) : ILoggerFactory
{
    public ILogger CreateLogger(string categoryName) =>
        new Logger(inner.CreateLogger(categoryName));

    public void Dispose() => inner.Dispose();

    public void AddProvider(ILoggerProvider provider) => inner.AddProvider(provider);

    private sealed class Logger(ILogger inner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => inner.BeginScope(state);

        public bool IsEnabled(LogLevel logLevel) => inner.IsEnabled(logLevel);

        public void Log<TState>(
            LogLevel level,
            EventId eventId,
            TState state,
            Exception? error,
            Func<TState, Exception?, string> formatter
        )
        {
            if (state is IEnumerable<KeyValuePair<string, object?>> properties)
            {
                var path = properties
                    .FirstOrDefault(x => x.Key is "Path" or "RequestPath")
                    .Value?.ToString();
                if (
                    path is not null
                    && (
                        path.Equals("/api/rail", StringComparison.OrdinalIgnoreCase)
                        || path.StartsWith("/api/rail/", StringComparison.OrdinalIgnoreCase)
                    )
                )
                {
                    var safe = new Dictionary<string, object?>
                    {
                        { "Path", path },
                        { "RailEvent", eventId.Id },
                    };
                    inner.Log(
                        level,
                        eventId,
                        safe,
                        null,
                        static (s, _) => $"Rail HTTP event {s["RailEvent"]} {s["Path"]}"
                    );
                    return;
                }
            }
            inner.Log(level, eventId, state, error, formatter);
        }
    }
}
