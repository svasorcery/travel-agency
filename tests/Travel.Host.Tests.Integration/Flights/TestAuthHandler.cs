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
/// It emits the same raw <c>sub</c> and space-delimited <c>scp</c> shape as the JWT boundary;
/// the registered production claims transformation creates canonical NameIdentifier and
/// <c>scope</c> claims before authorization evaluates the <c>flights:book</c> policy.
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

        var claims = new List<Claim> { new("sub", raw.ToString()) };

        if (
            Request.Headers.TryGetValue(ScopesHeader, out var scopes)
            && !string.IsNullOrWhiteSpace(scopes)
        )
            claims.Add(new Claim("scp", scopes.ToString()));

        var identity = new ClaimsIdentity(claims, SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.Headers.WWWAuthenticate = "Bearer";
        return base.HandleChallengeAsync(properties);
    }
}
