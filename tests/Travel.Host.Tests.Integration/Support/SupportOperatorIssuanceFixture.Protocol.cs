using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.IdentityModel.Tokens;

namespace Travel.Host.Tests.Integration.Support;

public sealed partial class SupportOperatorIssuanceFixture
{
    public async Task<CodeFlowResult> IssueAsync(
        TestIdentity identity,
        string clientId,
        string scope,
        CancellationToken ct,
        bool wrongVerifier = false,
        string? challengeMethod = "S256"
    )
    {
        var cookies = new CookieContainer();
        using var handler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false };
        using var browser = new HttpClient(handler)
        {
            BaseAddress = baseAddress,
            Timeout = TimeSpan.FromSeconds(20),
        };
        var callback =
            clientId == OperatorClient
                ? new Uri("http://localhost:4200/support/operator")
                : new Uri("http://localhost:4200/flights");
        var state = RandomSecret();
        var verifier = RandomSecret();
        var parameters = new Dictionary<string, string?>
        {
            ["client_id"] = clientId,
            ["redirect_uri"] = callback.AbsoluteUri,
            ["response_type"] = "code",
            ["response_mode"] = "query",
            ["scope"] = scope,
            ["state"] = state,
            ["nonce"] = RandomSecret(),
            ["code_challenge"] = Base64UrlEncoder.Encode(
                SHA256.HashData(Encoding.ASCII.GetBytes(verifier))
            ),
            ["code_challenge_method"] = challengeMethod,
        };
        if (challengeMethod is null)
        {
            parameters.Remove("code_challenge");
            parameters.Remove("code_challenge_method");
        }
        else if (challengeMethod == "plain")
            parameters["code_challenge"] = verifier;
        var authorizationUri = QueryHelpers.AddQueryString(
            $"{Issuer}/protocol/openid-connect/auth",
            parameters
        );
        using var authorization = await browser.GetAsync(authorizationUri, ct);
        if (challengeMethod != "S256")
        {
            if (!RedirectError(authorization, callback, state, "invalid_request"))
                throw new InvalidOperationException(
                    "CI issuer did not reject the missing or non-S256 PKCE challenge."
                );
            return new CodeFlowResult(null, IssuanceDenial.PkceMethodRejected);
        }
        var authorizationError = ScopeRejection(authorization, callback, state);
        if (authorizationError)
            return new CodeFlowResult(null, IssuanceDenial.ScopeRejected);
        RequireStatus(authorization, HttpStatusCode.OK, "CI application authorization form");
        RetainBrowserCookies(authorization, cookies, new Uri(authorizationUri));
        var html = await authorization.Content.ReadAsStringAsync(ct);
        var form = Match(html, "<form\\b[^>]*\\bid\\s*=\\s*[\"']kc-form-login[\"'][^>]*>");
        var action = Match(form.Value, "\\baction\\s*=\\s*[\"'](?<value>[^\"']+)[\"']");
        if (!form.Success || !action.Success)
            throw new InvalidOperationException(
                "CI issuer did not supply the expected application login form."
            );
        var loginUri = new Uri(baseAddress, WebUtility.HtmlDecode(action.Groups["value"].Value));
        if (
            !SameOrigin(loginUri, baseAddress)
            || !loginUri.AbsolutePath.StartsWith(
                $"/realms/{Realm}/login-actions/",
                StringComparison.Ordinal
            )
        )
            throw new InvalidOperationException(
                "CI application login form target is outside its isolated issuer."
            );
        var inputs = new Dictionary<string, string>
        {
            ["username"] = identity.Username,
            ["password"] = identity.Password,
            ["credentialId"] = string.Empty,
        };
        // Retain hidden browser-flow fields while using the cookie jar from the initial GET.
        foreach (
            Match input in Regex.Matches(
                html,
                "<input\\b[^>]*>",
                RegexOptions.IgnoreCase,
                TimeSpan.FromSeconds(1)
            )
        )
        {
            var type = Match(input.Value, "\\btype\\s*=\\s*[\"'](?<value>[^\"']*)[\"']");
            var name = Match(input.Value, "\\bname\\s*=\\s*[\"'](?<value>[^\"']+)[\"']");
            var value = Match(input.Value, "\\bvalue\\s*=\\s*[\"'](?<value>[^\"']*)[\"']");
            if (
                type.Groups["value"].Value.Equals("hidden", StringComparison.OrdinalIgnoreCase)
                && name.Success
            )
                inputs.TryAdd(
                    WebUtility.HtmlDecode(name.Groups["value"].Value),
                    WebUtility.HtmlDecode(value.Groups["value"].Value)
                );
        }
        using var loginForm = new FormUrlEncodedContent(inputs);
        using var loginRequest = new HttpRequestMessage(HttpMethod.Post, loginUri)
        {
            Content = loginForm,
        };
        var cookieHeader = BrowserCookieHeader(cookies, loginUri, baseAddress);
        if (cookieHeader.Length > 0)
            loginRequest.Headers.Add("Cookie", cookieHeader);
        using var login = await browser.SendAsync(loginRequest, ct);
        if (ScopeRejection(login, callback, state))
            return new CodeFlowResult(null, IssuanceDenial.ScopeRejected);
        RequireStatus(login, HttpStatusCode.Found, "CI application login redirect");
        var redirect = login.Headers.Location;
        if (
            redirect is null
            || !SameOrigin(redirect, callback)
            || redirect.AbsolutePath != callback.AbsolutePath
        )
            throw new InvalidOperationException(
                "CI application login did not redirect to its allowlisted callback."
            );
        var query = QueryHelpers.ParseQuery(redirect.Query);
        if (
            !query.TryGetValue("state", out var returnedState)
            || returnedState.Count != 1
            || returnedState[0] != state
            || !query.TryGetValue("code", out var code)
            || code.Count != 1
            || string.IsNullOrWhiteSpace(code[0])
        )
            throw new InvalidOperationException(
                "CI application callback did not retain state and authorization code."
            );
        using var tokenForm = new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["client_id"] = clientId,
                ["redirect_uri"] = callback.AbsoluteUri,
                ["grant_type"] = "authorization_code",
                ["code"] = code[0]!,
                ["code_verifier"] = wrongVerifier ? RandomSecret() : verifier,
            }
        );
        using var exchange = await browser.PostAsync(
            $"{Issuer}/protocol/openid-connect/token",
            tokenForm,
            ct
        );
        if (wrongVerifier)
        {
            RequireStatus(exchange, HttpStatusCode.BadRequest, "CI mismatched PKCE verifier");
            using var rejected = await ReadJsonAsync(exchange, ct);
            if (RequiredString(rejected.RootElement, "error") != "invalid_grant")
                throw new InvalidOperationException(
                    "CI issuer rejected PKCE with an unexpected fixed error type."
                );
            return new CodeFlowResult(null, IssuanceDenial.PkceRejected);
        }
        RequireStatus(exchange, HttpStatusCode.OK, "CI application authorization-code exchange");
        using var tokens = await ReadJsonAsync(exchange, ct);
        return new CodeFlowResult(
            new IssuedToken(
                RequiredString(tokens.RootElement, "access_token"),
                signingKeys,
                Issuer
            ),
            IssuanceDenial.None
        );
    }

    private static Match Match(string input, string pattern) =>
        Regex.Match(input, pattern, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));

    private static void RetainBrowserCookies(
        HttpResponseMessage response,
        CookieContainer cookies,
        Uri responseUri
    )
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var headers))
            return;
        try
        {
            foreach (var header in headers)
                cookies.SetCookies(responseUri, header);
        }
        catch (CookieException)
        {
            // CookieException can contain the raw header: expose neither it nor its inner error.
            throw new InvalidOperationException("CI browser could not retain issuer cookies.");
        }
    }

    internal static string BrowserCookieHeader(
        CookieContainer cookies,
        Uri target,
        Uri isolatedIssuer
    )
    {
        if (
            !SameOrigin(target, isolatedIssuer)
            || target.Scheme is not ("http" or "https")
            || !(
                target.IdnHost.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                || IPAddress.TryParse(target.IdnHost, out var address)
                    && IPAddress.IsLoopback(address)
            )
        )
            throw new InvalidOperationException(
                "CI browser cookie target is outside its isolated loopback issuer."
            );

        // Keycloak 26.6 marks loopback HTTP as a secure browser context and emits Secure cookies.
        // .NET's jar filters those out on HTTP. Project this one origin through its HTTPS lookup
        // while preserving cookie names, Secure/HttpOnly metadata, paths and expiry checks.
        // This changes only cookie selection; the request retains the original issuer URL.
        var cookieUri =
            target.Scheme == "http"
                ? new UriBuilder(target) { Scheme = "https", Port = target.Port }.Uri
                : target;
        return cookies.GetCookieHeader(cookieUri);
    }

    private static bool SameOrigin(Uri uri, Uri expected) =>
        uri.IsAbsoluteUri
        && uri.Scheme == expected.Scheme
        && uri.Host == expected.Host
        && uri.Port == expected.Port;

    private static bool ScopeRejection(HttpResponseMessage response, Uri callback, string state) =>
        RedirectError(response, callback, state, "invalid_scope");

    private static bool RedirectError(
        HttpResponseMessage response,
        Uri callback,
        string state,
        string expectedError
    )
    {
        if (response.StatusCode != HttpStatusCode.Found || response.Headers.Location is not { } uri)
            return false;
        if (!SameOrigin(uri, callback) || uri.AbsolutePath != callback.AbsolutePath)
            return false;
        var query = QueryHelpers.ParseQuery(uri.Query);
        return query.TryGetValue("error", out var error)
            && error.Count == 1
            && error[0] == expectedError
            && query.TryGetValue("state", out var returnedState)
            && returnedState.Count == 1
            && returnedState[0] == state;
    }

    public enum IssuanceDenial
    {
        None,
        ScopeRejected,
        PkceRejected,
        PkceMethodRejected,
    }

    public sealed class CodeFlowResult(IssuedToken? token, IssuanceDenial denial)
    {
        public IssuedToken? Token { get; } = token;
        public IssuanceDenial Denial { get; } = denial;
    }

    public sealed class IssuedToken
    {
        private readonly string encoded;
        private readonly JsonWebKeySet keys;
        public ClaimsPrincipal Principal { get; }

        internal IssuedToken(string encoded, JsonWebKeySet keys, string issuer)
        {
            this.encoded = encoded;
            this.keys = keys;
            Principal = Validate(issuer, CustomerClient);
        }

        public bool ValidatesFor(string issuer, string audience)
        {
            try
            {
                Validate(issuer, audience);
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        private ClaimsPrincipal Validate(string issuer, string audience)
        {
            try
            {
                return new JwtSecurityTokenHandler { MapInboundClaims = false }.ValidateToken(
                    encoded,
                    new TokenValidationParameters
                    {
                        RequireSignedTokens = true,
                        RequireExpirationTime = true,
                        ValidateIssuer = true,
                        ValidIssuer = issuer,
                        ValidateAudience = true,
                        ValidAudience = audience,
                        ValidateLifetime = true,
                        ClockSkew = TimeSpan.FromSeconds(5),
                        ValidateIssuerSigningKey = true,
                        IssuerSigningKeys = keys.GetSigningKeys(),
                        ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
                        IncludeTokenOnFailedValidation = false,
                        LogValidationExceptions = false,
                    },
                    out _
                );
            }
            catch (SecurityTokenException)
            {
                throw new InvalidOperationException(
                    "CI-issued access token failed signature, issuer, audience or lifetime validation."
                );
            }
        }
    }
}
