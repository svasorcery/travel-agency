using System.Net;
using Shouldly;
using Xunit;

namespace Travel.Host.Tests.Integration.Support;

// Pure client-boundary regressions: referencing static fixture helpers never initializes Keycloak.
public sealed class SupportOperatorBrowserCookieTests
{
    [Theory]
    [InlineData("localhost")]
    [InlineData("127.0.0.1")]
    [InlineData("[::1]")]
    public void Login_retains_secure_session_cookie_on_its_own_http_loopback_origin(string host)
    {
        var origin = new Uri($"http://{host}:49152/");
        var target = new Uri(origin, "realms/travel/login-actions/authenticate");
        var cookies = new CookieContainer();
        cookies.SetCookies(
            target,
            "AUTH_SESSION_ID=fictional-session; Version=1; Path=/realms/travel/; Secure; HttpOnly; SameSite=None"
        );

        // This is the .NET/Keycloak boundary that previously dropped the browser session.
        cookies.GetCookieHeader(target).ShouldBeEmpty();
        SupportOperatorIssuanceFixture
            .BrowserCookieHeader(cookies, target, origin)
            .ShouldContain("AUTH_SESSION_ID=fictional-session");

        var retained = cookies.GetAllCookies().Cast<Cookie>().Single();
        retained.Name.ShouldBe("AUTH_SESSION_ID");
        retained.Secure.ShouldBeTrue();
        retained.HttpOnly.ShouldBeTrue();
        retained.Path.ShouldBe("/realms/travel/");
        cookies.GetCookieHeader(target).ShouldBeEmpty();
    }

    [Fact]
    public void Https_login_uses_the_normal_secure_cookie_projection()
    {
        var origin = new Uri("https://localhost:49152/");
        var target = new Uri(origin, "realms/travel/login-actions/authenticate");
        var cookies = new CookieContainer();
        cookies.SetCookies(
            target,
            "AUTH_SESSION_ID=fictional-session; Path=/realms/travel/; Secure; HttpOnly"
        );

        SupportOperatorIssuanceFixture
            .BrowserCookieHeader(cookies, target, origin)
            .ShouldBe("AUTH_SESSION_ID=fictional-session");
        cookies.GetCookieHeader(target).ShouldBe("AUTH_SESSION_ID=fictional-session");
    }

    [Theory]
    [InlineData("http://127.0.0.1:49153/realms/travel/login-actions/authenticate")]
    [InlineData("http://localhost:49152/realms/travel/login-actions/authenticate")]
    [InlineData("https://127.0.0.1:49152/realms/travel/login-actions/authenticate")]
    public void Login_cookie_projection_rejects_another_origin(string targetUri)
    {
        var origin = new Uri("http://127.0.0.1:49152/");
        var cookies = new CookieContainer();
        cookies.SetCookies(origin, "AUTH_SESSION_ID=fictional-session; Path=/; Secure");

        Should.Throw<InvalidOperationException>(() =>
            SupportOperatorIssuanceFixture.BrowserCookieHeader(cookies, new Uri(targetUri), origin)
        );
    }

    [Theory]
    [InlineData("http://issuer.example.invalid:49152/")]
    [InlineData("https://issuer.example.invalid:49152/")]
    [InlineData("http://192.0.2.1:49152/")]
    [InlineData("http://localhost.example.invalid:49152/")]
    [InlineData("ftp://localhost:49152/")]
    public void Login_cookie_projection_rejects_a_non_loopback_or_non_http_issuer(string originUri)
    {
        var origin = new Uri(originUri);

        Should.Throw<InvalidOperationException>(() =>
            SupportOperatorIssuanceFixture.BrowserCookieHeader(
                new CookieContainer(),
                new Uri(origin, "realms/travel/login-actions/authenticate"),
                origin
            )
        );
    }

    [Fact]
    public void Loopback_cookie_projection_preserves_path_and_expiry_filters()
    {
        var origin = new Uri("http://localhost:49152/");
        var target = new Uri(origin, "realms/travel/login-actions/authenticate");
        var cookies = new CookieContainer();
        cookies.SetCookies(
            target,
            "AUTH_SESSION_ID=fictional-session; Path=/realms/travel/; Secure; HttpOnly"
        );
        cookies.SetCookies(target, "OTHER_SESSION=fictional-other; Path=/realms/other/; Secure");
        cookies.SetCookies(
            target,
            "EXPIRED_SESSION=fictional-expired; Path=/realms/travel/; Secure"
        );
        cookies
            .GetAllCookies()
            .Cast<Cookie>()
            .Single(cookie => cookie.Name == "EXPIRED_SESSION")
            .Expired = true;

        SupportOperatorIssuanceFixture
            .BrowserCookieHeader(cookies, target, origin)
            .ShouldBe("AUTH_SESSION_ID=fictional-session");
    }
}
