using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Travel.Shared.Infrastructure.Initialization;
using Xunit;

namespace Travel.Host.Tests.Integration.Initialization;

public sealed class InitializationGatedHostedServiceTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Owner_service_is_created_started_and_stopped_only_after_success(bool failure)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IInitializer>(new Initializer(failure));
        using var provider = services.BuildServiceProvider();
        var initialization = new AppInitializer(
            provider,
            new Environment(),
            NullLogger<AppInitializer>.Instance
        );
        var consumer = new Consumer();
        var created = 0;
        var gated = new InitializationGatedHostedService(
            initialization,
            provider,
            _ =>
            {
                created++;
                initialization.State.ShouldBe(InitializationState.Succeeded);
                return consumer;
            }
        );

        await gated.StartAsync(TestContext.Current.CancellationToken);
        await gated.StopAsync(TestContext.Current.CancellationToken);
        created.ShouldBe(failure ? 0 : 1);
        consumer.Starts.ShouldBe(failure ? 0 : 1);
        consumer.Stops.ShouldBe(failure ? 0 : 1);
        initialization.State.ShouldBe(
            failure ? InitializationState.Failed : InitializationState.Succeeded
        );
    }

    private sealed class Initializer(bool fail) : IInitializer
    {
        public InitializationPhase Phase => InitializationPhase.RelationalSchema;

        public Task InitializeAsync(CancellationToken ct) =>
            fail
                ? Task.FromException(new InvalidOperationException("fictional schema failure"))
                : Task.CompletedTask;
    }

    private sealed class Consumer : IHostedService
    {
        public int Starts { get; private set; }
        public int Stops { get; private set; }

        public Task StartAsync(CancellationToken ct)
        {
            Starts++;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken ct)
        {
            Stops++;
            return Task.CompletedTask;
        }
    }

    private sealed class Environment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "gate-test";
        public string ContentRootPath { get; set; } = "";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
