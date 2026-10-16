extern alias TravelAiApp;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shouldly;
using Testcontainers.PostgreSql;
using Travel.IntegrationContracts.AI.NlSearch;
using Travel.Shared.Infrastructure.Initialization;
using Wolverine;
using Wolverine.Nats;
using Xunit;

namespace Travel.Host.Tests.Integration.NlSearch;

[Trait("Category", "Integration")]
[Collection(HostIntegrationCollection.Name)]
public sealed class AiPendingSchemaTransportTests
{
    [Fact(Timeout = 90_000)]
    public async Task Pending_production_ledger_schema_keeps_diagnostics_live_without_business_consumers()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var postgres = new PostgreSqlBuilder("pgvector/pgvector:pg17").Build();
        await using var nats = new ContainerBuilder("library/nats:2.12")
            .WithPortBinding(4222, true)
            .WithPortBinding(8222, true)
            .WithCommand("-m", "8222")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Server is ready"))
            .Build();
        await postgres.StartAsync(ct);
        await nats.StartAsync(ct);
        var connectionString = postgres.GetConnectionString();
        await using (
            var db = new TravelAiApp::Travel.AI.Persistence.AiDbContext(
                new DbContextOptionsBuilder<TravelAiApp::Travel.AI.Persistence.AiDbContext>()
                    .UseNpgsql(
                        connectionString,
                        TravelAiApp::Travel.AI.Persistence.AiDbContextConfiguration.ConfigureNpgsql
                    )
                    .UseSnakeCaseNamingConvention()
                    .Options
            )
        )
            await db.GetService<IMigrator>().MigrateAsync("20260514090212_CostLedgerInit", ct);

        var chat = new CountingChatClient();
        var internalPort = GetAvailablePort();
        await using var factory =
            new WebApplicationFactory<TravelAiApp::Program>().WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment(Environments.Production);
                builder.UseSetting("ConnectionStrings:travel", connectionString);
                builder.UseSetting(
                    "ConnectionStrings:nats",
                    $"nats://{nats.Hostname}:{nats.GetMappedPublicPort(4222)}"
                );
                builder.UseSetting("HealthEndpoints:InternalPort", internalPort.ToString());
                builder.UseSetting("Anthropic:ApiKey", "fictional-test-key");
                builder.UseSetting("OTEL_EXPORTER_OTLP_ENDPOINT", "");
                builder.ConfigureLogging(logging => logging.ClearProviders());
                builder.ConfigureServices(services =>
                {
                    // Disposable loopback endpoints are deliberate. Preserve every real
                    // initializer and runtime service; relax only the deployment-host validator.
                    services.RemoveAll<
                        IValidateOptions<TravelAiApp::Travel.AI.Configuration.AiConnectionOptions>
                    >();
                    services.RemoveAll<IChatClient>();
                    services.AddSingleton<IChatClient>(chat);
                });
            });
        factory.UseKestrel();
        using var startup = factory.CreateClient();
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        (
            await http.GetAsync($"http://127.0.0.1:{internalPort}/health/live", ct)
        ).StatusCode.ShouldBe(HttpStatusCode.OK);
        (
            await http.GetAsync($"http://127.0.0.1:{internalPort}/health/ready", ct)
        ).StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        factory
            .Services.GetRequiredService<AppInitializer>()
            .State.ShouldBe(InitializationState.Failed);

        using var sender = Microsoft
            .Extensions.Hosting.Host.CreateDefaultBuilder()
            .ConfigureLogging(logging => logging.ClearProviders())
            .UseWolverine(options =>
            {
                options.UseNats($"nats://{nats.Hostname}:{nats.GetMappedPublicPort(4222)}");
                options.PublishMessage<NlSearchRequested>().ToNatsSubject("travel.ai.nl_search");
            })
            .Build();
        await sender.StartAsync(ct);
        try
        {
            await using var senderScope = sender.Services.CreateAsyncScope();
            await Should.ThrowAsync<Exception>(() =>
                senderScope
                    .ServiceProvider.GetRequiredService<IMessageBus>()
                    .InvokeAsync<NlSearchParsed>(
                        new NlSearchRequested("fictional route", Guid.NewGuid()),
                        ct,
                        timeout: TimeSpan.FromSeconds(1)
                    )
            );
            chat.Calls.ShouldBe(0);
        }
        finally
        {
            await sender.StopAsync(ct);
        }

        using (
            var response = await http.GetAsync(
                $"http://{nats.Hostname}:{nats.GetMappedPublicPort(8222)}/subsz?subs=1&test=travel.ai.nl_search",
                ct
            )
        )
        {
            var json = await response.Content.ReadAsStringAsync(ct);
            NlSearchTransportFixture
                .TryReadAiListenerMembership(json, expectedSubscriptionCount: 0, out _)
                .ShouldBeTrue();
        }
        await using var scope = factory.Services.CreateAsyncScope();
        var ledger =
            scope.ServiceProvider.GetRequiredService<TravelAiApp::Travel.AI.Persistence.AiDbContext>();
        (await ledger.Database.GetPendingMigrationsAsync(ct)).ShouldContain(
            "20260813200652_CostLedgerMessageIdempotency"
        );
    }

    private static int GetAvailablePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private sealed class CountingChatClient : IChatClient
    {
        private int calls;
        public int Calls => Volatile.Read(ref calls);

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default
        )
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "null")));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }
}
