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

namespace Travel.AI.Tests.Health;

[Collection("AI health listener")]
public sealed class HealthEndpointContractTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(15);

    [Theory]
    [InlineData("Development", 5159)]
    [InlineData("Testing", 5159)]
    [InlineData("Production", 0)]
    public async Task Dedicated_listener_exposes_only_explicit_health_contract(
        string environment,
        int expectedPort
    )
    {
        var internalPort = expectedPort == 0 ? GetAvailablePort() : expectedPort;
        await using var factory = new AiHealthFactory(environment, internalPort);
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
        await using var factory = new AiHealthFactory("Testing", 5159);
        factory.UseKestrel();
        using var client = factory.CreateClient();

        var registrations = factory
            .Services.GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value.Registrations.ToDictionary(registration => registration.Name);

        registrations["postgres"].Tags.ShouldBe(["ready"]);
        registrations["nats"].Tags.ShouldBe(["ready"]);
        registrations["initialization"].Tags.ShouldBe(["ready"]);
        registrations.Values.ShouldAllBe(registration => !registration.Tags.Contains("dependency"));
    }

    [Theory]
    [InlineData("initialization-failed")]
    [InlineData("required-local-failed")]
    public async Task Initialization_or_required_local_failure_closes_ready_but_not_live(
        string failure
    )
    {
        await using var factory = new AiHealthFactory("Testing", 5159, healthFailure: failure);
        factory.UseKestrel();
        using var startupClient = factory.CreateClient();
        using var internalClient = new HttpClient
        {
            BaseAddress = new Uri("http://127.0.0.1:5159"),
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
        await using var factory = new AiHealthFactory(
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
        await using var factory = new AiHealthFactory(
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
        await using var factory = new AiHealthFactory(
            Environments.Production,
            port,
            port.ToString(),
            source == "wildcard" ? $"http://*:{port}" : null,
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
    public async Task Production_rejects_kestrel_endpoint_collision_before_binding()
    {
        var port = GetAvailablePort();
        await using var factory = new AiHealthFactory(
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
        await using var factory = new AiHealthFactory(
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
        await using var factory = new AiHealthFactory(
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

    private sealed class AiHealthFactory(
        string environment,
        int? internalPort,
        string? rawInternalPort = null,
        string? publicUrls = null,
        string? httpPorts = null,
        string? healthFailure = null,
        bool omitPublicUrls = false,
        string? kestrelPublicUrl = null
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
                {
                    Replace(options, "postgres", HealthStatus.Healthy);
                    Replace(options, "nats", HealthStatus.Healthy);
                    if (healthFailure == "initialization-failed")
                        Replace(options, "initialization", HealthStatus.Unhealthy);
                    else if (healthFailure == "required-local-failed")
                    {
                        Replace(options, "initialization", HealthStatus.Healthy);
                        Replace(options, "nats", HealthStatus.Unhealthy);
                    }
                })
            );

            var production = string.Equals(
                environment,
                Environments.Production,
                StringComparison.Ordinal
            );
            var host = production ? "service.example.invalid" : "127.0.0.1";
            builder.UseSetting(
                "ConnectionStrings:travel",
                $"Host={host};Port=1;Database=travel;Password=health-contract-secret;Timeout=1"
            );
            builder.UseSetting("ConnectionStrings:nats", $"nats://{host}:1");
            builder.UseSetting("Anthropic:ApiKey", "health-contract-secret");
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
}

[CollectionDefinition("AI health listener", DisableParallelization = true)]
public sealed class AiHealthListenerCollection;
