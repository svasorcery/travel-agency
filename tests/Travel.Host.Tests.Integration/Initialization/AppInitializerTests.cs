using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Travel.Shared.Infrastructure.Initialization;
using Xunit;

namespace Travel.Host.Tests.Integration.Initialization;

public sealed class AppInitializerTests
{
    [Fact]
    public async Task StartAsync_runs_initializers_by_phase_then_type_name_and_records_success()
    {
        var probe = new InitializationProbe();
        await using var services = BuildServices(
            Environments.Development,
            probe,
            register =>
            {
                register.AddInitializer<PlatformZuluInitializer>();
                register.AddInitializer<DevelopmentSeedInitializer>();
                register.AddInitializer<EventStoreInitializer>();
                register.AddInitializer<PlatformAlphaInitializer>();
                register.AddInitializer<RelationalSchemaInitializer>();
            }
        );
        var initializer = services.GetRequiredService<AppInitializer>();

        initializer.State.ShouldBe(InitializationState.Pending);
        initializer.Initializers.ShouldBeEmpty();

        await initializer.StartAsync(TestContext.Current.CancellationToken);

        probe.Executed.ShouldBe([
            nameof(PlatformAlphaInitializer),
            nameof(PlatformZuluInitializer),
            nameof(RelationalSchemaInitializer),
            nameof(EventStoreInitializer),
            nameof(DevelopmentSeedInitializer),
        ]);
        initializer.State.ShouldBe(InitializationState.Succeeded);
        initializer
            .Initializers.Select(entry => entry.State)
            .ShouldAllBe(state => state == InitializationState.Succeeded);
    }

