using Testcontainers.PostgreSql;
using Xunit;

namespace Travel.Shared.TestInfrastructure;

public abstract class IntegrationTestBase : IAsyncLifetime
{
    protected PostgreSqlContainer Postgres { get; } = new PostgreSqlBuilder("pgvector/pgvector:pg17")
        .WithDatabase("travel_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    public async ValueTask InitializeAsync()
    {
        await Postgres.StartAsync();
        await OnInitializedAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await OnDisposingAsync();
        await Postgres.DisposeAsync();
    }

    protected virtual ValueTask OnInitializedAsync() => ValueTask.CompletedTask;
    protected virtual ValueTask OnDisposingAsync()    => ValueTask.CompletedTask;

    protected string ConnectionString => Postgres.GetConnectionString();
}
