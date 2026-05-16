using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Travel.Modules.Flights.Application.Notifications;
using Travel.Modules.Flights.Infrastructure.Notifications.Keycloak;

namespace Travel.Modules.Flights.Infrastructure.Notifications;

/// <summary>
/// Resolves user profiles from the Keycloak admin REST API. The <c>KeycloakAdmin</c> named
/// HttpClient carries a service-account bearer token via <see cref="KeycloakAdminAuthHandler"/>.
/// When the admin integration is not configured (see <see cref="KeycloakAdminOptions.IsConfigured"/>)
/// — or when a lookup fails — it degrades gracefully to a synthetic profile so the notification
/// pipeline still works end to end without a live Keycloak admin connection.
/// </summary>
public sealed class KeycloakUserDirectory(
    IHttpClientFactory httpClientFactory,
    IOptions<KeycloakAdminOptions> options,
    ILogger<KeycloakUserDirectory> logger
) : IUserDirectory
{
    private readonly KeycloakAdminOptions _options = options.Value;

    public async Task<UserProfile?> GetAsync(Guid userId, CancellationToken ct)
    {
        if (!_options.IsConfigured)
        {
            logger.LogDebug(
                "Keycloak admin not configured — returning fallback profile for user {UserId}.",
                userId
            );
            return FallbackProfile(userId);
        }

        try
        {
            var client = httpClientFactory.CreateClient(KeycloakAdminAuthHandler.HttpClientName);
            var url =
                $"{_options.AdminBaseUrl!.TrimEnd('/')}/admin/realms/{_options.Realm}/users/{userId}";
            var response = await client.GetFromJsonAsync<KeycloakUserDto>(url, ct);

            if (response is null)
                return FallbackProfile(userId);

            return new UserProfile(
                userId,
                response.Email ?? FallbackEmail(userId),
                response.FirstName ?? "Traveller",
                response.LastName ?? "",
                response.Attributes?.GetValueOrDefault("locale")?.FirstOrDefault() ?? "ru"
            );
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(
                ex,
                "Failed to fetch user profile for {UserId} from Keycloak — returning fallback.",
                userId
            );
            return FallbackProfile(userId);
        }
    }

    private static UserProfile FallbackProfile(Guid userId) =>
        new(userId, FallbackEmail(userId), "Traveller", "", "ru");

    private static string FallbackEmail(Guid userId) => $"{userId:N}@example.test";

    private sealed class KeycloakUserDto
    {
        public string? Email { get; init; }
        public string? FirstName { get; init; }
        public string? LastName { get; init; }
        public Dictionary<string, List<string>>? Attributes { get; init; }
    }
}
