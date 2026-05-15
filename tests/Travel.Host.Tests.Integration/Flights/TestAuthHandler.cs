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
/// An optional <c>X-Test-Scopes</c> header (space-delimited) populates the <c>scope</c> claim,
/// enabling tests to verify the <c>flights:book</c> policy.
/// </summary>
public sealed class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder
) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";
    public const string UserIdHeader = "X-Test-UserId";
    public const string ScopesHeader = "X-Test-Scopes";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (
            !Request.Headers.TryGetValue(UserIdHeader, out var raw)
            || string.IsNullOrWhiteSpace(raw)
        )
            return Task.FromResult(AuthenticateResult.NoResult());

        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, raw.ToString()) };

        if (
            Request.Headers.TryGetValue(ScopesHeader, out var scopes)
            && !string.IsNullOrWhiteSpace(scopes)
        )
            claims.Add(new Claim("scope", scopes.ToString()));

        var identity = new ClaimsIdentity(claims, SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
