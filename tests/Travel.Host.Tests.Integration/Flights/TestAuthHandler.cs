using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Travel.Host.Tests.Integration.Flights;

/// <summary>
/// Test authentication scheme for HTTP-pipeline tests. A request carrying the
/// <c>X-Test-UserId</c> header is authenticated as that user; a request without it is
/// anonymous and is rejected by <c>[Authorize]</c> endpoints with 401. This lets the tests
/// exercise the real authorization pipeline without a live Keycloak / signed JWTs.
/// </summary>
public sealed class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder
) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";
    public const string UserIdHeader = "X-Test-UserId";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (
            !Request.Headers.TryGetValue(UserIdHeader, out var raw)
            || string.IsNullOrWhiteSpace(raw)
        )
            return Task.FromResult(AuthenticateResult.NoResult());

        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, raw.ToString())],
            SchemeName
        );
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
