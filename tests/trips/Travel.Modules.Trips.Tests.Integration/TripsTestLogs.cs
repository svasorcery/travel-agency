using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Travel.Modules.Trips.Tests.Integration;

internal sealed class TripsTestLogs : ILoggerProvider
{
    internal ConcurrentQueue<string> Values { get; } = new();
    internal bool Overflow { get; private set; }

    private void Record(string value)
    {
        if (Values.Count >= 1024 || value.Length > 8192)
        {
            Overflow = true;
            return;
        }
        Values.Enqueue(value);
    }

    public ILogger CreateLogger(string categoryName) => new Sink(this);

    public void Dispose() { }

    private sealed class Sink(TripsTestLogs owner) : ILogger
    {
        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull
        {
            owner.Record(Format(state));
            return new Scope();
        }

        public bool IsEnabled(LogLevel level) => true;

        public void Log<TState>(
            LogLevel level,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            owner.Record(formatter(state, exception));
            owner.Record(Format(state));
            if (exception is not null)
                owner.Record(exception.ToString());
        }

        private static string Format<T>(T value) =>
            value is IEnumerable<KeyValuePair<string, object?>> pairs
                ? string.Join(";", pairs.Select(x => x.Key + "=" + x.Value))
                : value?.ToString() ?? "";

        private sealed class Scope : IDisposable
        {
            public void Dispose() { }
        }
    }
}
