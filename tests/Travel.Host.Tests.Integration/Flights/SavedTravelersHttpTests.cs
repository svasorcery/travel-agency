using System.Net;
using System.Text;
using System.Text.Json;
using ErrorOr;
using Shouldly;
using Travel.Modules.Flights.Application.Privacy;
using Travel.Modules.Flights.Application.SavedTravelers;
using Xunit;

namespace Travel.Host.Tests.Integration.Flights;

public sealed class SavedTravelersHttpTests(FlightsApiFixture fixture)
    : IClassFixture<FlightsApiFixture>
{
    private const string Body =
        "{\"title\":\"mr\",\"givenName\":\"John\",\"familyName\":\"Smith\",\"dateOfBirth\":\"1990-01-01\",\"gender\":\"male\",\"email\":\"john@example.test\",\"phone\":\"+12025550123\"}";
    private const string Path = "/api/flights/travelers/11111111-1111-1111-1111-111111111111";
    private const string ETag = "\"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa\"";
    private static readonly Guid Owner = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Theory]
    [InlineData("GET", "/api/flights/travelers")]
    [InlineData("GET", "/API/FLIGHTS/TRAVELERS/")]
    [InlineData("GET", Path)]
    [InlineData("PUT", Path)]
    [InlineData("DELETE", Path)]
    public async Task Anonymous_profile_requests_are_no_store(string method, string path)
    {
        using var request = Request(method, path, owner: null);
        await AssertResponse(request, 401);
    }

    [Theory]
    [InlineData("GET", "/api/flights/travelers")]
    [InlineData("GET", Path)]
    [InlineData("PUT", Path)]
    [InlineData("DELETE", Path)]
    public async Task Profiles_require_scope_and_valid_subject_before_service(
        string method,
        string path
    )
    {
        fixture.SavedTravelers.Reset();
        using var noScope = Request(method, path, scope: false);
        await AssertResponse(noScope, 403);
        using var badSubject = Request(method, path, owner: "invalid-sub");
        await AssertResponse(badSubject, 401);
        fixture.SavedTravelers.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task Authorized_reads_return_exact_shapes_and_owner_ignores_forged_demo_header()
    {
        fixture.SavedTravelers.Reset();
        fixture.IdempotencyStore.Reset();
        using var request = Request("GET", Path);
        request.Headers.Add("X-Travel-Demo-Owner", Guid.NewGuid().ToString());
        using var response = await AssertResponse(request, 200);
        (response.Headers.ETag?.Tag).ShouldBe(ETag);
        using var json = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)
        );
        json.RootElement.EnumerateObject()
            .Select(p => p.Name)
            .ShouldBe(["id", "revision", "details"]);
        json.RootElement.GetProperty("details").EnumerateObject().Count().ShouldBe(7);
        fixture.SavedTravelers.Owner.ShouldBe(Owner);
        fixture.IdempotencyStore.TryBeginCount.ShouldBe(0);
        using var list = Request("GET", "/api/flights/travelers?offset=20");
        using var listResponse = await AssertResponse(list, 200);
        listResponse.Headers.ETag.ShouldBeNull();
        using var page = JsonDocument.Parse(
            await listResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)
        );
        page.RootElement.EnumerateObject()
            .Select(p => p.Name)
            .ShouldBe(["items", "offset", "hasMore"]);
        page.RootElement.GetProperty("offset").GetInt32().ShouldBe(20);
    }

    [Theory]
    [InlineData(null, null, 428)]
    [InlineData("*", null, 201)]
    [InlineData(null, ETag, 200)]
    [InlineData("*", ETag, 400)]
    [InlineData("\"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa\"", null, 400)]
    [InlineData(null, "*", 400)]
    [InlineData(null, "W/\"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa\"", 400)]
    [InlineData(null, "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", 400)]
    [InlineData(null, "\"AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA\"", 400)]
    [InlineData(
        null,
        "\"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa\",\"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa\"",
        400
    )]
    [InlineData(null, "\"00000000-0000-0000-0000-000000000000\"", 400)]
    [InlineData(null, "\"bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb\"", 412)]
    public async Task Put_enforces_strict_conditional_protocol(
        string? none,
        string? match,
        int status
    )
    {
        fixture.SavedTravelers.Reset();
        fixture.IdempotencyStore.Reset();
        using var request = Request("PUT", Path, Body, none, match);
        using var response = await AssertResponse(request, status);
        if (status is 200 or 201)
        {
            (response.Headers.ETag?.Tag).ShouldBe(ETag);
            using var json = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)
            );
            json.RootElement.EnumerateObject().Select(p => p.Name).ShouldBe(["id", "revision"]);
            fixture.SavedTravelers.Owner.ShouldBe(Owner);
            (fixture.SavedTravelers.SubmittedDetails?.ToString()).ShouldBe("SavedTravelerDetails");
        }
        else if (status != 412)
            fixture.SavedTravelers.Calls.ShouldBe(0);
        fixture.IdempotencyStore.TryBeginCount.ShouldBe(0);
    }

    [Theory]
    [InlineData(null, null, "", 428)]
    [InlineData(null, ETag, "", 204)]
    [InlineData("*", null, "", 400)]
    [InlineData("*", ETag, "", 400)]
    [InlineData(null, "*", "", 400)]
    [InlineData(null, ETag, "{}", 400)]
    public async Task Delete_requires_exact_revision_and_empty_body(
        string? none,
        string? match,
        string body,
        int status
    )
    {
        fixture.SavedTravelers.Reset();
        using var request = Request("DELETE", Path, body, none, match);
        using var response = await AssertResponse(request, status);
        if (status == 204)
            (
                await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)
            ).ShouldBeEmpty();
        else
            fixture.SavedTravelers.Calls.ShouldBe(0);
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public async Task Foreign_resources_are_not_found(string method)
    {
        using var request = Request(
            method,
            "/api/flights/travelers/" + FakeSavedTravelerService.ForeignId,
            method == "PUT" ? Body : "",
            match: ETag
        );
        await AssertResponse(request, 404);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{\"secret-john@example.test\":1}")]
    [InlineData("{\"title\":\"mr\",\"TITLE\":\"mr\"}")]
    [InlineData("{\"title\":null}")]
    [InlineData("{\"title\":123}")]
    [InlineData("{\"dateOfBirth\":\"2026-02-30\"}")]
    [InlineData("{\"ownerUserId\":\"john@example.test\"}")]
    [InlineData("{\"bookingPassengerId\":\"john@example.test\"}")]
    [InlineData("{\"title\":\"john@example.test\"")]
    public async Task Malformed_unknown_duplicate_and_unusable_json_is_safe_before_binding(
        string body
    )
    {
        fixture.SavedTravelers.Reset();
        fixture.Logs.Messages.Clear();
        using var request = Request("PUT", Path, body, "*");
        using var response = await AssertResponse(request, 400);
        (
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)
        ).ShouldNotContain("john@example.test");
        string.Join("\n", fixture.Logs.Messages).ShouldNotContain("john@example.test");
        fixture.SavedTravelers.Calls.ShouldBe(0);
    }

    [Theory]
    [InlineData("givenName", "", "givenName", "Flights.PassengerGivenNameInvalid")]
    [InlineData("title", "bad", "title", "Flights.PassengerTitleInvalid")]
    [InlineData("phone", "secret", "phone", "Flights.PassengerPhoneInvalid")]
    public async Task Business_validation_has_fixed_field_errors_without_values(
        string field,
        string value,
        string expectedField,
        string code
    )
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(Body)!;
        node[field] = value;
        fixture.SavedTravelers.Reset();
        using var request = Request("PUT", Path, node.ToJsonString(), "*");
        using var response = await AssertResponse(request, 400);
        using var json = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)
        );
        json.RootElement.GetProperty("type").GetString().ShouldEndWith("Flights.TravelerInvalid");
        var error = json.RootElement.GetProperty("fieldErrors")[0];
        error.GetProperty("field").GetString().ShouldBe(expectedField);
        error.GetProperty("code").GetString().ShouldBe(code);
        error.EnumerateObject().Count().ShouldBe(2);
        fixture.SavedTravelers.Calls.ShouldBe(0);
    }

    [Theory]
    [InlineData("GET", "/api/flights/travelers?offset=-20", 400)]
    [InlineData("GET", "/api/flights/travelers?offset=1", 400)]
    [InlineData("GET", "/api/flights/travelers?offset=2147483641", 400)]
    [InlineData("GET", "/api/flights/travelers?offset=bad", 400)]
    [InlineData("GET", "/api/flights/travelers/not-a-guid", 404)]
    public async Task Invalid_pages_and_routes_are_no_store(string method, string path, int status)
    {
        using var request = Request(method, path);
        await AssertResponse(request, status);
    }

    [Fact]
    public async Task Protection_and_storage_errors_are_safe_service_unavailable()
    {
        foreach (
            var error in new[]
            {
                PiiProtectionErrors.Unavailable,
                PiiProtectionErrors.PayloadUnavailable,
                SavedTravelerErrors.StorageUnavailable,
            }
        )
        {
            fixture.SavedTravelers.Reset();
            fixture.SavedTravelers.Errors = [error];
            using var request = Request("GET", Path);
            await AssertResponse(request, 503);
        }
    }

    [Fact]
    public async Task Application_error_metadata_and_descriptions_are_never_echoed_and_fields_are_bounded()
    {
        fixture.SavedTravelers.Reset();
        string[] codes =
        [
            "Flights.PassengerTitleInvalid",
            "Flights.PassengerGivenNameInvalid",
            "Flights.PassengerFamilyNameInvalid",
            "Flights.PassengerDateOfBirthInvalid",
            "Flights.PassengerDateOfBirthFutureInvalid",
            "Flights.PassengerGenderInvalid",
            "Flights.PassengerEmailInvalid",
            "Flights.PassengerPhoneInvalid",
        ];
        fixture.SavedTravelers.Errors = Enumerable
            .Range(0, 20)
            .Select(index =>
                Error.Validation(
                    codes[index % codes.Length],
                    "secret-john@example.test",
                    new Dictionary<string, object> { ["field"] = "secret-john@example.test" }
                )
            )
            .ToList();
        using var request = Request("GET", Path);
        using var response = await AssertResponse(request, 400);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.ShouldNotContain("john@example.test");
        using var json = JsonDocument.Parse(body);
        json.RootElement.GetProperty("fieldErrors").GetArrayLength().ShouldBe(7);
    }

    [Theory]
    [InlineData("PUT", false)]
    [InlineData("PUT", true)]
    [InlineData("DELETE", false)]
    [InlineData("DELETE", true)]
    public async Task Known_and_chunked_oversize_bodies_are_rejected_before_service(
        string method,
        bool chunked
    )
    {
        fixture.SavedTravelers.Reset();
        fixture.IdempotencyStore.Reset();
        using var request = Request(method, Path, match: ETag);
        request.Content = chunked
            ? new UnknownLengthContent(new string('x', 16 * 1024 + 200))
            : new StringContent(
                new string('x', 16 * 1024 + 200),
                Encoding.UTF8,
                "application/json"
            );
        await AssertResponse(request, 413);
        fixture.SavedTravelers.Calls.ShouldBe(0);
        fixture.IdempotencyStore.TryBeginCount.ShouldBe(0);
    }

    [Fact]
    public async Task Exactly_16_KiB_body_is_accepted_and_duplicate_header_values_are_rejected()
    {
        fixture.SavedTravelers.Reset();
        using var exact = Request("PUT", Path, Body.PadRight(16 * 1024), "*");
        using var accepted = await AssertResponse(exact, 201);
        fixture.SavedTravelers.Calls.ShouldBe(1);
        fixture.SavedTravelers.Reset();
        using var duplicate = Request("PUT", Path, Body);
        duplicate.Headers.TryAddWithoutValidation("If-Match", new[] { ETag, ETag });
        using var rejected = await AssertResponse(duplicate, 400);
        fixture.SavedTravelers.Calls.ShouldBe(0);
    }

    [Theory]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("traveler@example.test")]
    public async Task Empty_or_non_GUID_subject_never_reaches_service(string subject)
    {
        fixture.SavedTravelers.Reset();
        fixture.Logs.Messages.Clear();
        using var request = Request("GET", Path, owner: subject);
        using var response = await AssertResponse(request, 401);
        (
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)
        ).ShouldNotContain(subject);
        string.Join("\n", fixture.Logs.Messages).ShouldNotContain(subject);
        fixture.SavedTravelers.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task Profiles_can_store_children_and_missing_fields_keep_typed_business_errors()
    {
        fixture.SavedTravelers.Reset();
        var child = System.Text.Json.Nodes.JsonNode.Parse(Body)!;
        child["dateOfBirth"] = "2020-01-01";
        using var request = Request("PUT", Path, child.ToJsonString(), "*");
        using var response = await AssertResponse(request, 201);
        fixture.SavedTravelers.SubmittedDetails!.Passenger.DateOfBirth.ShouldBe(
            new DateOnly(2020, 1, 1)
        );
        fixture.SavedTravelers.Reset();
        using var missing = Request("PUT", Path, "{}", "*");
        using var invalid = await AssertResponse(missing, 400);
        (
            await invalid.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)
        ).ShouldContain("Flights.PassengerGivenNameInvalid");
        fixture.SavedTravelers.Calls.ShouldBe(0);
    }

    private static HttpRequestMessage Request(
        string method,
        string path,
        string? body = null,
        string? none = null,
        string? match = null,
        string? owner = "22222222-2222-2222-2222-222222222222",
        bool scope = true
    )
    {
        var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (owner is not null)
            request.Headers.Add(TestAuthHandler.UserIdHeader, owner);
        if (scope)
            request.Headers.Add(TestAuthHandler.ScopesHeader, "flights:book");
        if (none is not null)
            request.Headers.TryAddWithoutValidation("If-None-Match", none);
        if (match is not null)
            request.Headers.TryAddWithoutValidation("If-Match", match);
        if (body is not null)
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        return request;
    }

    private async Task<HttpResponseMessage> AssertResponse(HttpRequestMessage request, int status)
    {
        var response = await fixture.Client.SendAsync(
            request,
            TestContext.Current.CancellationToken
        );
        ((int)response.StatusCode).ShouldBe(
            status,
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)
        );
        (response.Headers.CacheControl?.NoStore).ShouldBe(true);
        return response;
    }

    private sealed class UnknownLengthContent(string content) : HttpContent
    {
        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            stream.WriteAsync(Encoding.UTF8.GetBytes(content)).AsTask();
    }
}
