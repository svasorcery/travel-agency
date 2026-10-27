using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ErrorOr;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Travel.IntegrationContracts.AI.TripsBriefExtraction;
using Travel.Modules.Trips.Api.Composition;
using Travel.Modules.Trips.Api.Contracts;
using Travel.Modules.Trips.Api.Middleware;
using Travel.Modules.Trips.Application.BriefExtraction;
using Travel.Modules.Trips.Core;

namespace Travel.Modules.Trips.Tests.Integration;

public class TripsBriefExtractionHttpTests
{
    [Fact]
    public async Task Capability_and_extraction_ignore_plan_and_package_readiness()
    {
        var fake = new FakeClient();
        await using var fixture = await TripsHttpFixture.Start(
            ready: false,
            briefExtraction: true,
            configure: services => services.AddSingleton<ITripBriefExtractionClient>(fake)
        );
        var capability = await fixture.Client.GetAsync(
            "/api/trips/brief-extraction-capability",
            TestContext.Current.CancellationToken
        );
        capability.StatusCode.ShouldBe(HttpStatusCode.OK);
        capability.Headers.CacheControl!.NoStore.ShouldBeTrue();
        var metadata = await capability.Content.ReadFromJsonAsync<JsonElement>(
            TestContext.Current.CancellationToken
        );
        var policy = metadata.GetProperty("processingPolicyVersion").GetString()!;
        metadata.GetProperty("status").GetString().ShouldBe("configured");
        var result = await fixture.Client.PostAsJsonAsync(
            "/API/TRIPS/BRIEF-CANDIDATE/",
            Body(policy),
            TestContext.Current.CancellationToken
        );
        result.StatusCode.ShouldBe(HttpStatusCode.OK);
        result.Headers.CacheControl!.NoStore.ShouldBeTrue();
        fixture.Store.Loads.ShouldBe(0);
        fixture.Protection.Reads.ShouldBe(0);
        fake.Calls.ShouldBe(1);
        (
            await result.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)
        ).ShouldNotContain("fictional-private-marker");
    }

    [Theory]
    [InlineData("/api/trips/brief-extraction-capability")]
    [InlineData("/API/TRIPS/BRIEF-EXTRACTION-CAPABILITY/")]
    [InlineData("/api/trips/brief-candidate")]
    [InlineData("/API/TRIPS/BRIEF-CANDIDATE/")]
    public async Task Off_returns_404_before_auth(string route)
    {
        await using var fixture = await TripsHttpFixture.Start();
        fixture.Client.DefaultRequestHeaders.Remove("X-Trips-Owner");
        var request = new HttpRequestMessage(
            route.Contains("capability", StringComparison.OrdinalIgnoreCase)
                ? HttpMethod.Get
                : HttpMethod.Post,
            route
        );
        var response = await fixture.Client.SendAsync(
            request,
            TestContext.Current.CancellationToken
        );
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
        fixture.Authentication.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task Stale_policy_and_unknown_body_never_dispatch()
    {
        var fake = new FakeClient();
        await using var fixture = await TripsHttpFixture.Start(
            briefExtraction: true,
            configure: services => services.AddSingleton<ITripBriefExtractionClient>(fake)
        );
        (
            await fixture.Client.PostAsJsonAsync(
                "/api/trips/brief-candidate",
                Body("stale"),
                TestContext.Current.CancellationToken
            )
        ).StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        var body =
            "{\"requestId\":\"11111111-1111-4111-8111-111111111111\",\"text\":\"Москва\",\"locale\":\"ru\",\"referenceZone\":null,\"processingPolicyVersion\":\"policy\",\"ownerId\":\"private\"}";
        (
            await fixture.Client.PostAsync(
                "/api/trips/brief-candidate",
                new StringContent(body, Encoding.UTF8, "application/json"),
                TestContext.Current.CancellationToken
            )
        ).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        fake.Calls.ShouldBe(0);
    }

    [Theory]
    [InlineData("?text=private")]
    [InlineData("?cursor=private")]
    public async Task Capability_rejects_query_without_AI_resolution(string query)
    {
        await using var fixture = await TripsHttpFixture.Start(
            briefExtraction: true,
            configure: services =>
                services.AddSingleton<ITripBriefExtractionClient>(_ =>
                    throw new InvalidOperationException("never resolve")
                )
        );
        (
            await fixture.Client.GetAsync(
                "/api/trips/brief-extraction-capability" + query,
                TestContext.Current.CancellationToken
            )
        ).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Scalar_limit_and_envelope_limits_are_413()
    {
        var fake = new FakeClient();
        await using var fixture = await TripsHttpFixture.Start(
            briefExtraction: true,
            configure: services => services.AddSingleton<ITripBriefExtractionClient>(fake)
        );
        var policy = TripsBriefPolicyIdentity.Compute(
            "anthropic",
            "fictional-model",
            TripsBriefMessageIdentity.PrivacyNoticeVersion
        );
        (
            await fixture.Client.PostAsJsonAsync(
                "/api/trips/brief-candidate",
                Body(policy, new string('я', 1001)),
                TestContext.Current.CancellationToken
            )
        ).StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
        var raw = new StringContent(new string(' ', 8193), Encoding.UTF8, "application/json");
        (
            await fixture.Client.PostAsync(
                "/api/trips/brief-candidate",
                raw,
                TestContext.Current.CancellationToken
            )
        ).StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
        fake.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task Capability_never_resolves_client_and_is_bounded_public_metadata()
    {
        await using var fixture = await TripsHttpFixture.Start(
            ready: false,
            briefExtraction: true,
            configure: services =>
                services.AddSingleton<ITripBriefExtractionClient>(_ =>
                    throw new InvalidOperationException("never resolve")
                )
        );
        var response = await fixture.Client.GetAsync(
            "/api/trips/brief-extraction-capability",
            TestContext.Current.CancellationToken
        );
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var bytes = await response.Content.ReadAsByteArrayAsync(
            TestContext.Current.CancellationToken
        );
        bytes.Length.ShouldBeLessThanOrEqualTo(2048);
        using var json = JsonDocument.Parse(bytes);
        json.RootElement.EnumerateObject()
            .Select(x => x.Name)
            .Order()
            .ShouldBe(
                new[]
                {
                    "candidateVersion",
                    "status",
                    "processorId",
                    "modelId",
                    "processingPolicyVersion",
                }.Order()
            );
    }

    [Fact]
    public async Task Unconfigured_returns_null_metadata_and_preserves_manual_fallback()
    {
        await using var fixture = await TripsHttpFixture.Start(
            briefExtraction: true,
            configure: services => services.AddSingleton(new TripsBriefExtractionFeature(true))
        );
        var response = await fixture.Client.GetFromJsonAsync<JsonElement>(
            "/api/trips/brief-extraction-capability",
            TestContext.Current.CancellationToken
        );
        response.GetProperty("status").GetString().ShouldBe("unconfigured");
        response.GetProperty("processorId").ValueKind.ShouldBe(JsonValueKind.Null);
        response.GetProperty("modelId").ValueKind.ShouldBe(JsonValueKind.Null);
        response.GetProperty("processingPolicyVersion").ValueKind.ShouldBe(JsonValueKind.Null);
        (
            await fixture.Client.PostAsJsonAsync(
                "/api/trips/brief-candidate",
                Body("policy"),
                TestContext.Current.CancellationToken
            )
        ).StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task Public_policy_digest_binds_all_three_public_members()
    {
        var baseline = TripsBriefPolicyIdentity.Compute(
            "anthropic",
            "fictional-model",
            "trips-brief-ru-v1"
        );
        baseline.ShouldNotBe(
            TripsBriefPolicyIdentity.Compute("other", "fictional-model", "trips-brief-ru-v1")
        );
        baseline.ShouldNotBe(
            TripsBriefPolicyIdentity.Compute("anthropic", "other", "trips-brief-ru-v1")
        );
        baseline.ShouldNotBe(
            TripsBriefPolicyIdentity.Compute("anthropic", "fictional-model", "other")
        );
        await using var fixture = await TripsHttpFixture.Start(
            briefExtraction: true,
            configure: services =>
                services.AddSingleton(
                    new TripsBriefExtractionFeature(true, "unknown", "fictional-model", baseline)
                )
        );
        var response = await fixture.Client.GetFromJsonAsync<JsonElement>(
            "/api/trips/brief-extraction-capability",
            TestContext.Current.CancellationToken
        );
        response.GetProperty("status").GetString().ShouldBe("unconfigured");
    }

    [Fact]
    public async Task Seventh_request_per_owner_is_busy_without_dispatch()
    {
        var fake = new FakeClient();
        await using var fixture = await TripsHttpFixture.Start(
            briefExtraction: true,
            configure: services => services.AddSingleton<ITripBriefExtractionClient>(fake)
        );
        var policy = TripsBriefPolicyIdentity.Compute(
            "anthropic",
            "fictional-model",
            TripsBriefMessageIdentity.PrivacyNoticeVersion
        );
        for (var i = 0; i < 6; i++)
            (
                await fixture.Client.PostAsJsonAsync(
                    "/api/trips/brief-candidate",
                    Body(policy),
                    TestContext.Current.CancellationToken
                )
            ).StatusCode.ShouldBe(HttpStatusCode.OK);
        var seventh = await fixture.Client.PostAsJsonAsync(
            "/api/trips/brief-candidate",
            Body(policy),
            TestContext.Current.CancellationToken
        );
        seventh.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        seventh.Headers.CacheControl!.NoStore.ShouldBeTrue();
        fake.Calls.ShouldBe(6);
    }

    [Theory]
    [InlineData("Busy", 429)]
    [InlineData("InvalidOutput", 502)]
    [InlineData("Disabled", 503)]
    [InlineData("Refused", 503)]
    [InlineData("DeadlineExceeded", 504)]
    public async Task Controlled_transport_outcomes_remain_distinct_no_store(
        string status,
        int expected
    )
    {
        var fake = new FakeClient { Status = status };
        await using var fixture = await TripsHttpFixture.Start(
            briefExtraction: true,
            configure: services => services.AddSingleton<ITripBriefExtractionClient>(fake)
        );
        var policy = TripsBriefPolicyIdentity.Compute(
            "anthropic",
            "fictional-model",
            TripsBriefMessageIdentity.PrivacyNoticeVersion
        );
        var response = await fixture.Client.PostAsJsonAsync(
            "/api/trips/brief-candidate",
            Body(policy),
            TestContext.Current.CancellationToken
        );
        ((int)response.StatusCode).ShouldBe(expected);
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
        fake.Calls.ShouldBe(1);
        var content = await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken
        );
        content.ShouldNotContain("fictional-private-marker");
    }

    [Fact]
    public async Task Query_ifmatch_get_body_duplicate_fields_and_controls_are_rejected()
    {
        var fake = new FakeClient();
        await using var fixture = await TripsHttpFixture.Start(
            briefExtraction: true,
            configure: services => services.AddSingleton<ITripBriefExtractionClient>(fake)
        );
        var policy = TripsBriefPolicyIdentity.Compute(
            "anthropic",
            "fictional-model",
            TripsBriefMessageIdentity.PrivacyNoticeVersion
        );
        var post = new HttpRequestMessage(HttpMethod.Post, "/api/trips/brief-candidate")
        {
            Content = JsonContent.Create(Body(policy)),
        };
        post.Headers.TryAddWithoutValidation("If-Match", "\"1\"");
        (
            await fixture.Client.SendAsync(post, TestContext.Current.CancellationToken)
        ).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (
            await fixture.Client.PostAsJsonAsync(
                "/api/trips/brief-candidate?private=marker",
                Body(policy),
                TestContext.Current.CancellationToken
            )
        ).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var get = new HttpRequestMessage(HttpMethod.Get, "/api/trips/brief-extraction-capability")
        {
            Content = new StringContent("{}"),
        };
        (
            await fixture.Client.SendAsync(get, TestContext.Current.CancellationToken)
        ).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var json = JsonSerializer
            .Serialize(Body(policy))
            .Replace("\"locale\":\"ru\"", "\"locale\":\"ru\",\"locale\":\"ru\"");
        (
            await fixture.Client.PostAsync(
                "/api/trips/brief-candidate",
                new StringContent(json, Encoding.UTF8, "application/json"),
                TestContext.Current.CancellationToken
            )
        ).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (
            await fixture.Client.PostAsJsonAsync(
                "/api/trips/brief-candidate",
                Body(policy, "Москва\t"),
                TestContext.Current.CancellationToken
            )
        ).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        fake.Calls.ShouldBe(0);
        fixture.Logs.Values.ShouldNotContain(entry =>
            entry.Contains("fictional-private-marker", StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task Chunked_body_is_bounded_and_cannot_reset_admission_deadline()
    {
        var fake = new FakeClient();
        await using var fixture = await TripsHttpFixture.Start(
            briefExtraction: true,
            configure: services => services.AddSingleton<ITripBriefExtractionClient>(fake)
        );
        var response = await fixture.Client.PostAsync(
            "/api/trips/brief-candidate",
            new ChunkedContent(new byte[8193])
            {
                Headers =
                {
                    ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(
                        "application/json"
                    ),
                },
            },
            TestContext.Current.CancellationToken
        );
        response.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
        fake.Calls.ShouldBe(0);
        var services = new ServiceCollection()
            .AddSingleton(TimeProvider.System)
            .AddSingleton(new TripsBriefExtractionFeature(true))
            .AddLogging()
            .BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Method = "POST";
        context.Request.ContentType = "application/json";
        context.Request.Body = new SlowBody();
        context.Response.Body = new MemoryStream();
        var started = System.Diagnostics.Stopwatch.StartNew();
        await TripsBriefExtractionGuard.HandleAsync(
            context,
            TripsBriefExtractionRoute.Candidate,
            OwnerIdentity.Create("https://fictional.test", Guid.NewGuid()).Value,
            _ => throw new InvalidOperationException("body must expire before endpoint")
        );
        context.Response.StatusCode.ShouldBe(504);
        started.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(6));
    }

    private sealed class ChunkedContent(byte[] bytes) : HttpContent
    {
        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            stream.WriteAsync(bytes).AsTask();

        public ChunkedContent()
            : this([]) { }
    }

    private sealed class SlowBody : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken ct = default
        )
        {
            await Task.Delay(Timeout.Infinite, ct);
            return 0;
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override void Flush() => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }

    private static object Body(string policy, string text = "Москва fictional-private-marker") =>
        new
        {
            requestId = Guid.NewGuid(),
            text,
            locale = "ru",
            referenceZone = "Europe/Moscow",
            processingPolicyVersion = policy,
        };

    private sealed class FakeClient : ITripBriefExtractionClient
    {
        public int Calls;
        public string Status = "Ready";

        public Task<ErrorOr<TripsBriefParsed>> ParseAsync(
            TripBriefExtractionInput input,
            CancellationToken ct
        )
        {
            Calls++;
            var candidate = TripBriefCandidateValidator.Analyze(input);
            return Task.FromResult<ErrorOr<TripsBriefParsed>>(
                new TripsBriefParsed(
                    input.RequestId,
                    input.ProcessingPolicyVersion,
                    Status,
                    candidate.Fields,
                    candidate.Issues
                )
            );
        }
    }
}
