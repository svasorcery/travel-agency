namespace Travel.Modules.Flights.Infrastructure.Notifications.Keycloak;

/// <summary>
/// Supplies a service-account access token for the Keycloak admin REST API,
/// acquired via the <c>client_credentials</c> grant and cached until shortly before expiry.
/// </summary>
public interface IKeycloakAdminTokenProvider
{
    /// <summary>Returns a valid bearer token, fetching a fresh one only when the cache is empty or stale.</summary>
    Task<string> GetTokenAsync(CancellationToken ct);
}