    [Fact]
    public async Task StartAsync_marks_the_current_initializer_running_before_completion()
    {
        var probe = new InitializationProbe();
        await using var services = BuildServices(
            Environments.Development,
            probe,
            register => register.AddInitializer<BlockingPlatformInitializer>()
        );
        var initializer = services.GetRequiredService<AppInitializer>();

        var start = initializer.StartAsync(TestContext.Current.CancellationToken);
        await probe.Started.Task.WaitAsync(TestContext.Current.CancellationToken);

        initializer.State.ShouldBe(InitializationState.Running);
        initializer.Initializers.ShouldHaveSingleItem().State.ShouldBe(InitializationState.Running);

        probe.Release.SetResult();
        await start;
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    public async Task StartAsync_outside_production_fails_startup_and_records_a_secret_safe_failure(
        string environmentName
    )
    {
        const string secret = "password=super-secret";
        var probe = new InitializationProbe { Failure = new InvalidOperationException(secret) };
        await using var services = BuildServices(
            environmentName,
            probe,
            register => register.AddInitializer<FailingPlatformInitializer>()
        );
        var initializer = services.GetRequiredService<AppInitializer>();

        await Should.ThrowAsync<InvalidOperationException>(() =>
            initializer.StartAsync(TestContext.Current.CancellationToken)
        );

        initializer.State.ShouldBe(InitializationState.Failed);
        var failed = initializer.Initializers.ShouldHaveSingleItem();
        failed.State.ShouldBe(InitializationState.Failed);
        failed.ErrorType.ShouldBe(nameof(InvalidOperationException));
        failed.ErrorType!.ShouldNotContain(secret);
    }

    [Fact]
    public async Task StartAsync_in_production_records_failure_stays_live_and_keeps_readiness_unhealthy()
    {
        const string secret = "connection-string=super-secret";
        var probe = new InitializationProbe { Failure = new InvalidOperationException(secret) };
        await using var services = BuildServices(
            Environments.Production,
            probe,
            register => register.AddInitializer<FailingPlatformInitializer>()
        );
        var initializer = services.GetRequiredService<AppInitializer>();

        await initializer.StartAsync(TestContext.Current.CancellationToken);
        var health = await services
            .GetRequiredService<HealthCheckService>()
            .CheckHealthAsync(
                registration => registration.Tags.Contains(InitializationHealthCheck.ReadinessTag),
                TestContext.Current.CancellationToken
            );

        initializer.State.ShouldBe(InitializationState.Failed);
        health.Status.ShouldBe(HealthStatus.Unhealthy);
        health.Entries.ShouldContainKey(InitializationHealthCheck.Name);
        health.Entries[InitializationHealthCheck.Name].Description!.ShouldNotContain(secret);
    }

    [Fact]
    public async Task StartAsync_records_cancellation_and_stops_startup_in_every_environment()
    {
        var probe = new InitializationProbe();
        await using var services = BuildServices(
            Environments.Production,
            probe,
            register => register.AddInitializer<CancelledPlatformInitializer>()
        );
        var initializer = services.GetRequiredService<AppInitializer>();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(() =>
            initializer.StartAsync(cancellation.Token)
        );

        initializer.State.ShouldBe(InitializationState.Cancelled);
        initializer
            .Initializers.ShouldHaveSingleItem()
            .State.ShouldBe(InitializationState.Cancelled);
    }

    [Fact]
    public async Task Readiness_is_healthy_when_no_initializers_are_registered()
    {
        var probe = new InitializationProbe();
        await using var services = BuildServices(Environments.Production, probe, _ => { });
        var initializer = services.GetRequiredService<AppInitializer>();

        await initializer.StartAsync(TestContext.Current.CancellationToken);
        var health = await services
            .GetRequiredService<HealthCheckService>()
            .CheckHealthAsync(
                registration => registration.Tags.Contains(InitializationHealthCheck.ReadinessTag),
                TestContext.Current.CancellationToken
            );

        initializer.State.ShouldBe(InitializationState.Succeeded);
        initializer.Initializers.ShouldBeEmpty();
        health.Status.ShouldBe(HealthStatus.Healthy);
    }

    private static ServiceProvider BuildServices(
        string environmentName,
        InitializationProbe probe,
        Action<IServiceCollection> registerInitializers
    )
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment(environmentName));
        services.AddSingleton(probe);
        services.AddAppInitialization();
        registerInitializers(services);
        return services.BuildServiceProvider();
    }

    private sealed class InitializationProbe
    {
        public List<string> Executed { get; } = [];
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Exception? Failure { get; init; }
    }

    private abstract class RecordingInitializer(InitializationProbe probe) : IInitializer
    {
        protected InitializationProbe Probe { get; } = probe;
        public abstract InitializationPhase Phase { get; }

        public virtual Task InitializeAsync(CancellationToken ct)
        {
            Probe.Executed.Add(GetType().Name);
            return Task.CompletedTask;
        }
    }

    private sealed class PlatformAlphaInitializer(InitializationProbe probe)
        : RecordingInitializer(probe)
    {
        public override InitializationPhase Phase => InitializationPhase.Platform;
    }

    private sealed class PlatformZuluInitializer(InitializationProbe probe)
        : RecordingInitializer(probe)
    {
        public override InitializationPhase Phase => InitializationPhase.Platform;
    }

    private sealed class RelationalSchemaInitializer(InitializationProbe probe)
        : RecordingInitializer(probe)
    {
        public override InitializationPhase Phase => InitializationPhase.RelationalSchema;
    }

    private sealed class EventStoreInitializer(InitializationProbe probe)
        : RecordingInitializer(probe)
    {
        public override InitializationPhase Phase => InitializationPhase.EventStoreSchema;
    }

    private sealed class DevelopmentSeedInitializer(InitializationProbe probe)
        : RecordingInitializer(probe)
    {
        public override InitializationPhase Phase => InitializationPhase.DevelopmentSeed;
    }

    private sealed class BlockingPlatformInitializer(InitializationProbe probe)
        : RecordingInitializer(probe)
    {
        public override InitializationPhase Phase => InitializationPhase.Platform;

        public override async Task InitializeAsync(CancellationToken ct)
        {
            Probe.Started.SetResult();
            await Probe.Release.Task.WaitAsync(ct);
            Probe.Executed.Add(GetType().Name);
        }
    }

    private sealed class FailingPlatformInitializer(InitializationProbe probe)
        : RecordingInitializer(probe)
    {
        public override InitializationPhase Phase => InitializationPhase.Platform;

        public override Task InitializeAsync(CancellationToken ct) =>
            Task.FromException(Probe.Failure!);
    }

    private sealed class CancelledPlatformInitializer(InitializationProbe probe)
        : RecordingInitializer(probe)
    {
        public override InitializationPhase Phase => InitializationPhase.Platform;

        public override Task InitializeAsync(CancellationToken ct) => Task.FromCanceled(ct);
    }

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Travel.Initialization.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            null!;
    }
}
