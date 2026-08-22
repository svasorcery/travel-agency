using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Shouldly;
using Travel.Modules.Identity.Api.Composition;
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
        var builder = BuildBuilder(Environments.Production, (key, value));

        Should.Throw<OptionsValidationException>(() => ValidateOnStart(builder));
    }

    [Fact]
    public void Development_accepts_explicit_localhost_authority()
    {
        var builder = BuildBuilder(
            Environments.Development,
            ("Keycloak:Authority", "http://localhost:8180/realms/travel")
        );

        Should.NotThrow(() => ValidateOnStart(builder));
        using var provider = builder.Services.BuildServiceProvider();
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
    public void Jwt_bearer_keeps_raw_claim_names_for_canonical_transformation()
    {
        var builder = BuildBuilder(Environments.Development);

        using var provider = builder.Services.BuildServiceProvider();
        var jwt = provider
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);

        jwt.MapInboundClaims.ShouldBeFalse();
    }

    [Fact]
    public void Production_valid_configuration_enables_https_metadata()
    {
        var builder = BuildBuilder(Environments.Production);

        Should.NotThrow(() => ValidateOnStart(builder));
        using var provider = builder.Services.BuildServiceProvider();
        var jwt = provider
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);
        jwt.Authority.ShouldBe("https://identity.example/realms/travel");
        jwt.Audience.ShouldBe("travel-web");
        jwt.RequireHttpsMetadata.ShouldBeTrue();
    }

    private static HostApplicationBuilder BuildBuilder(
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

        var builder = new HostApplicationBuilder(
            new HostApplicationBuilderSettings
            {
                ApplicationName = "Travel.Modules.Identity.Tests.Unit",
                EnvironmentName = environmentName,
            }
        );
        builder.Configuration.AddInMemoryCollection(values);
        builder.AddIdentityModule();
        return builder;
    }

    private static void ValidateOnStart(HostApplicationBuilder builder)
    {
        using var provider = builder.Services.BuildServiceProvider();
        provider.GetRequiredService<IStartupValidator>().Validate();
    }
}
