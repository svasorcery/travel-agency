using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Travel.Modules.Flights.Application.Notifications;

namespace Travel.Modules.Flights.Infrastructure.Notifications;

/// <summary>
/// Attempts to resolve user profiles from the Keycloak admin REST API.
/// If <c>Flights:Keycloak:AdminBaseUrl</c> is not configured, falls back to a best-effort
/// synthetic profile so the notification pipeline works end-to-end without a live Keycloak
/// admin connection. Full Keycloak admin wiring (service-account token, realm config) is a
/// follow-up task.
/// </summary>
public sealed class KeycloakUserDirectory(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<KeycloakUserDirectory> logger
) : IUserDirectory
{
    public async Task<UserProfile?> GetAsync(Guid userId, CancellationToken ct)
    {
        var adminBaseUrl = configuration["Flights:Keycloak:AdminBaseUrl"];
        var realm = configuration["Flights:Keycloak:Realm"] ?? "travel";

        if (string.IsNullOrWhiteSpace(adminBaseUrl))
        {
            // Graceful degradation: return a synthetic profile so downstream
            // notification handlers can proceed without Keycloak admin being configured.
            logger.LogDebug(
                "Flights:Keycloak:AdminBaseUrl not configured — returning fallback profile for user {UserId}.",
                userId
            );
            return new UserProfile(userId, $"{userId:N}@example.test", "Traveller", "", "ru");
        }

        try
        {
            var client = httpClientFactory.CreateClient("KeycloakAdmin");
            var url = $"{adminBaseUrl.TrimEnd('/')}/admin/realms/{realm}/users/{userId}";
            var response = await client.GetFromJsonAsync<KeycloakUserDto>(url, ct);

            if (response is null)
                return null;

            return new UserProfile(
                userId,
                response.Email ?? $"{userId:N}@example.test",
                response.FirstName ?? "Traveller",
                response.LastName ?? "",
                response.Attributes?.GetValueOrDefault("locale")?.FirstOrDefault()
                    ?? response.Attributes?.GetValueOrDefault("locale")?.FirstOrDefault()
                    ?? "ru"
            );
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Failed to fetch user profile for {UserId} from Keycloak — returning fallback.",
                userId
            );
            return new UserProfile(userId, $"{userId:N}@example.test", "Traveller", "", "ru");
        }
    }

    private sealed class KeycloakUserDto
    {
        public string? Email { get; init; }
        public string? FirstName { get; init; }
        public string? LastName { get; init; }
        public Dictionary<string, List<string>>? Attributes { get; init; }
    }
}
