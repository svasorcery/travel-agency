using System.Net;
using System.Text;
using Shouldly;
using Travel.Modules.Flights.Application.Cancellation;
using Xunit;

namespace Travel.Host.Tests.Integration.Flights;

public sealed class CancellationHttpTests(CancellationApiFixture fixture)
    : IClassFixture<CancellationApiFixture>
{
    private static string Body =>
        "{\"aggregateId\":\""
        + CancellationApiFixture.Aggregate
        + "\",\"operationId\":\"22222222-2222-2222-2222-222222222222\",\"expectedBookingVersion\":3}";

    [Theory]
    [InlineData("ManualReviewRequired", true, 202)]
    [InlineData("ManualReviewRequired", false, 200)]
    [InlineData("Succeeded", false, 200)]
    public async Task Refresh_reports_accepted_read_work_separately_from_business_phase(
        string phase,
        bool pending,
        int expected
    )
    {
        fixture.Probe.Reset();
        fixture.Probe.StatusOverride = new CancellationStatusResult(
            CancellationApiFixture.Aggregate,
            "Held",
            8,
            null,
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            true,
            new(
                Guid.Parse("22222222-2222-2222-2222-222222222222"),
                4,
                phase,
                "Confirmation",
                "Unknown",
                "None",
                "ManualVerificationRequired",
                "ConfirmationClaimed",
                null,
                null,
                null,
                pending
            ),
            null,
            DateTimeOffset.UtcNow
        );
        using var request = CancellationJwtHttpTests.Request(
            "/api/flights/cancellations/refresh",
            ProfileJwtTestTokens.Create(CancellationApiFixture.Owner)
        );
        request.Method = HttpMethod.Post;
        request.Content = new StringContent(
            "{\"aggregateId\":\""
                + CancellationApiFixture.Aggregate
                + "\",\"operationId\":\"22222222-2222-2222-2222-222222222222\",\"expectedOperationRevision\":4,\"refreshRequestId\":\"55555555-5555-5555-5555-555555555555\"}",
            Encoding.UTF8,
            "application/json"
        );
        using var response = await fixture.Client.SendAsync(
            request,
            TestContext.Current.CancellationToken
        );
        ((int)response.StatusCode).ShouldBe(expected);
        fixture
            .Probe.Calls.Single()
            .ShouldBeOfType<RefreshCancellationCommand>()
            .IsOperator.ShouldBeFalse();
    }

    [Theory]
    [InlineData("valid", 200)]
    [InlineData("unknown", 400)]
    [InlineData("duplicate", 400)]
    [InlineData("too-large", 413)]
    [InlineData("missing", 400)]
    public async Task Bounded_strict_body_precedes_bus(string mode, int expected)
    {
        fixture.Probe.Reset();
        var body = mode switch
        {
            "unknown" => Body[..^1] + ",\"actor\":\"invented\"}",
            "duplicate" => Body[..^1] + ",\"ExpectedBookingVersion\":3}",
            "too-large" => new string(' ', 16 * 1024 + 1),
            "missing" => "{}",
            _ => Body,
        };
        using var request = CancellationJwtHttpTests.Request(
            "/api/flights/cancellations/prepare",
            ProfileJwtTestTokens.Create(CancellationApiFixture.Owner)
        );
        request.Method = HttpMethod.Post;
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = await fixture.Client.SendAsync(
            request,
            TestContext.Current.CancellationToken
        );
        ((int)response.StatusCode).ShouldBe(expected);
        response.Headers.CacheControl!.NoStore.ShouldBe(true);
        fixture.Probe.Calls.Count.ShouldBe(expected == 200 ? 1 : 0);
        if (expected == 200)
            fixture
                .Probe.Calls.Single()
                .ShouldBeOfType<PrepareCancellationCommand>()
                .Fingerprint.Length.ShouldBe(64);
    }

    [Fact]
    public async Task Owner_scope_cannot_resolve_or_claim_operator_actor()
    {
        fixture.Probe.Reset();
        using var request = CancellationJwtHttpTests.Request(
            "/api/flights/cancellations/review/resolve",
            ProfileJwtTestTokens.Create(CancellationApiFixture.Owner)
        );
        request.Method = HttpMethod.Post;
        request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        using var response = await fixture.Client.SendAsync(
            request,
            TestContext.Current.CancellationToken
        );
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        fixture.Probe.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Foreign_owner_result_is_404_without_private_metadata()
    {
        fixture.Probe.Reset();
        fixture.Probe.ForeignOwner = true;
        using var request = CancellationJwtHttpTests.Request(
            "/api/flights/orders/" + CancellationApiFixture.Aggregate + "/cancellation",
            ProfileJwtTestTokens.Create(Guid.NewGuid())
        );
        using var response = await fixture.Client.SendAsync(
            request,
            TestContext.Current.CancellationToken
        );
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        text.ShouldNotContain("ProviderOrderRef");
        text.ShouldNotContain("DispatchOwnerInstanceId");
    }
}
