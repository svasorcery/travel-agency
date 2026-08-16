using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;
using Shouldly;
using Travel.Modules.Flights.Application;
using Travel.Modules.Flights.Core.Providers;
using Travel.Modules.Flights.Infrastructure;
using Travel.Modules.Flights.Infrastructure.ExternalServices;
using Travel.Modules.Flights.Infrastructure.Notifications.Email;
using Travel.Modules.Flights.Infrastructure.Notifications.Keycloak;
using Travel.Modules.Flights.Infrastructure.Providers.Duffel;
using Travel.Modules.Flights.Infrastructure.Providers.Travelpayouts;
using Xunit;

namespace Travel.Modules.Flights.Tests.Unit.Composition;

public sealed class FlightsOptionsValidationTests
{
    private const string ApiKeySentinel = "duffel-api-secret-sentinel";
    private const string WebhookSecretSentinel = "duffel-webhook-secret-sentinel";
    private const string TravelpayoutsTokenSentinel = "travelpayouts-token-secret-sentinel";
    private const string KeycloakSecretSentinel = "keycloak-admin-secret-sentinel";

    public static TheoryData<string, string?> InvalidProductionSmtpSettings =>
        new()
        {
            { "Flights:Smtp:Host", null },
            { "Flights:Smtp:Host", "localhost" },
            { "Flights:Smtp:Host", "127.0.0.1" },
            { "Flights:Smtp:Host", "LOCALHOST." },
            { "Flights:Smtp:Port", "0" },
            { "Flights:Smtp:Port", "65536" },
            { "Flights:Smtp:FromAddress", null },
            { "Flights:Smtp:FromAddress", "not-an-email" },
        };

    public static TheoryData<string, string?> InvalidProductionDuffelSettings =>
        new()
        {
            { "Flights:Duffel:ApiKey", null },
            { "Flights:Duffel:WebhookSecret", null },
            { "Flights:Duffel:BaseUrl", "relative/path" },
            { "Flights:Duffel:BaseUrl", "http://api.duffel.example" },
            { "Flights:Duffel:BaseUrl", "https://localhost:9443" },
            { "Flights:Duffel:BaseUrl", "https://127.0.0.1:9443" },
            { "Flights:Duffel:BaseUrl", "https://LOCALHOST.:9443" },
            { "Flights:Duffel:TimeoutSeconds", "0" },
            { "Flights:Duffel:SearchTimeoutSeconds", "0" },
        };

    public static TheoryData<string, string?> InvalidEnabledTravelpayoutsSettings =>
        new()
        {
            { "Flights:Travelpayouts:ApiToken", null },
            { "Flights:Travelpayouts:PartnerMarker", null },
            { "Flights:Travelpayouts:BaseUrl", "relative/path" },
            { "Flights:Travelpayouts:BaseUrl", "http://api.travelpayouts.example" },
            { "Flights:Travelpayouts:BaseUrl", "https://127.0.0.1:9443" },
            { "Flights:Travelpayouts:BaseUrl", "https://LOCALHOST.:9443" },
            { "Flights:Travelpayouts:TimeoutSeconds", "0" },
        };

    public static TheoryData<string?, string?, string?> PartialKeycloakAdminSettings =>
        new()
        {
            { "https://identity.example", null, null },
            { null, "travel-host", null },
            { null, null, KeycloakSecretSentinel },
            { "https://identity.example", "travel-host", null },
            { "https://identity.example", null, KeycloakSecretSentinel },
            { null, "travel-host", KeycloakSecretSentinel },
        };

    public static TheoryData<string, string?> InvalidFrankfurterSettings =>
        new()
        {
            { "Flights:Providers:Frankfurter:BaseAddress", "relative/path" },
            { "Flights:Providers:Frankfurter:BaseAddress", "ftp://fx.example" },
            { "Flights:Providers:Frankfurter:TimeoutSeconds", "0" },
        };

    [Theory]
    [MemberData(nameof(InvalidProductionSmtpSettings))]
    public void Production_rejects_invalid_smtp(string key, string? value)
    {
        var services = BuildServices(Environments.Production, (key, value));

        var error = Should.Throw<OptionsValidationException>(() => ValidateOnStart(services));

        AssertSecretsAreRedacted(error);
    }

    [Fact]
    public void Development_accepts_explicit_mailpit_smtp()
    {
        var services = BuildServices(
            Environments.Development,
            ("Flights:Smtp:Host", "localhost"),
            ("Flights:Smtp:Port", "1025")
        );

        Should.NotThrow(() => ValidateOnStart(services));
        using var provider = services.BuildServiceProvider();
        var smtp = provider.GetRequiredService<IOptions<SmtpOptions>>().Value;
        smtp.Host.ShouldBe("localhost");
        smtp.Port.ShouldBe(1025);
    }

    [Theory]
    [MemberData(nameof(InvalidProductionDuffelSettings))]
    public void Production_rejects_invalid_mandatory_duffel(string key, string? value)
    {
        var services = BuildServices(Environments.Production, (key, value));

        var error = Should.Throw<OptionsValidationException>(() => ValidateOnStart(services));

        AssertSecretsAreRedacted(error);
    }

    [Theory]
    [MemberData(nameof(InvalidEnabledTravelpayoutsSettings))]
    public void Enabled_travelpayouts_rejects_invalid_options(string key, string? value)
    {
        var services = BuildServices(
            Environments.Development,
            ("Flights:FeatureFlags:Travelpayouts:Enabled", "true"),
            (key, value)
        );

        var error = Should.Throw<OptionsValidationException>(() => ValidateOnStart(services));

        AssertSecretsAreRedacted(error);
    }

