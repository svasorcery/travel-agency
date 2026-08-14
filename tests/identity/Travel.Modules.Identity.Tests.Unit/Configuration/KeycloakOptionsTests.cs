using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Shouldly;
using Travel.Modules.Identity.Infrastructure;
using Xunit;

namespace Travel.Modules.Identity.Tests.Unit.Configuration;

public sealed class KeycloakOptionsTests
{
    public static TheoryData<string, string?> InvalidProductionSettings =>
        new()
        {
            { "Keycloak:Authority", null },
            { "Keycloak:Authority", "realms/travel" },
            { "Keycloak:Authority", "http://identity.example/realms/travel" },
            { "Keycloak:Authority", "https://localhost:8180/realms/travel" },
            { "Keycloak:Authority", "https://127.0.0.1:8180/realms/travel" },
            { "Keycloak:Authority", "https://LOCALHOST.:8180/realms/travel" },
            { "Keycloak:Audience", null },
            { "Keycloak:Audience", "   " },
        };

    [Theory]
    [MemberData(nameof(InvalidProductionSettings))]
    public void Production_rejects_invalid_authority_or_audience(string key, string? value)
    {
        var services = BuildServices(Environments.Production, (key, value));

        Should.Throw<OptionsValidationException>(() => ValidateOnStart(services));
    }

    [Fact]
    public void Development_accepts_explicit_localhost_authority()
    {
        var services = BuildServices(
            Environments.Development,
            ("Keycloak:Authority", "http://localhost:8180/realms/travel")
        );

        Should.NotThrow(() => ValidateOnStart(services));
        using var provider = services.BuildServiceProvider();
        var keycloak = provider.GetRequiredService<IOptions<KeycloakOptions>>().Value;
        keycloak.Authority.ShouldBe("http://localhost:8180/realms/travel");

        var jwt = provider
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);
        jwt.Authority.ShouldBe(keycloak.Authority);
        jwt.Audience.ShouldBe("travel-web");
        jwt.RequireHttpsMetadata.ShouldBeFalse();
    }

    [Fact]
    public void Production_valid_configuration_enables_https_metadata()
    {
        var services = BuildServices(Environments.Production);

        Should.NotThrow(() => ValidateOnStart(services));
        using var provider = services.BuildServiceProvider();
        var jwt = provider
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);
        jwt.Authority.ShouldBe("https://identity.example/realms/travel");
        jwt.Audience.ShouldBe("travel-web");
        jwt.RequireHttpsMetadata.ShouldBeTrue();
    }

    private static IServiceCollection BuildServices(
        string environmentName,
        params (string Key, string? Value)[] overrides
    )
    {
        var values = new Dictionary<string, string?>
        {
            ["Keycloak:Authority"] = "https://identity.example/realms/travel",
            ["Keycloak:Audience"] = "travel-web",
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
        services.AddLogging();
        services.AddIdentityModule(
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

file sealed class OptionsHostEnvironment : IWebHostEnvironment
{
    public string EnvironmentName { get; set; } = Environments.Development;
    public string ApplicationName { get; set; } = "Travel.Modules.Identity.Tests.Unit";
    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    public string WebRootPath { get; set; } = AppContext.BaseDirectory;
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
}
