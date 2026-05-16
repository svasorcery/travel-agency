using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Shouldly;
using Testcontainers.PostgreSql;
using Travel.Modules.Flights.Infrastructure.Persistence;
using Xunit;

namespace Travel.Modules.Flights.Tests.Integration.Webhooks;

[Trait("Category", "Integration")]
public sealed class WebhookInboxStoreTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder(
        "pgvector/pgvector:pg17"
    ).Build();

    private FlightsDbContext _db = default!;

    public async ValueTask InitializeAsync()
    {
        await _pg.StartAsync();
        var efOptions = new DbContextOptionsBuilder<FlightsDbContext>()
            .UseNpgsql(_pg.GetConnectionString())
            .UseSnakeCaseNamingConvention()
            .Options;
        _db = new FlightsDbContext(efOptions);
        await _db.Database.EnsureCreatedAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _db.DisposeAsync();
        await _pg.DisposeAsync();
    }

    [Fact]
    public async Task MarkProcessed_on_missing_row_logs_warning()
    {
        var ct = TestContext.Current.CancellationToken;
        var recordingLogger = new RecordingLogger<WebhookInboxStore>();
        var store = new WebhookInboxStore(_db, recordingLogger);
        var missingId = Guid.NewGuid();

        // No row with this id exists — MarkProcessedAsync must NOT throw, but the
        // silent no-op should be surfaced as a Warning so the failure is observable.
        await store.MarkProcessedAsync(missingId, DateTimeOffset.UtcNow, ct);

        var warnings = recordingLogger.Entries.Where(e => e.Level == LogLevel.Warning).ToList();
        warnings.Count.ShouldBe(1);
        warnings[0].Message.ShouldContain(missingId.ToString());
    }
}

/// <summary>
/// Minimal in-memory <see cref="ILogger{T}"/> that captures every log call so
/// tests can assert on level and rendered message. Avoids pulling
/// <c>Microsoft.Extensions.Logging.Testing</c>'s <c>FakeLogger</c> for one test.
/// </summary>
internal sealed class RecordingLogger<T> : ILogger<T>
{
    public List<(LogLevel Level, string Message)> Entries { get; } = [];

    IDisposable? ILogger.BeginScope<TState>(TState state) => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter
    ) => Entries.Add((logLevel, formatter(state, exception)));
}
