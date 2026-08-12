using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Travel.Modules.Flights.Infrastructure.Notifications.Keycloak;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace Travel.Modules.Flights.Tests.Integration.Notifications;

/// <summary>
/// Exercises <see cref="KeycloakAdminTokenProvider"/> against a WireMock'd Keycloak
/// token endpoint — covers the client-credentials grant and the in-memory token cache.
/// </summary>
[Trait("Category", "Integration")]
public sealed class KeycloakAdminTokenProviderTests : IDisposable
{
    private readonly WireMockServer _keycloak = WireMockServer.Start();
    private readonly FakeTimeProvider _time = new();

    public void Dispose() => _keycloak.Stop();

    private KeycloakAdminTokenProvider BuildProvider()
    {
        var options = Options.Create(
            new KeycloakAdminOptions
            {
                AdminBaseUrl = _keycloak.Url,
                Realm = "travel",
                ClientId = "flights-admin",
                ClientSecret = "s3cr3t",
            }
        );
        return new KeycloakAdminTokenProvider(
            new StubHttpClientFactory(new HttpClient()),
            options,
            _time,
            NullLogger<KeycloakAdminTokenProvider>.Instance
        );
    }

    private void StubTokenEndpoint(string accessToken, int expiresIn)
    {
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
                        $$"""
                        {"access_token":"{{accessToken}}","expires_in":{{expiresIn}},"token_type":"Bearer"}
                        """
                    )
            );
    }

    [Fact]
    public async Task GetTokenAsync_PostsClientCredentialsGrant_ReturnsAccessToken()
    {
        StubTokenEndpoint("token-abc", expiresIn: 300);
        var provider = BuildProvider();

        var token = await provider.GetTokenAsync(TestContext.Current.CancellationToken);

        token.ShouldBe("token-abc");

        // The single request must be a client_credentials grant carrying the configured client.
        var request = _keycloak.LogEntries.ShouldHaveSingleItem();
        var requestMessage = request.RequestMessage;
        requestMessage.ShouldNotBeNull();
        var body = requestMessage.Body;
        body.ShouldNotBeNull();
        body.ShouldContain("grant_type=client_credentials");
        body.ShouldContain("client_id=flights-admin");
        body.ShouldContain("client_secret=s3cr3t");
    }

    [Fact]
    public async Task GetTokenAsync_CachesToken_WithinExpiryWindow()
    {
        StubTokenEndpoint("token-cached", expiresIn: 300);
        var provider = BuildProvider();

        var first = await provider.GetTokenAsync(TestContext.Current.CancellationToken);
        var second = await provider.GetTokenAsync(TestContext.Current.CancellationToken);

        first.ShouldBe("token-cached");
        second.ShouldBe("token-cached");
        // Cached — the token endpoint is hit exactly once.
        _keycloak.LogEntries.Count().ShouldBe(1);
    }

    [Fact]
    public async Task GetTokenAsync_RefetchesToken_AfterExpiry()
    {
        StubTokenEndpoint("token-1", expiresIn: 300);
        var provider = BuildProvider();

        var first = await provider.GetTokenAsync(TestContext.Current.CancellationToken);

        // Advance past the cached token's lifetime, then re-stub with a fresh token.
        _time.Advance(TimeSpan.FromSeconds(301));
        _keycloak.Reset();
        StubTokenEndpoint("token-2", expiresIn: 300);

        var second = await provider.GetTokenAsync(TestContext.Current.CancellationToken);

        first.ShouldBe("token-1");
        second.ShouldBe("token-2");
    }

    [Fact]
    public async Task GetTokenAsync_RefreshesProactivelyBeforeExpiry_WithinSkewWindow()
    {
        // ExpirySkew is 30 s. A 300 s token is cached for 270 s (300 - 30).
        // Advancing by 271 s puts us past the skew boundary → proactive refetch on next call.
        StubTokenEndpoint("token-first", expiresIn: 300);
        var provider = BuildProvider();

        var first = await provider.GetTokenAsync(TestContext.Current.CancellationToken);
        first.ShouldBe("token-first");
        _keycloak.LogEntries.Count().ShouldBe(1);

        // Advance by 271 s — within the ExpirySkew window (last 30 s of the 300 s lifetime).
        _time.Advance(TimeSpan.FromSeconds(271));

        // Re-stub with a new token before the second call.
        _keycloak.Reset();
        StubTokenEndpoint("token-refreshed", expiresIn: 300);

        var second = await provider.GetTokenAsync(TestContext.Current.CancellationToken);

        // The provider should have proactively refetched.
        second.ShouldBe(
            "token-refreshed",
            "Token should be refreshed proactively when within the ExpirySkew window."
        );
        _keycloak
            .LogEntries.Count()
            .ShouldBe(
                1,
                "Exactly one refresh request should be made after crossing the skew window."
            );
    }
}

/// <summary>Minimal <see cref="IHttpClientFactory"/> returning a single pre-built client.</summary>
file sealed class StubHttpClientFactory(HttpClient client) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => client;
}
