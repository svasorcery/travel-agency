using System.Net;
using System.Net.Http.Headers;
using Shouldly;
using Travel.Modules.Flights.Application.Cancellation;
using Xunit;

namespace Travel.Host.Tests.Integration.Flights;

public sealed class CancellationJwtHttpTests(CancellationApiFixture fixture)
    : IClassFixture<CancellationApiFixture>
{
    [Theory]
    [InlineData("signature", 401)]
    [InlineData("issuer", 401)]
    [InlineData("audience", 401)]
    [InlineData("expired", 401)]
    [InlineData("subject", 401)]
    [InlineData("scope", 403)]
    public async Task Invalid_signed_jwt_never_reaches_bus(string failure, int status)
    {
        fixture.Probe.Reset();
        using var request = Request(
            "/api/flights/orders/" + CancellationApiFixture.Aggregate + "/cancellation",
            ProfileJwtTestTokens.Create(CancellationApiFixture.Owner, failure)
        );
        using var response = await fixture.Client.SendAsync(
            request,
            TestContext.Current.CancellationToken
        );
        ((int)response.StatusCode).ShouldBe(status);
        response.Headers.CacheControl!.NoStore.ShouldBe(true);
        fixture.Probe.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Signed_subject_supplies_owner_and_ignores_demo_header()
    {
        fixture.Probe.Reset();
        var token = ProfileJwtTestTokens.Create(CancellationApiFixture.Owner);
        using var request = Request(
            "/api/flights/orders/" + CancellationApiFixture.Aggregate + "/cancellation",
            token
        );
        request.Headers.Add("X-Travel-Demo-Owner", Guid.NewGuid().ToString());
        using var response = await fixture.Client.SendAsync(
            request,
            TestContext.Current.CancellationToken
        );
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        fixture
            .Probe.Calls.Single()
            .ShouldBeOfType<GetCancellationStatusQuery>()
            .UserId.ShouldBe(CancellationApiFixture.Owner);
        (
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)
        ).ShouldNotContain(token);
    }

    [Theory]
    [InlineData("flights:book", 403)]
    [InlineData("flights:cancellation-review", 200)]
    public async Task Review_requires_its_own_scope(string scope, int expected)
    {
        fixture.Probe.Reset();
        using var request = Request(
            "/api/flights/orders/" + CancellationApiFixture.Aggregate + "/cancellation-review",
            ProfileJwtTestTokens.Create(CancellationApiFixture.Owner, scope: scope)
        );
        using var response = await fixture.Client.SendAsync(
            request,
            TestContext.Current.CancellationToken
        );
        ((int)response.StatusCode).ShouldBe(expected);
        fixture.Probe.Calls.Count.ShouldBe(expected == 200 ? 1 : 0);
        response.Headers.CacheControl!.NoStore.ShouldBe(true);
    }

    internal static HttpRequestMessage Request(string path, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }
}