    [Fact]
    public void Disabled_travelpayouts_registers_no_options_provider_client_or_probe()
    {
        var services = BuildServices(
            Environments.Development,
            ("Flights:FeatureFlags:Travelpayouts:Enabled", "false")
        );

        services
            .Any(d => d.ServiceType == typeof(IConfigureOptions<TravelpayoutsOptions>))
            .ShouldBeFalse();
        services
            .Any(d =>
                d.ServiceType == typeof(IFlightSearchProvider)
                && d.ImplementationType == typeof(TravelpayoutsSearchProvider)
            )
            .ShouldBeFalse();
        services.Any(d => d.ServiceType == typeof(TravelpayoutsClient)).ShouldBeFalse();

        using var provider = services.BuildServiceProvider();
        provider
            .GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value.Registrations.Any(r => r.Name == "travelpayouts")
            .ShouldBeFalse();
        provider
            .GetRequiredService<IOptionsMonitor<HttpClientFactoryOptions>>()
            .Get("travelpayouts-health")
            .HttpClientActions.ShouldBeEmpty();
    }

    [Fact]
    public void Completely_absent_keycloak_admin_integration_is_valid_and_disabled()
    {
        var services = BuildServices(Environments.Production);

        Should.NotThrow(() => ValidateOnStart(services));
        using var provider = services.BuildServiceProvider();
        provider
            .GetRequiredService<IOptions<KeycloakAdminOptions>>()
            .Value.IsConfigured.ShouldBeFalse();
    }

    [Theory]
    [MemberData(nameof(PartialKeycloakAdminSettings))]
    public void Partial_keycloak_admin_integration_is_rejected(
        string? adminBaseUrl,
        string? clientId,
        string? clientSecret
    )
    {
        var overrides = new List<(string Key, string? Value)>();
        if (adminBaseUrl is not null)
            overrides.Add(("Flights:Keycloak:AdminBaseUrl", adminBaseUrl));
        if (clientId is not null)
            overrides.Add(("Flights:Keycloak:ClientId", clientId));
        if (clientSecret is not null)
            overrides.Add(("Flights:Keycloak:ClientSecret", clientSecret));
        var services = BuildServices(Environments.Development, [.. overrides]);

        var error = Should.Throw<OptionsValidationException>(() => ValidateOnStart(services));

        AssertSecretsAreRedacted(error);
    }

    [Theory]
    [MemberData(nameof(InvalidFrankfurterSettings))]
    public void Frankfurter_rejects_invalid_uri_or_timeout(string key, string? value)
    {
        var services = BuildServices(Environments.Development, (key, value));

        Should.Throw<OptionsValidationException>(() => ValidateOnStart(services));
    }

    [Fact]
    public void Valid_production_configuration_passes_startup_validation()
    {
        var services = BuildServices(Environments.Production);

        Should.NotThrow(() => ValidateOnStart(services));
    }

    private static IServiceCollection BuildServices(
        string environmentName,
        params (string Key, string? Value)[] overrides
    )
    {
        var values = ValidConfiguration();
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
        services.AddFlightsModule(
            configuration,
            new OptionsHostEnvironment { EnvironmentName = environmentName }
        );
        return services;
    }

    private static Dictionary<string, string?> ValidConfiguration() =>
        new()
        {
            ["ConnectionStrings:redis"] = "redis.internal:6379",
            ["Flights:FeatureFlags:Travelpayouts:Enabled"] = "false",
            ["Flights:Duffel:BaseUrl"] = "https://api.duffel.com",
            ["Flights:Duffel:ApiKey"] = ApiKeySentinel,
            ["Flights:Duffel:WebhookSecret"] = WebhookSecretSentinel,
            ["Flights:Duffel:TimeoutSeconds"] = "10",
            ["Flights:Duffel:SearchTimeoutSeconds"] = "4",
            ["Flights:Travelpayouts:BaseUrl"] = "https://api.travelpayouts.com",
            ["Flights:Travelpayouts:ApiToken"] = TravelpayoutsTokenSentinel,
            ["Flights:Travelpayouts:PartnerMarker"] = "partner-123",
            ["Flights:Travelpayouts:TimeoutSeconds"] = "4",
            ["Flights:Providers:Frankfurter:BaseAddress"] = "https://api.frankfurter.app/",
            ["Flights:Providers:Frankfurter:TimeoutSeconds"] = "2",
            ["Flights:Smtp:Host"] = "smtp.internal",
            ["Flights:Smtp:Port"] = "587",
            ["Flights:Smtp:FromAddress"] = "noreply@travel.example",
            ["Flights:Smtp:FromName"] = "Travel Platform",
        };

    private static void ValidateOnStart(IServiceCollection services)
    {
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IStartupValidator>().Validate();
    }

    private static void AssertSecretsAreRedacted(OptionsValidationException error)
    {
        error.Message.ShouldNotContain(ApiKeySentinel);
        error.Message.ShouldNotContain(WebhookSecretSentinel);
        error.Message.ShouldNotContain(TravelpayoutsTokenSentinel);
        error.Message.ShouldNotContain(KeycloakSecretSentinel);
    }
}

file sealed class OptionsHostEnvironment : IHostEnvironment
{
    public string EnvironmentName { get; set; } = Environments.Development;
    public string ApplicationName { get; set; } = "Travel.Modules.Flights.Tests.Unit";
    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
