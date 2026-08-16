using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Shouldly;
using Travel.Host.Configuration;
using Xunit;

namespace Travel.Host.Tests.Integration.Configuration;

public sealed class HostConnectionOptionsTests
{
    private const string PasswordSentinel = "database-password-secret-sentinel";

    public static TheoryData<string, string?> InvalidProductionConnections =>
        new()
        {
            { "ConnectionStrings:travel", null },
            {
                "ConnectionStrings:travel",
                $"Host=localhost;Database=travel;Password={PasswordSentinel}"
            },
            {
                "ConnectionStrings:travel",
                $"Host=127.0.0.1;Database=travel;Password={PasswordSentinel}"
            },
            {
                "ConnectionStrings:travel",
                $"Host=LOCALHOST.;Database=travel;Password={PasswordSentinel}"
            },
            { "ConnectionStrings:nats", null },
            { "ConnectionStrings:nats", "nats://localhost:4222" },
            { "ConnectionStrings:nats", "nats://127.0.0.1:4222" },
            { "ConnectionStrings:nats", "nats://LOCALHOST.:4222" },
            { "ConnectionStrings:redis", null },
            { "ConnectionStrings:redis", "localhost:6379" },
            { "ConnectionStrings:redis", "127.0.0.1:6379" },
            { "ConnectionStrings:redis", "LOCALHOST.:6379" },
        };

    [Theory]
    [MemberData(nameof(InvalidProductionConnections))]
    public void Production_rejects_missing_or_local_connections(string key, string? value)
    {
        var services = BuildServices(Environments.Production, (key, value));

        var error = Should.Throw<OptionsValidationException>(() => ValidateOnStart(services));

        error.Message.ShouldNotContain(PasswordSentinel);
    }

    [Fact]
    public void Development_accepts_explicit_local_connections()
    {
        var services = BuildServices(
            Environments.Development,
            ("ConnectionStrings:travel", "Host=localhost;Database=travel"),
            ("ConnectionStrings:nats", "nats://localhost:4222"),
            ("ConnectionStrings:redis", "localhost:6379")
        );

        Should.NotThrow(() => ValidateOnStart(services));
    }

    private static IServiceCollection BuildServices(
        string environmentName,
        params (string Key, string? Value)[] overrides
    )
    {
        var values = new Dictionary<string, string?>
        {
            ["ConnectionStrings:travel"] =
                $"Host=postgres.internal;Database=travel;Password={PasswordSentinel}",
            ["ConnectionStrings:nats"] = "nats://nats.internal:4222",
            ["ConnectionStrings:redis"] = "redis.internal:6379",
        };
        foreach (var (key, value) in overrides)
        {
            if (value is null)
                values.Remove(key);
            else
                values[key] = value;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.AddHostConnectionOptions(
            configuration,
            new OptionsHostEnvironment { EnvironmentName = environmentName }
        );
        return services;
    }

    private static void ValidateOnStart(IServiceCollection services)
    {
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IStartupValidator>().Validate();
    }
}

file sealed class OptionsHostEnvironment : IHostEnvironment
{
    public string EnvironmentName { get; set; } = Environments.Development;
    public string ApplicationName { get; set; } = "Travel.Host.Tests.Integration";
    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
