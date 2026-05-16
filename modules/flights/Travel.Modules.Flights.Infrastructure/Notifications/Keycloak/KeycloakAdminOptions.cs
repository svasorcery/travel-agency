namespace Travel.Modules.Flights.Infrastructure.Notifications.Keycloak;

/// <summary>
/// Configuration for the Keycloak admin REST integration used by
/// <see cref="KeycloakUserDirectory"/>. Bound from the <c>Flights:Keycloak</c> section.
/// When <see cref="IsConfigured"/> is false the user directory degrades gracefully to a
/// synthetic profile (see <see cref="KeycloakUserDirectory"/>).
/// </summary>
public sealed class KeycloakAdminOptions
{
    public const string SectionName = "Flights:Keycloak";

    /// <summary>Keycloak server base URL, e.g. <c>http://keycloak:8080</c> (no trailing path).</summary>
    public string? AdminBaseUrl { get; set; }

    /// <summary>Realm that owns both the service-account client and the users being queried.</summary>
    public string Realm { get; set; } = "travel";

    /// <summary>Service-account client id used for the <c>client_credentials</c> grant.</summary>
    public string? ClientId { get; set; }

    /// <summary>Service-account client secret.</summary>
    public string? ClientSecret { get; set; }

    /// <summary>True when enough is configured to talk to the Keycloak admin API.</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(AdminBaseUrl)
        && !string.IsNullOrWhiteSpace(ClientId)
        && !string.IsNullOrWhiteSpace(ClientSecret);
}
