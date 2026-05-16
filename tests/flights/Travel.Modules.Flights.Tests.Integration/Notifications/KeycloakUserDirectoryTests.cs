using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Travel.Modules.Flights.Infrastructure.Notifications;
using Travel.Modules.Flights.Infrastructure.Notifications.Keycloak;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace Travel.Modules.Flights.Tests.Integration.Notifications;

/// <summary>
/// Exercises <see cref="KeycloakUserDirectory"/> against a WireMock'd Keycloak: the configured
/// path (token grant → bearer-authenticated admin lookup) and the two graceful-degradation
/// paths (not configured, admin API failure).
/// </summary>
[Trait("Category", "Integration")]
public sealed class KeycloakUserDirectoryTests : IDisposable
{
    private readonly WireMockServer _keycloak = WireMockServer.Start();

    public void Dispose() => _keycloak.Stop();

    // Mirrors the Program.cs registration: token-endpoint client + admin client with the
    // bearer-attaching DelegatingHandler.
    private IHttpClientFactory BuildHttpClientFactory(KeycloakAdminOptions opts)
    {
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(new FakeTimeProvider());
        services.AddLogging();
        services.AddSingleton(Options.Create(opts));
        services.AddSingleton<IKeycloakAdminTokenProvider, KeycloakAdminTokenProvider>();
        services.AddTransient<KeycloakAdminAuthHandler>();
        services.AddHttpClient(KeycloakAdminTokenProvider.HttpClientName);
        services
            .AddHttpClient(KeycloakAdminAuthHandler.HttpClientName)
            .AddHttpMessageHandler<KeycloakAdminAuthHandler>();
        return services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>();
    }

    private KeycloakAdminOptions ConfiguredOptions() =>
        new()
        {
            AdminBaseUrl = _keycloak.Url,
            Realm = "travel",
            ClientId = "flights-admin",
            ClientSecret = "s3cr3t",
        };

    private void StubTokenEndpoint(string accessToken) =>
        _keycloak
            .Given(
                Request
                    .Create()
                    .WithPath("/realms/travel/protocol/openid-connect/token")
                    .UsingPost()
            )
            .RespondWith(
                Response
                    .Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody(
                        $$"""{"access_token":"{{accessToken}}","expires_in":300,"token_type":"Bearer"}"""
                    )
            );

    [Fact]
    public async Task GetAsync_WhenConfigured_ReturnsRealProfile_AndAttachesBearerToken()
    {
        var userId = Guid.NewGuid();
        StubTokenEndpoint("token-xyz");
        // The admin lookup only matches when the bearer token is attached by the auth handler.
        _keycloak
            .Given(
                Request
                    .Create()
                    .WithPath($"/admin/realms/travel/users/{userId}")
                    .WithHeader("Authorization", "Bearer token-xyz")
                    .UsingGet()
            )
            .RespondWith(
                Response
                    .Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody(
                        """
                        {"email":"real@kc.test","firstName":"Real","lastName":"User","attributes":{"locale":["en"]}}
                        """
                    )
            );

        var sut = new KeycloakUserDirectory(
            BuildHttpClientFactory(ConfiguredOptions()),
            Options.Create(ConfiguredOptions()),
            NullLogger<KeycloakUserDirectory>.Instance
        );

        var profile = await sut.GetAsync(userId, TestContext.Current.CancellationToken);

        profile.ShouldNotBeNull();
        profile.Id.ShouldBe(userId);
        profile.Email.ShouldBe("real@kc.test");
        profile.GivenName.ShouldBe("Real");
        profile.FamilyName.ShouldBe("User");
        profile.Locale.ShouldBe("en");
    }

    [Fact]
    public async Task GetAsync_WhenNotConfigured_ReturnsFallbackProfile()
    {
        var userId = Guid.NewGuid();
        var unconfigured = new KeycloakAdminOptions { AdminBaseUrl = "" };

        var sut = new KeycloakUserDirectory(
            BuildHttpClientFactory(unconfigured),
            Options.Create(unconfigured),
            NullLogger<KeycloakUserDirectory>.Instance
        );

        var profile = await sut.GetAsync(userId, TestContext.Current.CancellationToken);

        profile.ShouldNotBeNull();
        profile.Email.ShouldBe($"{userId:N}@example.test");
        // Not configured → no HTTP call attempted.
        _keycloak.LogEntries.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetAsync_WhenAdminApiFails_ReturnsFallbackProfile()
    {
        var userId = Guid.NewGuid();
        StubTokenEndpoint("token-xyz");
        _keycloak
            .Given(Request.Create().WithPath($"/admin/realms/travel/users/{userId}").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(500));

        var sut = new KeycloakUserDirectory(
            BuildHttpClientFactory(ConfiguredOptions()),
            Options.Create(ConfiguredOptions()),
            NullLogger<KeycloakUserDirectory>.Instance
        );

        var profile = await sut.GetAsync(userId, TestContext.Current.CancellationToken);

        profile.ShouldNotBeNull();
        profile.Email.ShouldBe($"{userId:N}@example.test");
    }

    [Fact]
    public async Task GetAsync_WhenCancelled_PropagatesCancellation()
    {
        var userId = Guid.NewGuid();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var sut = new KeycloakUserDirectory(
            BuildHttpClientFactory(ConfiguredOptions()),
            Options.Create(ConfiguredOptions()),
            NullLogger<KeycloakUserDirectory>.Instance
        );

        // OperationCanceledException must not be swallowed
        await Should.ThrowAsync<OperationCanceledException>(() => sut.GetAsync(userId, cts.Token));
    }

    [Fact]
    public async Task GetAsync_WhenResponseBodyIsNull_ReturnsFallbackProfile()
    {
        var userId = Guid.NewGuid();
        StubTokenEndpoint("token-xyz");
        // Return 200 with empty/null body so GetFromJsonAsync returns null
        _keycloak
            .Given(Request.Create().WithPath($"/admin/realms/travel/users/{userId}").UsingGet())
            .RespondWith(
                Response
                    .Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody("null")
            );

        var sut = new KeycloakUserDirectory(
            BuildHttpClientFactory(ConfiguredOptions()),
            Options.Create(ConfiguredOptions()),
            NullLogger<KeycloakUserDirectory>.Instance
        );

        var profile = await sut.GetAsync(userId, TestContext.Current.CancellationToken);

        // null body yields fallback, not hard null
        profile.ShouldNotBeNull();
        profile.Email.ShouldBe($"{userId:N}@example.test");
    }
}
