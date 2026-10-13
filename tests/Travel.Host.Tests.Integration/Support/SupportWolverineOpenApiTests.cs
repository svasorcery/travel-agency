using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Travel.Modules.Support.Api.Composition;
using Travel.Modules.Support.Api.Contracts;
using Travel.Modules.Support.Application;
using Travel.Modules.Support.Core;
using Xunit;

namespace Travel.Host.Tests.Integration.Support;

public sealed class SupportWolverineOpenApiTests
{
    private static readonly (string Method, string Pattern, bool Operator)[] Endpoints =
    [
        ("PUT", "/api/support/cases/{caseId}", false),
        ("GET", "/api/support/cases", false),
        ("GET", "/api/support/cases/{caseId}", false),
        ("GET", "/api/support/cases/{caseId}/messages", false),
        ("PUT", "/api/support/cases/{caseId}/messages/{messageId}", false),
        ("GET", "/api/support/cases/{caseId}/actions/{actionId}", false),
        ("GET", "/api/support/operator/cases", true),
        ("GET", "/api/support/operator/cases/{caseId}", true),
        ("GET", "/api/support/operator/cases/{caseId}/messages", true),
        ("PUT", "/api/support/operator/cases/{caseId}/messages/{messageId}", true),
        ("PUT", "/api/support/operator/cases/{caseId}/actions/{actionId}", true),
        ("GET", "/api/support/operator/cases/{caseId}/actions/{actionId}", true),
    ];

    [Fact]
    public async Task Actual_Wolverine_mapping_contains_only_twelve_Support_routes_with_their_actor_policies()
    {
        await using var app = await SupportWolverineOpenApiFixture.CreateAsync(
            TestContext.Current.CancellationToken
        );
        var routes = app
            .Services.GetRequiredService<EndpointDataSource>()
            .Endpoints.OfType<RouteEndpoint>()
            .Where(route =>
                route.RoutePattern.RawText?.StartsWith("/api/", StringComparison.Ordinal) == true
            )
            .ToArray();
        routes.Length.ShouldBe(12);
        var mapped = routes
            .SelectMany(route =>
                route
                    .Metadata.GetMetadata<HttpMethodMetadata>()!
                    .HttpMethods.Select(method =>
                        (Method: method, Pattern: Normalize(route.RoutePattern.RawText!))
                    )
            )
            .ToArray();
        mapped.ShouldBe(
            Endpoints.Select(endpoint => (endpoint.Method, endpoint.Pattern)).ToArray(),
            ignoreOrder: true
        );
        foreach (var route in routes)
        {
            var authorization = route.Metadata.OfType<IAuthorizeData>().ToArray();
            authorization.ShouldNotBeEmpty();
            if (
                route.RoutePattern.RawText!.StartsWith(
                    "/api/support/operator/",
                    StringComparison.Ordinal
                )
            )
                authorization.ShouldContain(rule => rule.Policy == SupportModule.OperatorPolicy);
            else
                authorization.ShouldNotContain(rule => !string.IsNullOrEmpty(rule.Policy));
            authorization.ShouldNotContain(rule => rule.Policy == "flights:book");
        }
    }

