using System.Net.Http.Headers;

namespace Travel.Modules.Flights.Infrastructure.Notifications.Keycloak;

/// <summary>
/// <see cref="DelegatingHandler"/> for the named <c>KeycloakAdmin</c> HttpClient: attaches a
/// service-account bearer token (from <see cref="IKeycloakAdminTokenProvider"/>) to every
/// outgoing request to the Keycloak admin REST API.
/// </summary>
public sealed class KeycloakAdminAuthHandler(IKeycloakAdminTokenProvider tokenProvider)
    : DelegatingHandler
{
    /// <summary>Named <see cref="HttpClient"/> for the admin REST API (this handler is attached to it).</summary>
    public const string HttpClientName = "KeycloakAdmin";

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        var token = await tokenProvider.GetTokenAsync(cancellationToken);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await base.SendAsync(request, cancellationToken);
    }
}
