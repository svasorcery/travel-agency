using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Travel.Modules.Flights.Infrastructure.Notifications.Keycloak;

/// <summary>
/// Acquires (and caches) a service-account access token for the Keycloak admin REST API
/// using the OAuth2 <c>client_credentials</c> grant. Registered as a singleton so the token
/// is shared in-process and re-fetched only when empty or within the expiry-skew window.
/// </summary>
public sealed class KeycloakAdminTokenProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<KeycloakAdminOptions> options,
    TimeProvider time,
    ILogger<KeycloakAdminTokenProvider> logger
) : IKeycloakAdminTokenProvider
{
    /// <summary>Named <see cref="HttpClient"/> used to call the token endpoint (no auth handler).</summary>
    public const string HttpClientName = "KeycloakAdminToken";

    // Re-fetch this long before the reported expiry so a token never expires mid-request.
    private static readonly TimeSpan ExpirySkew = TimeSpan.FromSeconds(30);

    private readonly KeycloakAdminOptions _options = options.Value;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private string? _cachedToken;
    private DateTimeOffset _expiresAt;

    public async Task<string> GetTokenAsync(CancellationToken ct)
    {
        if (_cachedToken is not null && time.GetUtcNow() < _expiresAt)
            return _cachedToken;

        await _gate.WaitAsync(ct);
        try
        {
            // Double-checked: another caller may have refreshed while we waited on the gate.
            if (_cachedToken is not null && time.GetUtcNow() < _expiresAt)
                return _cachedToken;

            if (!_options.IsConfigured)
                throw new InvalidOperationException(
                    "Keycloak admin is not configured (Flights:Keycloak AdminBaseUrl/ClientId/ClientSecret)."
                );

            var tokenUrl =
                $"{_options.AdminBaseUrl!.TrimEnd('/')}/realms/{_options.Realm}/protocol/openid-connect/token";

            using var form = new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["grant_type"] = "client_credentials",
                    ["client_id"] = _options.ClientId!,
                    ["client_secret"] = _options.ClientSecret!,
                }
            );

            var client = httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.PostAsync(tokenUrl, form, ct);
            response.EnsureSuccessStatusCode();

            var payload =
                await response.Content.ReadFromJsonAsync<TokenResponse>(ct)
                ?? throw new InvalidOperationException(
                    "Keycloak token endpoint returned an empty body."
                );

            _cachedToken = payload.AccessToken;
            // Keep the cache valid for at least a few seconds even if expires_in is tiny.
            var lifetime = TimeSpan.FromSeconds(payload.ExpiresIn) - ExpirySkew;
            if (lifetime < TimeSpan.FromSeconds(5))
                lifetime = TimeSpan.FromSeconds(5);
            _expiresAt = time.GetUtcNow() + lifetime;

            logger.LogDebug(
                "Acquired Keycloak admin token (expires in {ExpiresIn}s).",
                payload.ExpiresIn
            );
            return _cachedToken;
        }
        finally
        {
            _gate.Release();
        }
    }

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn
    );
}
