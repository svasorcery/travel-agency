using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Travel.Modules.Support.Api.Composition;
using Travel.Shared.Web;
using Xunit;

namespace Travel.Host.Tests.Integration.Support;

[Trait("Category", "Integration")]
[Collection(HostIntegrationCollection.Name)]
public sealed class SupportOperatorIssuanceTests(SupportOperatorIssuanceFixture issuer)
    : IClassFixture<SupportOperatorIssuanceFixture>
{
    [Fact(Timeout = 60_000)]
    public async Task Entitled_operator_gets_signed_support_token_without_booking_and_cannot_request_booking()
    {
        var result = await issuer.IssueAsync(
            issuer.Operator,
            SupportOperatorIssuanceFixture.OperatorClient,
            "openid support:operator",
            TestContext.Current.CancellationToken
        );
        result.Denial.ShouldBe(SupportOperatorIssuanceFixture.IssuanceDenial.None);
        var token = result.Token.ShouldNotBeNull();
        AssertIdentity(
            token,
            issuer.Operator.Subject,
            SupportOperatorIssuanceFixture.OperatorClient
        );
        Scopes(token).ShouldContain("support:operator");
        Scopes(token).ShouldNotContain("flights:book");
        (await IsOperatorAuthorizedAsync(token)).ShouldBeTrue();
        token.ValidatesFor(issuer.Issuer, "other-audience").ShouldBeFalse();
        token
            .ValidatesFor("https://other-issuer.example.invalid/realms/travel", "travel-web")
            .ShouldBeFalse();

        var attempted = await issuer.IssueAsync(
            issuer.Operator,
            SupportOperatorIssuanceFixture.OperatorClient,
            "openid support:operator flights:book",
            TestContext.Current.CancellationToken
        );
        if (attempted.Token is { } issued)
        {
            attempted.Denial.ShouldBe(SupportOperatorIssuanceFixture.IssuanceDenial.None);
            AssertIdentity(
                issued,
                issuer.Operator.Subject,
                SupportOperatorIssuanceFixture.OperatorClient
            );
            Scopes(issued).ShouldNotContain("flights:book");
        }
        else
            attempted.Denial.ShouldBe(SupportOperatorIssuanceFixture.IssuanceDenial.ScopeRejected);
    }

    [Fact(Timeout = 60_000)]
    public async Task Non_operator_cannot_gain_support_permission_by_requesting_its_optional_scope()
    {
        var result = await issuer.IssueAsync(
            issuer.Customer,
            SupportOperatorIssuanceFixture.OperatorClient,
            "openid support:operator",
            TestContext.Current.CancellationToken
        );
        result.Denial.ShouldBe(SupportOperatorIssuanceFixture.IssuanceDenial.None);
        var token = result.Token.ShouldNotBeNull();
        AssertIdentity(
            token,
            issuer.Customer.Subject,
            SupportOperatorIssuanceFixture.OperatorClient
        );
        Scopes(token).ShouldNotContain("support:operator");
        Scopes(token).ShouldNotContain("flights:book");
        (await IsOperatorAuthorizedAsync(token)).ShouldBeFalse();
    }

    [Fact(Timeout = 60_000)]
    public async Task Operator_permission_is_optional_and_existing_customer_flow_keeps_booking_and_same_guid_subject()
    {
        var withoutRequest = await issuer.IssueAsync(
            issuer.Operator,
            SupportOperatorIssuanceFixture.OperatorClient,
            "openid",
            TestContext.Current.CancellationToken
        );
        withoutRequest.Denial.ShouldBe(SupportOperatorIssuanceFixture.IssuanceDenial.None);
        var operatorToken = withoutRequest.Token.ShouldNotBeNull();
        AssertIdentity(
            operatorToken,
            issuer.Operator.Subject,
            SupportOperatorIssuanceFixture.OperatorClient
        );
        Scopes(operatorToken).ShouldNotContain("support:operator");
        Scopes(operatorToken).ShouldNotContain("flights:book");
        (await IsOperatorAuthorizedAsync(operatorToken)).ShouldBeFalse();

        // The same realm account remains the same owner in the unchanged customer context.
        foreach (var identity in new[] { issuer.Operator, issuer.Customer })
        {
            var result = await issuer.IssueAsync(
                identity,
                SupportOperatorIssuanceFixture.CustomerClient,
                "openid flights:book",
                TestContext.Current.CancellationToken
            );
            result.Denial.ShouldBe(SupportOperatorIssuanceFixture.IssuanceDenial.None);
            var customer = result.Token.ShouldNotBeNull();
            AssertIdentity(
                customer,
                identity.Subject,
                SupportOperatorIssuanceFixture.CustomerClient
            );
            Scopes(customer).ShouldContain("flights:book");
            Scopes(customer).ShouldNotContain("support:operator");
            (await IsOperatorAuthorizedAsync(customer)).ShouldBeFalse();
        }
    }

    [Fact(Timeout = 60_000)]
    public async Task Actual_authorization_code_cannot_be_exchanged_with_another_pkce_verifier()
    {
        var result = await issuer.IssueAsync(
            issuer.Operator,
            SupportOperatorIssuanceFixture.OperatorClient,
            "openid support:operator",
            TestContext.Current.CancellationToken,
            wrongVerifier: true
        );
        result.Token.ShouldBeNull();
        result.Denial.ShouldBe(SupportOperatorIssuanceFixture.IssuanceDenial.PkceRejected);
    }

    [Theory(Timeout = 60_000)]
    [InlineData(null)]
    [InlineData("plain")]
    public async Task Operator_client_enforces_the_source_s256_requirement(string? method)
    {
        var result = await issuer.IssueAsync(
            issuer.Operator,
            SupportOperatorIssuanceFixture.OperatorClient,
            "openid support:operator",
            TestContext.Current.CancellationToken,
            challengeMethod: method
        );
        result.Token.ShouldBeNull();
        result.Denial.ShouldBe(SupportOperatorIssuanceFixture.IssuanceDenial.PkceMethodRejected);
    }

    private void AssertIdentity(
        SupportOperatorIssuanceFixture.IssuedToken token,
        Guid subject,
        string client
    )
    {
        token.Principal.TryGetUserId(out var actual).ShouldBeTrue();
        actual.ShouldBe(subject);
        token.Principal.FindFirst("azp")?.Value.ShouldBe(client);
        token.Principal.FindFirst("iss")?.Value.ShouldBe(issuer.Issuer);
        token.Principal.FindAll("aud").Select(claim => claim.Value).ShouldContain("travel-web");
        token.Principal.Identity?.IsAuthenticated.ShouldBeTrue();
    }

    private static string[] Scopes(SupportOperatorIssuanceFixture.IssuedToken token) =>
        token
            .Principal.FindAll("scope")
            .SelectMany(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .ToArray();

    private static async Task<bool> IsOperatorAuthorizedAsync(
        SupportOperatorIssuanceFixture.IssuedToken token
    )
    {
        var services = new ServiceCollection();
        services.AddLogging();
        SupportModule.ConfigureAuthorization(services);
        using var provider = services.BuildServiceProvider();
        return (
            await provider
                .GetRequiredService<IAuthorizationService>()
                .AuthorizeAsync(token.Principal, null, SupportModule.OperatorPolicy)
        ).Succeeded;
    }
}
