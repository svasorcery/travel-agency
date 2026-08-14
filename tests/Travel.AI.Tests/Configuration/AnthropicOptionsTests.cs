using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Shouldly;
using Travel.AI.Configuration;
using Xunit;

namespace Travel.AI.Tests.Configuration;

public sealed class AnthropicOptionsTests
{
    private const string ApiKeySentinel = "anthropic-api-key-secret-sentinel";

    public static TheoryData<string, string?> InvalidProductionConnections =>
        new()
        {
            { "ConnectionStrings:travel", null },
            { "ConnectionStrings:travel", "Host=localhost;Database=travel" },
            { "ConnectionStrings:travel", "Host=127.0.0.1;Database=travel" },
            { "ConnectionStrings:travel", "Host=LOCALHOST.;Database=travel" },
            { "ConnectionStrings:nats", null },
            { "ConnectionStrings:nats", "nats://localhost:4222" },
            { "ConnectionStrings:nats", "nats://127.0.0.1:4222" },
            { "ConnectionStrings:nats", "nats://LOCALHOST.:4222" },
        };

    [Fact]
    public void Production_requires_anthropic_api_key()
    {
        var services = BuildServices(Environments.Production, ("Anthropic:ApiKey", null));

        Should.Throw<OptionsValidationException>(() => ValidateOnStart(services));
    }

    [Theory]
    [MemberData(nameof(InvalidProductionConnections))]
    public void Production_rejects_missing_or_local_connections(string key, string? value)
    {
        var services = BuildServices(Environments.Production, (key, value));

        var error = Should.Throw<OptionsValidationException>(() => ValidateOnStart(services));

        error.Message.ShouldNotContain(ApiKeySentinel);
    }

    [Fact]
    public void Development_accepts_local_connections_without_anthropic_key()
    {
        var services = BuildServices(
            Environments.Development,
            ("Anthropic:ApiKey", null),
            ("ConnectionStrings:travel", "Host=localhost;Database=travel"),
            ("ConnectionStrings:nats", "nats://localhost:4222")
        );

        Should.NotThrow(() => ValidateOnStart(services));
    }

    [Fact]
    public void Valid_production_configuration_binds_typed_anthropic_options()
    {
        var services = BuildServices(Environments.Production);

        Should.NotThrow(() => ValidateOnStart(services));
        using var provider = services.BuildServiceProvider();
        provider
            .GetRequiredService<IOptions<AnthropicOptions>>()
            .Value.ApiKey.ShouldBe(ApiKeySentinel);
    }

    private static IServiceCollection BuildServices(
        string environmentName,
        params (string Key, string? Value)[] overrides
    )
    {
        var values = new Dictionary<string, string?>
        {
            ["Anthropic:ApiKey"] = ApiKeySentinel,
            ["ConnectionStrings:travel"] = "Host=postgres.internal;Database=travel;Username=travel",
            ["ConnectionStrings:nats"] = "nats://nats.internal:4222",
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
        services.AddTravelAiOptions(
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
    public string ApplicationName { get; set; } = "Travel.AI.Tests";
    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
