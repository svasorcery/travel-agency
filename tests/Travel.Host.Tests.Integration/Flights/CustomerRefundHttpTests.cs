using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Shouldly;
using Travel.Modules.Flights.Application.Refunds;
using Xunit;

namespace Travel.Host.Tests.Integration.Flights;

// Source-only locally: ordinary Host test assembly has an assembly PII fixture.
public sealed class CustomerRefundHttpTests(CancellationApiFixture fixture)
    : IClassFixture<CancellationApiFixture>
{
    private static string Path(bool review = false) =>
        "/api/flights/orders/"
        + CancellationApiFixture.Aggregate
        + "/customer-refund"
        + (review ? "/review" : "");

    private static HttpRequestMessage Request(
        HttpMethod method,
        bool review,
        string? scope = "flights:book",
        string? body = null
    )
    {
        var r = new HttpRequestMessage(method, Path(review));
        if (scope is not null)
            r.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                ProfileJwtTestTokens.Create(CancellationApiFixture.Owner, scope: scope)
            );
        if (body is not null)
            r.Content = new StringContent(body, Encoding.UTF8, "application/json");
        return r;
    }

    [Theory]
    [InlineData(null, 401)]
    [InlineData("flights:cancellation-review", 403)]
    [InlineData("flights:book", 200)]
    public async Task Owner_read_is_authorized_no_store_and_has_exact_money(
        string? scope,
        int expected
    )
    {
        fixture.Probe.Reset();
        using var r = Request(HttpMethod.Get, false, scope);
        using var response = await fixture.Client.SendAsync(
            r,
            TestContext.Current.CancellationToken
        );
        ((int)response.StatusCode).ShouldBe(expected);
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
        if (expected != 200)
        {
            fixture.Probe.Calls.ShouldBeEmpty();
            return;
        }
        fixture
            .Probe.Calls.Single()
            .ShouldBeOfType<GetCustomerRefundStatusQuery>()
            .UserId.ShouldBe(CancellationApiFixture.Owner);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var json = JsonDocument.Parse(body);
        json.RootElement.GetProperty("amount")
            .GetProperty("amount")
            .GetString()
            .ShouldBe("80.000000000000000001");
        json.RootElement.GetProperty("customerCredit").GetString().ShouldBe("NotEstablished");
        foreach (
            var secret in new[]
            {
                "paymentReference",
                "refundId",
                "receiptReference",
                "ownerId",
                "providerOrderRef",
                "Authorization",
            }
        )
            body.ShouldNotContain(secret);
    }

    [Theory]
    [InlineData("flights:book", 403)]
    [InlineData("flights:cancellation-review", 403)]
    [InlineData("flights:customer-refund-review", 200)]
    public async Task Review_has_separate_authority_and_uses_jwt_actor(string scope, int expected)
    {
        fixture.Probe.Reset();
        using var r = Request(HttpMethod.Get, true, scope);
        using var response = await fixture.Client.SendAsync(
            r,
            TestContext.Current.CancellationToken
        );
        ((int)response.StatusCode).ShouldBe(expected);
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
        if (expected == 200)
            fixture
                .Probe.Calls.Single()
                .ShouldBeOfType<GetCustomerRefundReviewQuery>()
                .ActorId.ShouldBe(CancellationApiFixture.Owner);
        else
            fixture.Probe.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Foreign_owner_get_is_hidden_and_never_exposes_a_case()
    {
        fixture.Probe.Reset();
        fixture.Probe.ForeignOwner = true;
        using var r = Request(HttpMethod.Get, false);
        using var response = await fixture.Client.SendAsync(
            r,
            TestContext.Current.CancellationToken
        );
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
    }

    [Theory]
    [InlineData("{}", 400)]
    [InlineData(
        "{\"refundId\":\"00000000-0000-0000-0000-000000000123\",\"expectedRevision\":1,\"resolutionId\":\"00000000-0000-0000-0000-000000000456\",\"decision\":\"Inconclusive\",\"evidenceReference\":\"EVIDENCE-FICTIONAL\",\"extra\":true}",
        400
    )]
    [InlineData("{\"decision\":\"Sent\",\"decision\":\"Inconclusive\"}", 400)]
    [InlineData(
        "{\"refundId\":\"00000000-0000-0000-0000-000000000123\",\"expectedRevision\":1,\"resolutionId\":\"00000000-0000-0000-0000-000000000456\",\"decision\":\"Inconclusive\",\"evidenceReference\":\"evidence_fictional\"}",
        400
    )]
    public async Task Malformed_or_extra_fields_never_reach_the_bus(string body, int expected)
    {
        fixture.Probe.Reset();
        using var r = Request(HttpMethod.Post, true, "flights:customer-refund-review", body);
        using var response = await fixture.Client.SendAsync(
            r,
            TestContext.Current.CancellationToken
        );
        ((int)response.StatusCode).ShouldBe(expected);
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
        fixture.Probe.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Oversize_body_is_bounded_and_wrong_scope_is_checked_first()
    {
        foreach (var scope in new[] { "flights:book", "flights:customer-refund-review" })
        {
            fixture.Probe.Reset();
            using var r = Request(HttpMethod.Post, true, scope, new string('x', 17 * 1024));
            using var response = await fixture.Client.SendAsync(
                r,
                TestContext.Current.CancellationToken
            );
            ((int)response.StatusCode).ShouldBe(scope == "flights:book" ? 403 : 413);
            response.Headers.CacheControl!.NoStore.ShouldBeTrue();
            fixture.Probe.Calls.ShouldBeEmpty();
        }
    }

    [Fact]
    public async Task Manual_post_emits_only_a_resolution_command_with_authenticated_actor()
    {
        fixture.Probe.Reset();
        var body =
            "{\"refundId\":\"00000000-0000-0000-0000-000000000123\",\"expectedRevision\":1,\"resolutionId\":\"00000000-0000-0000-0000-000000000456\",\"decision\":\"Inconclusive\",\"evidenceReference\":\"EVIDENCE-FICTIONAL\"}";
        using var r = Request(HttpMethod.Post, true, "flights:customer-refund-review", body);
        using var response = await fixture.Client.SendAsync(
            r,
            TestContext.Current.CancellationToken
        );
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        fixture
            .Probe.Calls.Single()
            .ShouldBeOfType<ResolveCustomerRefundReviewCommand>()
            .ActorId.ShouldBe(CancellationApiFixture.Owner);
    }
}
