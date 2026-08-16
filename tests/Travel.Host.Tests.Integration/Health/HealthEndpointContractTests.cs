using System.Net;
using Alba;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shouldly;
using Travel.ServiceDefaults.Health;
using Travel.Shared.Infrastructure.Initialization;
using Wolverine;
using Xunit;

namespace Travel.Host.Tests.Integration.Health;

[Collection("Host health listener")]
public sealed class HealthEndpointContractTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(15);

    [Theory]
    [InlineData("Development", 5098)]
    [InlineData("Testing", 5098)]
    [InlineData("Production", 0)]
    public async Task Dedicated_listener_exposes_only_explicit_health_contract(
        string environment,
        int expectedPort
    )
    {
        var internalPort = expectedPort == 0 ? GetAvailablePort() : expectedPort;
        await using var factory = new HostHealthFactory(environment, internalPort);
        factory.UseKestrel();

        using var startupClient = factory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false }
        );
        using var publicClient = CreatePublicClient(factory, internalPort);
        using var internalClient = new HttpClient
        {
            BaseAddress = new Uri($"http://127.0.0.1:{internalPort}"),
            Timeout = TestTimeout,
        };

        var live = await internalClient.GetAsync(
            "/health/live",
            TestContext.Current.CancellationToken
        );
        var ready = await internalClient.GetAsync(
            "/health/ready",
            TestContext.Current.CancellationToken
        );
        var dependencies = await internalClient.GetAsync(
            "/health/dependencies",
            TestContext.Current.CancellationToken
        );

        live.StatusCode.ShouldBe(HttpStatusCode.OK);
        ready.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        dependencies.StatusCode.ShouldBe(HttpStatusCode.OK);
        (
            await dependencies.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)
        ).ShouldContain("duffel");

        foreach (var path in new[] { "/health/live", "/health/ready", "/health/dependencies" })
        {
            var publicRequest = new HttpRequestMessage(HttpMethod.Get, path);
            publicRequest.Headers.Host = $"127.0.0.1:{internalPort}";
            var publicResponse = await publicClient.SendAsync(
                publicRequest,
                TestContext.Current.CancellationToken
            );
            publicResponse.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }

        factory
            .Services.GetRequiredService<IOptions<HealthEndpointOptions>>()
            .Value.InternalPort.ShouldBe(internalPort);
    }

    [Fact]
    public async Task Readiness_contains_all_required_local_dependencies_and_no_provider()
    {
        await using var factory = new HostHealthFactory("Testing", 5098);
        factory.UseKestrel();
        using var client = factory.CreateClient();

        var registrations = factory
            .Services.GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value.Registrations.ToDictionary(registration => registration.Name);

        registrations["postgres"].Tags.ShouldBe(["ready"]);
        registrations["nats"].Tags.ShouldBe(["ready"]);
        registrations["redis"].Tags.ShouldBe(["ready"]);
        registrations["initialization"].Tags.ShouldBe(["ready"]);
        registrations["duffel"].Tags.ShouldBe(["dependency"]);
        registrations.ShouldNotContainKey("travelpayouts");
    }

    [Fact]
    public async Task Provider_degradation_is_diagnostic_and_does_not_close_readiness()
    {
        await using var factory = new HostHealthFactory(
            "Testing",
            5098,
            healthScenario: HealthScenario.ProviderDegraded
        );
        factory.UseKestrel();
        using var publicClient = factory.CreateClient();
        using var internalClient = new HttpClient
        {
            BaseAddress = new Uri("http://127.0.0.1:5098"),
            Timeout = TestTimeout,
        };

        var ready = await internalClient.GetAsync(
            "/health/ready",
            TestContext.Current.CancellationToken
        );
        var dependencies = await internalClient.GetAsync(
            "/health/dependencies",
            TestContext.Current.CancellationToken
        );
        var dependenciesBody = await dependencies.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken
        );

        ready.StatusCode.ShouldBe(HttpStatusCode.OK);
        dependencies.StatusCode.ShouldBe(HttpStatusCode.OK);
        dependenciesBody.ShouldContain("duffel");
        dependenciesBody.ShouldContain("Degraded");
    }

    [Theory]
    [InlineData("initialization-failed")]
    [InlineData("required-local-failed")]
    public async Task Initialization_or_required_local_failure_closes_ready_but_not_live(
        string failure
    )
    {
        var scenario =
            failure == "initialization-failed"
                ? HealthScenario.InitializationFailed
                : HealthScenario.LocalDependencyUnhealthy;
        await using var factory = new HostHealthFactory("Testing", 5098, healthScenario: scenario);
        factory.UseKestrel();
        using var startupClient = factory.CreateClient();
        using var internalClient = new HttpClient
        {
            BaseAddress = new Uri("http://127.0.0.1:5098"),
            Timeout = TestTimeout,
        };

        var live = await internalClient.GetAsync(
            "/health/live",
            TestContext.Current.CancellationToken
        );
        var ready = await internalClient.GetAsync(
            "/health/ready",
            TestContext.Current.CancellationToken
        );

        live.StatusCode.ShouldBe(HttpStatusCode.OK);
        ready.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("0")]
    [InlineData("65536")]
    [InlineData("not-a-port")]
    public async Task Production_rejects_missing_or_invalid_internal_port(string? value)
    {
        await using var factory = new HostHealthFactory(
            Environments.Production,
            value is null ? null
                : int.TryParse(value, out var port) ? port
                : -1,
            value
        );
        factory.UseKestrel(0);

        var exception = await Should.ThrowAsync<Exception>(() =>
            Task.Run(() => factory.CreateClient()).WaitAsync(TestTimeout)
        );

        exception.GetBaseException().ShouldBeOfType<OptionsValidationException>();
        exception.Message.ShouldContain("HealthEndpoints:InternalPort");
    }

    [Fact]
    public async Task Production_rejects_internal_port_colliding_with_public_listener()
    {
        var port = GetAvailablePort();
        await using var factory = new HostHealthFactory(
            Environments.Production,
            port,
            port.ToString(),
            $"http://127.0.0.1:{port}"
        );
        factory.UseKestrel(port);

        var exception = await Should.ThrowAsync<Exception>(() =>
            Task.Run(() => factory.CreateClient()).WaitAsync(TestTimeout)
        );

        exception.GetBaseException().ShouldBeOfType<OptionsValidationException>();
        exception.Message.ShouldContain("must not collide");
    }

    [Theory]
    [InlineData("wildcard")]
    [InlineData("http-ports")]
    public async Task Production_rejects_normal_container_public_port_collisions(string source)
    {
        var port = GetAvailablePort();
        await using var factory = new HostHealthFactory(
            Environments.Production,
            port,
            port.ToString(),
            source == "wildcard" ? $"http://+:{port}" : null,
            httpPorts: source == "http-ports" ? port.ToString() : null,
            omitPublicUrls: source == "http-ports"
        );
        factory.UseKestrel(port);

        var exception = await Should.ThrowAsync<Exception>(() =>
            Task.Run(() => factory.CreateClient()).WaitAsync(TestTimeout)
        );

        exception.GetBaseException().ShouldBeOfType<OptionsValidationException>();
        exception.Message.ShouldContain("must not collide");
    }

    [Fact]
    public async Task Production_rejects_collision_with_implicit_kestrel_default_port()
    {
        await using var factory = new HostHealthFactory(
            Environments.Production,
            5000,
            "5000",
            omitPublicUrls: true
        );
        factory.UseKestrel(5000);

        var exception = await Should.ThrowAsync<Exception>(() =>
            Task.Run(() => factory.CreateClient()).WaitAsync(TestTimeout)
        );

        exception.GetBaseException().ShouldBeOfType<OptionsValidationException>();
        exception.Message.ShouldContain("must not collide");
    }

    [Fact]
    public async Task Production_rejects_kestrel_endpoint_collision_before_binding()
    {
        var port = GetAvailablePort();
        await using var factory = new HostHealthFactory(
            Environments.Production,
            port,
            port.ToString(),
            kestrelPublicUrl: $"HTTP://127.0.0.1:{port}",
            omitPublicUrls: true
        );
        factory.UseKestrel();

        var exception = await Should.ThrowAsync<Exception>(() =>
            Task.Run(() => factory.CreateClient()).WaitAsync(TestTimeout)
        );

        exception.GetBaseException().ShouldBeOfType<OptionsValidationException>();
        exception.Message.ShouldContain("must not collide");
        exception.Message.ShouldNotContain(port.ToString());
        exception.Message.ShouldNotContain("127.0.0.1");
    }

    [Fact]
    public async Task Production_kestrel_endpoint_coexists_with_production_internal_listener()
    {
        var publicPort = GetAvailablePort();
        var internalPort = GetAvailablePort();
        await using var factory = new HostHealthFactory(
            Environments.Production,
            internalPort,
            internalPort.ToString(),
            publicUrls: $"http://127.0.0.1:{internalPort}",
            httpPorts: internalPort.ToString(),
            kestrelPublicUrl: $"http://127.0.0.1:{publicPort}"
        );
        factory.UseKestrel();

        using var startupClient = factory.CreateClient();
        using var publicClient = new HttpClient
        {
            BaseAddress = new Uri($"http://127.0.0.1:{publicPort}"),
            Timeout = TestTimeout,
        };
        using var internalClient = new HttpClient
        {
            BaseAddress = new Uri($"http://127.0.0.1:{internalPort}"),
            Timeout = TestTimeout,
        };

        var spoofedPublicRequest = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        spoofedPublicRequest.Headers.Host = $"127.0.0.1:{internalPort}";
        var publicResponse = await publicClient.SendAsync(
            spoofedPublicRequest,
            TestContext.Current.CancellationToken
        );
        var internalResponse = await internalClient.GetAsync(
            "/health/live",
            TestContext.Current.CancellationToken
        );

        publicResponse.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        internalResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Urls_override_http_ports_without_activating_ignored_binding()
    {
        var internalPort = GetAvailablePort();
        await using var factory = new HostHealthFactory(
            Environments.Production,
            internalPort,
            internalPort.ToString(),
            "  HTTP://127.0.0.1:0  ",
            httpPorts: internalPort.ToString()
        );
        factory.UseKestrel();

        using var startupClient = factory.CreateClient();
        using var internalClient = new HttpClient
        {
            BaseAddress = new Uri($"http://127.0.0.1:{internalPort}"),
            Timeout = TestTimeout,
        };

        var internalResponse = await internalClient.GetAsync(
            "/health/live",
            TestContext.Current.CancellationToken
        );

        internalResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("redis.internal:6379,abortConnect=false")]
    [InlineData("redis.internal:6379,abortConnect=false,connectTimeout=1000")]
    public async Task Redis_health_registration_accepts_runtime_connection_syntax(
        string connectionString
    )
    {
        await using var factory = new HostHealthFactory(
            "Testing",
            5098,
            healthScenario: HealthScenario.RedisRuntimeSyntax,
            redisConnectionString: connectionString
        );

        var registration = factory
            .Services.GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value.Registrations.Single(candidate => candidate.Name == "redis");
        var check = registration.Factory(factory.Services);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(() =>
            check.CheckHealthAsync(
                new HealthCheckContext { Registration = registration },
                cancellation.Token
            )
        );
    }

    [Fact]
    public async Task Redis_health_registration_uses_configured_first_endpoint_port()
    {
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        try
        {
            await using var factory = new HostHealthFactory(
                "Testing",
                5098,
                healthScenario: HealthScenario.RedisRuntimeSyntax,
                redisConnectionString: $"127.0.0.1:{port},abortConnect=false,connectTimeout=1000"
            );

            var registration = factory
                .Services.GetRequiredService<IOptions<HealthCheckServiceOptions>>()
                .Value.Registrations.Single(candidate => candidate.Name == "redis");
            var check = registration.Factory(factory.Services);

            var result = await check.CheckHealthAsync(
                new HealthCheckContext { Registration = registration },
                TestContext.Current.CancellationToken
            );

            result.Status.ShouldBe(HealthStatus.Healthy);
        }
        finally
        {
            listener.Stop();
        }
    }

    private static int GetAvailablePort()
    {
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static HttpClient CreatePublicClient(
        WebApplicationFactory<Program> factory,
        int internalPort
    )
    {
        var address = factory
            .Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses.Single(value => new Uri(value).Port != internalPort);
        return new HttpClient { BaseAddress = new Uri(address), Timeout = TestTimeout };
    }

    private sealed class HostHealthFactory(
        string environment,
        int? internalPort,
        string? rawInternalPort = null,
        string? publicUrls = null,
        HealthScenario healthScenario = HealthScenario.InitializationPending,
        string? httpPorts = null,
        bool omitPublicUrls = false,
        string? kestrelPublicUrl = null,
        string? redisConnectionString = null
    ) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(environment);
            builder.UseSetting("OTEL_EXPORTER_OTLP_ENDPOINT", string.Empty);
            if (!omitPublicUrls)
                builder.UseSetting("urls", publicUrls ?? "http://127.0.0.1:0");
            if (httpPorts is not null)
                builder.UseSetting("HTTP_PORTS", httpPorts);
            if (kestrelPublicUrl is not null)
                builder.UseSetting("Kestrel:Endpoints:Public:Url", kestrelPublicUrl);
            builder.UseSetting(
                "HealthEndpoints:InternalPort",
                rawInternalPort ?? internalPort?.ToString()
            );
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureServices(DisableExternalRuntime);
            builder.ConfigureServices(services =>
                services.PostConfigure<HealthCheckServiceOptions>(options =>
                    ConfigureHealthScenario(options, healthScenario)
                )
            );

            var production = string.Equals(
                environment,
                Environments.Production,
                StringComparison.Ordinal
            );
            var development = string.Equals(
                environment,
                Environments.Development,
                StringComparison.Ordinal
            );
            var localHost = production ? "service.example.invalid" : "127.0.0.1";
            var dependencyHost = production ? "dependency.example.invalid" : "127.0.0.1";
            builder.UseSetting(
                "ConnectionStrings:travel",
                $"Host={localHost};Port=1;Database=travel;Password=health-contract-secret;Timeout=1"
            );
            builder.UseSetting("ConnectionStrings:nats", $"nats://{localHost}:1");
            builder.UseSetting(
                "ConnectionStrings:redis",
                redisConnectionString ?? $"{localHost}:1"
            );
            builder.UseSetting(
                "Keycloak:Authority",
                $"{(development ? "http" : "https")}://{dependencyHost}:1/realms/travel"
            );
            builder.UseSetting("Keycloak:Audience", "travel-web");
            builder.UseSetting("Flights:FeatureFlags:Travelpayouts:Enabled", "false");
            builder.UseSetting(
                "Flights:Duffel:BaseUrl",
                $"{(production ? "https" : "http")}://{dependencyHost}:1"
            );
            builder.UseSetting("Flights:Duffel:ApiKey", "health-contract-secret");
            builder.UseSetting("Flights:Duffel:WebhookSecret", "health-contract-secret");
            builder.UseSetting("Flights:Duffel:TimeoutSeconds", "1");
            builder.UseSetting("Flights:Duffel:SearchTimeoutSeconds", "1");
            builder.UseSetting("Flights:Smtp:Host", dependencyHost);
            builder.UseSetting("Flights:Smtp:Port", "1");
            builder.UseSetting("Flights:Smtp:FromAddress", "health@travel.example");
            builder.UseSetting(
                "Flights:Providers:Frankfurter:BaseAddress",
                $"https://{dependencyHost}:1/"
            );
            builder.UseSetting("Flights:Providers:Frankfurter:TimeoutSeconds", "1");
        }

        private static void DisableExternalRuntime(IServiceCollection services)
        {
            services.DisableAllExternalWolverineTransports();
            RemoveHostedService(
                services,
                descriptor =>
                    descriptor.ImplementationFactory?.Method.ReturnType == typeof(AppInitializer)
            );
            RemoveHostedService(
                services,
                descriptor =>
                    descriptor.ImplementationFactory?.Method.DeclaringType?.DeclaringType
                    == typeof(Wolverine.HostBuilderExtensions)
            );
        }

        private static void RemoveHostedService(
            IServiceCollection services,
            Func<ServiceDescriptor, bool> matches
        )
        {
            var descriptor = services.Single(service =>
                service.ServiceType == typeof(IHostedService) && matches(service)
            );
            services.Remove(descriptor).ShouldBeTrue();
        }

        private static void ConfigureHealthScenario(
            HealthCheckServiceOptions options,
            HealthScenario scenario
        )
        {
            Replace(options, "postgres", HealthStatus.Healthy);
            Replace(options, "nats", HealthStatus.Healthy);
            if (scenario != HealthScenario.RedisRuntimeSyntax)
                Replace(options, "redis", HealthStatus.Healthy);
            Replace(options, "duffel", HealthStatus.Degraded);

            if (scenario == HealthScenario.ProviderDegraded)
            {
                Replace(options, "initialization", HealthStatus.Healthy);
            }
            else if (scenario == HealthScenario.InitializationFailed)
            {
                Replace(options, "initialization", HealthStatus.Unhealthy);
            }
            else if (scenario == HealthScenario.LocalDependencyUnhealthy)
            {
                Replace(options, "initialization", HealthStatus.Healthy);
                Replace(options, "redis", HealthStatus.Unhealthy);
            }
        }

        private static void Replace(
            HealthCheckServiceOptions options,
            string name,
            HealthStatus status
        )
        {
            var existing = options.Registrations.Single(registration => registration.Name == name);
            options.Registrations.Remove(existing);
            options.Registrations.Add(
                new HealthCheckRegistration(
                    name,
                    new FixedHealthCheck(status),
                    status,
                    existing.Tags
                )
            );
        }
    }

    private sealed class FixedHealthCheck(HealthStatus status) : IHealthCheck
    {
        public Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default
        ) =>
            Task.FromResult(
                status switch
                {
                    HealthStatus.Healthy => HealthCheckResult.Healthy(),
                    HealthStatus.Degraded => HealthCheckResult.Degraded(),
                    _ => HealthCheckResult.Unhealthy(),
                }
            );
    }

    private enum HealthScenario
    {
        InitializationPending,
        ProviderDegraded,
        InitializationFailed,
        LocalDependencyUnhealthy,
        RedisRuntimeSyntax,
    }
}

[CollectionDefinition("Host health listener", DisableParallelization = true)]
public sealed class HostHealthListenerCollection;