    [Fact]
    public async Task All_twelve_production_handlers_compile_and_call_only_the_controlled_workflow_service()
    {
        await using var app = await SupportWolverineOpenApiFixture.CreateAsync(
            TestContext.Current.CancellationToken
        );
        using var client = app.GetTestClient();
        var service = (SupportHttpService)app.Services.GetRequiredService<ISupportCaseService>();
        foreach (var endpoint in Endpoints)
        {
            service.Reset();
            var path = endpoint
                .Pattern.Replace(
                    "{caseId}",
                    SupportHttpService.Case.ToString("D"),
                    StringComparison.Ordinal
                )
                .Replace(
                    "{messageId}",
                    SupportHttpService.Action.ToString("D"),
                    StringComparison.Ordinal
                )
                .Replace(
                    "{actionId}",
                    SupportHttpService.Action.ToString("D"),
                    StringComparison.Ordinal
                );
            using var request = new HttpRequestMessage(new HttpMethod(endpoint.Method), path);
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                SupportJwtTestTokens.Create(
                    endpoint.Operator ? SupportHttpService.Operator : SupportHttpService.Owner,
                    endpoint.Operator ? "openid support:operator" : "openid",
                    endpoint.Operator ? "travel-support-operator" : "travel-web"
                )
            );
            if (endpoint.Method == "PUT")
            {
                var body =
                    endpoint.Pattern.EndsWith("/actions/{actionId}", StringComparison.Ordinal)
                        ? "{\"kind\":\"Resolve\",\"expectedRevision\":1}"
                    : endpoint.Pattern.EndsWith("/messages/{messageId}", StringComparison.Ordinal)
                        ? "{\"body\":\"Fictional Wolverine body\",\"expectedRevision\":1,\"kind\":\"Reply\"}"
                    : "{\"messageId\":\"22222222-2222-4222-8222-222222222222\",\"title\":\"Fictional Wolverine title\",\"body\":\"Fictional Wolverine body\"}";
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            }
            using var response = await client.SendAsync(
                request,
                TestContext.Current.CancellationToken
            );
            ((int)response.StatusCode).ShouldBe(
                endpoint.Method == "PUT" && endpoint.Pattern == "/api/support/cases/{caseId}"
                    ? 201
                    : 200,
                await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)
            );
            response.Headers.CacheControl!.NoStore.ShouldBeTrue();
            service.Calls.ShouldBe(1);
            service.Caller!.Subject.ShouldBe(
                endpoint.Operator ? SupportHttpService.Operator : SupportHttpService.Owner
            );
            service.Caller.Kind.ShouldBe(
                endpoint.Operator ? SupportActorKind.Operator : SupportActorKind.Customer
            );
        }
    }

    [Fact]
    public async Task Real_OpenApi_documents_exact_Support_DTO_fields_and_metadata_only_receipts()
    {
        await using var app = await SupportWolverineOpenApiFixture.CreateAsync(
            TestContext.Current.CancellationToken
        );
        using var client = app.GetTestClient();
        var body = await client.GetStringAsync(
            "/openapi/v1.json",
            TestContext.Current.CancellationToken
        );
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        var paths = root.GetProperty("paths");
        var operations = paths
            .EnumerateObject()
            .SelectMany(path =>
                path.Value.EnumerateObject()
                    .Where(operation =>
                        operation.Name is "get" or "put" or "post" or "delete" or "patch"
                    )
                    .Select(operation =>
                        (Method: operation.Name.ToUpperInvariant(), Pattern: path.Name)
                    )
            )
            .ToArray();
        operations.ShouldBe(
            Endpoints.Select(endpoint => (endpoint.Method, endpoint.Pattern)).ToArray(),
            ignoreOrder: true
        );
        var schemas = root.GetProperty("components").GetProperty("schemas");
        Fields(schemas, nameof(CreateSupportCaseDto), ["messageId", "title", "body", "orderId"]);
        Fields(schemas, nameof(ReplySupportCaseDto), ["body", "expectedRevision", "kind"]);
        Fields(
            schemas,
            nameof(ActSupportCaseDto),
            ["kind", "expectedRevision", "category", "priority"]
        );
        Fields(
            schemas,
            nameof(SupportReceiptDto),
            [
                "caseId",
                "actionId",
                "kind",
                "appliedRevision",
                "appliedState",
                "appliedAt",
                "messageId",
            ]
        );
        Fields(
            schemas,
            nameof(SupportCaseDto),
            [
                "caseId",
                "ownerId",
                "title",
                "state",
                "revision",
                "category",
                "priority",
                "triageSource",
                "orderId",
                "orderVerifiedAt",
                "orderStreamVersion",
                "createdAt",
                "updatedAt",
                "resolvedAt",
                "purgeAt",
            ]
        );
        Fields(
            schemas,
            nameof(SupportMessageDto),
            ["caseId", "messageId", "actorKind", "body", "writtenAtRevision", "createdAt"]
        );
        Fields(schemas, nameof(SupportCasePageDto), ["items", "nextCursor"]);
        Fields(schemas, nameof(SupportMessagePageDto), ["caseId", "items", "nextCursor"]);
        foreach (
            var name in new[]
            {
                nameof(CreateSupportCaseDto),
                nameof(ReplySupportCaseDto),
                nameof(ActSupportCaseDto),
            }
        )
        {
            var properties = schemas.GetProperty(name).GetProperty("properties");
            foreach (
                var forbidden in new[]
                {
                    "ownerId",
                    "actorId",
                    "actorKind",
                    "scope",
                    "azp",
                    "authorizedParty",
                    "protectedTitle",
                    "protectedBody",
                }
            )
                properties
                    .TryGetProperty(forbidden, out _)
                    .ShouldBeFalse(name + " must not take caller authority or stored ciphertext.");
        }
        var receipt = schemas.GetProperty(nameof(SupportReceiptDto)).GetProperty("properties");
        foreach (
            var forbidden in new[]
            {
                "ownerId",
                "actorId",
                "body",
                "title",
                "expectedRevision",
                "requestedOrderId",
                "previousCategory",
                "requestedCategory",
            }
        )
            receipt
                .TryGetProperty(forbidden, out _)
                .ShouldBeFalse("Public receipt is metadata only.");
        RequestSchema(paths.GetProperty("/api/support/cases/{caseId}").GetProperty("put"))
            .ShouldBe(nameof(CreateSupportCaseDto));
        RequestSchema(
                paths
                    .GetProperty("/api/support/cases/{caseId}/messages/{messageId}")
                    .GetProperty("put")
            )
            .ShouldBe(nameof(ReplySupportCaseDto));
        RequestSchema(
                paths
                    .GetProperty("/api/support/operator/cases/{caseId}/messages/{messageId}")
                    .GetProperty("put")
            )
            .ShouldBe(nameof(ReplySupportCaseDto));
        RequestSchema(
                paths
                    .GetProperty("/api/support/operator/cases/{caseId}/actions/{actionId}")
                    .GetProperty("put")
            )
            .ShouldBe(nameof(ActSupportCaseDto));
        foreach (
            var endpoint in Endpoints.Where(endpoint =>
                endpoint.Method == "PUT"
                || endpoint.Pattern.EndsWith("/actions/{actionId}", StringComparison.Ordinal)
            )
        )
        {
            var operation = paths
                .GetProperty(endpoint.Pattern)
                .GetProperty(endpoint.Method.ToLowerInvariant());
            var status =
                endpoint.Method == "PUT" && endpoint.Pattern == "/api/support/cases/{caseId}"
                    ? "201"
                    : "200";
            var responseSchema = operation
                .GetProperty("responses")
                .GetProperty(status)
                .GetProperty("content")
                .GetProperty("application/json")
                .GetProperty("schema")
                .GetProperty("$ref")
                .GetString();
            responseSchema.ShouldBe("#/components/schemas/" + nameof(SupportReceiptDto));
        }
    }

    private static void Fields(JsonElement schemas, string schema, string[] fields) =>
        schemas
            .GetProperty(schema)
            .GetProperty("properties")
            .EnumerateObject()
            .Select(property => property.Name)
            .ShouldBe(fields, ignoreOrder: true);

    private static string RequestSchema(JsonElement operation) =>
        operation
            .GetProperty("requestBody")
            .GetProperty("content")
            .GetProperty("application/json")
            .GetProperty("schema")
            .GetProperty("$ref")
            .GetString()!["#/components/schemas/".Length..];

    private static string Normalize(string pattern) =>
        pattern
            .Replace("{caseId:guid}", "{caseId}", StringComparison.Ordinal)
            .Replace("{messageId:guid}", "{messageId}", StringComparison.Ordinal)
            .Replace("{actionId:guid}", "{actionId}", StringComparison.Ordinal);
}
