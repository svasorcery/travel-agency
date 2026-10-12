using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Shouldly;
using Travel.Modules.Support.Application;
using Travel.Modules.Support.Core;
using Xunit;

namespace Travel.Host.Tests.Integration.Support;

public sealed class SupportHttpTests(SupportApiFixture fixture) : IClassFixture<SupportApiFixture>
{
    private const string Customer = "/api/support/cases/11111111-1111-4111-8111-111111111111";
    private const string Operator =
        "/api/support/operator/cases/11111111-1111-4111-8111-111111111111";
    private const string Action = "22222222-2222-4222-8222-222222222222";
    private const string CreateBody =
        "{\"messageId\":\"22222222-2222-4222-8222-222222222222\",\"title\":\"Fictional title\",\"body\":\"Fictional body\"}";
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<HttpResponseMessage> Send(
        string method,
        string path,
        string? body = null,
        string? token = null
    )
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (token is not null)
            request.Headers.Authorization = new("Bearer", token);
        if (body is not null)
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        return await fixture.Client.SendAsync(request, Ct);
    }

    private static string OwnerToken(Guid? owner = null) =>
        SupportJwtTestTokens.Create(owner ?? SupportHttpService.Owner);

    private static string OperatorToken(
        Guid? actor = null,
        string scope = "openid support:operator",
        string azp = "travel-support-operator"
    ) => SupportJwtTestTokens.Create(actor ?? SupportHttpService.Operator, scope, azp);

    [Fact]
    public async Task Customer_without_booking_scope_can_create_and_receipt_has_exact_metadata_only_shape()
    {
        fixture.Service.Reset();
        using var response = await Send("PUT", Customer, CreateBody, OwnerToken());
        ((int)response.StatusCode).ShouldBe(201);
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
        fixture.Service.Caller!.Subject.ShouldBe(SupportHttpService.Owner);
        fixture.Service.Caller.Kind.ShouldBe(SupportActorKind.Customer);
        fixture.Service.Command.ShouldBeOfType<CreateSupportCase>().OrderId.ShouldBeNull();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        json.RootElement.EnumerateObject()
            .Select(x => x.Name)
            .Order()
            .ShouldBe(
                new[]
                {
                    "caseId",
                    "actionId",
                    "kind",
                    "appliedRevision",
                    "appliedState",
                    "appliedAt",
                    "messageId",
                }.Order()
            );
        json.RootElement.GetProperty("kind").GetString().ShouldBe("Create");
        json.RootElement.ToString().ShouldNotContain("Fictional");
        fixture.Service.Replay = true;
        using var replay = await Send("PUT", Customer, CreateBody, OwnerToken());
        ((int)replay.StatusCode).ShouldBe(200);
    }

    [Theory]
    [InlineData("signature")]
    [InlineData("audience")]
    [InlineData("issuer")]
    [InlineData("expired")]
    [InlineData("subject")]
    public async Task Invalid_signed_identity_is_no_store_and_never_reaches_service(string failure)
    {
        fixture.Service.Reset();
        using var response = await Send(
            "PUT",
            Customer,
            CreateBody,
            SupportJwtTestTokens.Create(SupportHttpService.Owner, failure: failure)
        );
        ((int)response.StatusCode).ShouldBe(401);
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
        fixture.Service.Calls.ShouldBe(0);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldNotContain("Fictional body");
    }

    [Theory]
    [InlineData("openid", "travel-support-operator")]
    [InlineData("openid support:operator", "travel-web")]
    public async Task Operator_requires_both_scope_and_azp_before_body_or_service(
        string scope,
        string azp
    )
    {
        fixture.Service.Reset();
        using var response = await Send(
            "PUT",
            Operator + "/actions/" + Action,
            "{\"kind\":\"Resolve\",\"expectedRevision\":1}",
            OperatorToken(scope: scope, azp: azp)
        );
        ((int)response.StatusCode).ShouldBe(403);
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
        fixture.Service.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task Separate_operator_identity_maps_reply_resolve_and_triage_to_explicit_kinds()
    {
        fixture.Service.Reset();
        using var reply = await Send(
            "PUT",
            Operator + "/messages/" + Action,
            "{\"body\":\"Operator text\",\"expectedRevision\":1,\"kind\":\"Reply\"}",
            OperatorToken()
        );
        ((int)reply.StatusCode).ShouldBe(200);
        fixture
            .Service.Command.ShouldBeOfType<ReplyToSupportCase>()
            .Kind.ShouldBe(SupportActionKind.OperatorReply);
        using var triage = await Send(
            "PUT",
            Operator + "/actions/" + Action,
            "{\"kind\":\"SetTriage\",\"expectedRevision\":1,\"category\":\"Refund\",\"priority\":\"High\"}",
            OperatorToken()
        );
        ((int)triage.StatusCode).ShouldBe(200);
        fixture
            .Service.Command.ShouldBeOfType<ActOnSupportCase>()
            .Category.ShouldBe(ManualCategory.Refund);
        using var resolve = await Send(
            "PUT",
            Operator + "/actions/" + Action,
            "{\"kind\":\"Resolve\",\"expectedRevision\":1}",
            OperatorToken()
        );
        fixture
            .Service.Command.ShouldBeOfType<ActOnSupportCase>()
            .Kind.ShouldBe(SupportActionKind.Resolve);
    }

    [Theory]
    [InlineData(
        "{\"messageId\":\"22222222-2222-4222-8222-222222222222\",\"title\":\"private sentinel\",\"body\":\"private sentinel\",\"ownerId\":\"33333333-3333-4333-8333-333333333333\"}"
    )]
    [InlineData(
        "{\"messageId\":\"22222222-2222-4222-8222-222222222222\",\"title\":\"private sentinel\",\"title\":\"duplicate\",\"body\":\"x\"}"
    )]
    [InlineData(
        "{\"messageId\":\"00000000-0000-0000-0000-000000000000\",\"title\":\"private sentinel\",\"body\":\"x\"}"
    )]
    [InlineData(
        "{\"messageId\":\"22222222-2222-4222-8222-222222222222\",\"title\":\" \",\"body\":\"private sentinel\"}"
    )]
    public async Task Invalid_JSON_fields_are_fixed_no_echo_and_no_effect(string body)
    {
        fixture.Service.Reset();
        using var response = await Send("PUT", Customer, body, OwnerToken());
        ((int)response.StatusCode).ShouldBe(400);
        fixture.Service.Calls.ShouldBe(0);
        var error = await response.Content.ReadAsStringAsync(Ct);
        error.ShouldContain("Support.InvalidInput");
        error.ShouldNotContain("private sentinel");
        error.ShouldNotContain("ownerId");
    }

    [Fact]
    public async Task Scalar_body_limits_and_request_byte_limit_reject_without_echo()
    {
        fixture.Service.Reset();
        foreach (
            var body in new[]
            {
                CreateBody.Replace("Fictional title", new string('a', 121)),
                CreateBody.Replace("Fictional body", new string('b', 4001)),
            }
        )
        {
            using var response = await Send("PUT", Customer, body, OwnerToken());
            ((int)response.StatusCode).ShouldBe(400);
        }
        using var tooLarge = await Send("PUT", Customer, new string('z', 33 * 1024), OwnerToken());
        ((int)tooLarge.StatusCode).ShouldBe(413);
        (await tooLarge.Content.ReadAsStringAsync(Ct)).ShouldContain("Support.RequestTooLarge");
        fixture.Service.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task Foreign_customer_and_other_operator_receipts_are_concealed()
    {
        fixture.Service.Reset();
        using var foreign = await Send(
            "GET",
            Customer + "/actions/" + Action,
            token: OwnerToken(Guid.NewGuid())
        );
        ((int)foreign.StatusCode).ShouldBe(404);
        using var otherOperator = await Send(
            "GET",
            Operator + "/actions/" + Action,
            token: OperatorToken(Guid.NewGuid())
        );
        ((int)otherOperator.StatusCode).ShouldBe(404);
    }

    [Fact]
    public async Task Unavailable_list_is_an_error_and_case_nulls_and_strings_follow_wire_contract()
    {
        fixture.Service.Reset();
        fixture.Service.Failure = SupportErrors.Unavailable;
        using var unavailable = await Send("GET", "/api/support/cases", token: OwnerToken());
        ((int)unavailable.StatusCode).ShouldBe(503);
        (await unavailable.Content.ReadAsStringAsync(Ct)).ShouldContain("Support.Unavailable");
        fixture.Service.Reset();
        using var detail = await Send("GET", Customer, token: OwnerToken());
        using var json = JsonDocument.Parse(await detail.Content.ReadAsStringAsync(Ct));
        foreach (
            var name in new[]
            {
                "orderId",
                "orderVerifiedAt",
                "orderStreamVersion",
                "resolvedAt",
                "purgeAt",
            }
        )
            json.RootElement.GetProperty(name).ValueKind.ShouldBe(JsonValueKind.Null);
        json.RootElement.GetProperty("state").GetString().ShouldBe("Open");
        json.RootElement.GetProperty("title")
            .GetString()
            .ShouldBe("<script>Fictional private title</script>");
    }

    [Fact]
    public async Task Linked_order_precommit_errors_keep_reserved_types_and_generic_errors_keep_unknown_types()
    {
        foreach (
            var scenario in new[]
            {
                (
                    Error: ErrorOr.Error.NotFound("Support.OrderLinkNotFound", "Fixed error."),
                    Status: 404,
                    Code: "Support.OrderLinkNotFound"
                ),
                (
                    Error: ErrorOr.Error.Failure("Support.OrderLinkUnavailable", "Fixed error."),
                    Status: 503,
                    Code: "Support.OrderLinkUnavailable"
                ),
                (Error: SupportErrors.NotFound, Status: 404, Code: "Support.NotFound"),
                (Error: SupportErrors.Unavailable, Status: 503, Code: "Support.Unavailable"),
            }
        )
        {
            fixture.Service.Reset();
            fixture.Service.Failure = scenario.Error;
            using var response = await Send("PUT", Customer, CreateBody, OwnerToken());
            ((int)response.StatusCode).ShouldBe(scenario.Status);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
            json.RootElement.GetProperty("type")
                .GetString()
                .ShouldBe("https://travel.local/errors/" + scenario.Code);
        }
    }

    [Fact]
    public async Task Query_fields_and_enum_numbers_are_rejected_with_fixed_errors()
    {
        fixture.Service.Reset();
        foreach (
            var suffix in new[]
            {
                "?state=0",
                "?priority=High&priority=Normal",
                "?ownerId=private-sentinel",
                "?category=unclassified",
            }
        )
        {
            using var response = await Send(
                "GET",
                "/api/support/cases" + suffix,
                token: OwnerToken()
            );
            ((int)response.StatusCode).ShouldBe(400);
            (await response.Content.ReadAsStringAsync(Ct)).ShouldNotContain("private-sentinel");
        }
        fixture.Service.Calls.ShouldBe(0);
    }
}
