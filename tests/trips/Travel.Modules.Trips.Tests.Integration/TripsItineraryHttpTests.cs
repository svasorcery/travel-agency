using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ErrorOr;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shouldly;
using Travel.Modules.Trips.Api.Contracts;
using Travel.Modules.Trips.Application;
using Travel.Modules.Trips.Application.Itineraries;
using Travel.Modules.Trips.Core;
using Travel.Modules.Trips.Core.Itineraries;
using Travel.Modules.Trips.Infrastructure.Serialization;
using Xunit;

namespace Travel.Modules.Trips.Tests.Integration;

public sealed class TripsItineraryHttpTests
{
    [Theory]
    [InlineData("GET", "/api/trips/itinerary-availability")]
    [InlineData("POST", "/api/trips/aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa/itinerary-candidate")]
    public async Task Disabled_itinerary_is_hidden_before_authentication(string method, string path)
    {
        await using var fixture = await TripsHttpFixture.Start();
        fixture.Client.DefaultRequestHeaders.Remove("X-Trips-Owner");
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        using var response = await fixture.Client.SendAsync(
            request,
            TestContext.Current.CancellationToken
        );
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
        fixture.Protection.Reads.ShouldBe(0);
    }

    [Fact]
    public async Task Fictional_fixture_returns_its_validated_facts()
    {
        var source = new FictionalItinerarySource();
        source.Facts.ShouldNotBeNull();
        var result = await source.ReadAsync(Ct);
        result.IsError.ShouldBeFalse();
        result.Value.ShouldBeSameAs(source.Facts);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private const string Availability = "/api/trips/itinerary-availability";
    internal const string CreateJson =
        """{"title":"PRIVATE_TITLE","brief":{"destinationId":"moscow","originId":"saint-petersburg","startDate":"2026-10-22","endDate":"2026-10-22","adults":2,"currency":"RUB","totalBudget":"15000.00","interests":["architecture"],"pace":"relaxed"}}""";

    [Theory]
    [InlineData("/api/trips/itinerary-availability")]
    [InlineData("/API/TRIPS/ITINERARY-AVAILABILITY/")]
    public async Task Ready_bypasses_all_M1_dependencies_and_has_exact_ten_fields(string path)
    {
        var resolves = 0;
        await using var fixture = await TripsHttpFixture.Start(
            itinerary: true,
            configure: services =>
            {
                services.RemoveAll<ITripsReadiness>();
                services.RemoveAll<ITripPlanStore>();
                services.RemoveAll<ITripPlanProtector>();
                services.RemoveAll<ITripPlanCodec>();
                services.AddSingleton<ITripsReadiness>(_ =>
                {
                    resolves++;
                    throw new InvalidOperationException();
                });
                services.AddSingleton<ITripPlanStore>(_ =>
                {
                    resolves++;
                    throw new InvalidOperationException();
                });
                services.AddSingleton<ITripPlanProtector>(_ =>
                {
                    resolves++;
                    throw new InvalidOperationException();
                });
                services.AddSingleton<ITripPlanCodec>(_ =>
                {
                    resolves++;
                    throw new InvalidOperationException();
                });
            }
        );
        using var response = await fixture.Client.GetAsync(path, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var bytes = await response.Content.ReadAsByteArrayAsync(Ct);
        bytes.Length.ShouldBeLessThanOrEqualTo(2048);
        using var json = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 4 });
        json.RootElement.EnumerateObject()
            .Select(p => p.Name)
            .ShouldBe(
                new[]
                {
                    "candidateVersion",
                    "status",
                    "destinationId",
                    "datasetId",
                    "datasetVersion",
                    "datasetHash",
                    "policyVersion",
                    "dataMode",
                    "humanQuality",
                    "releaseValidUntil",
                }
            );
        resolves.ShouldBe(0);
        fixture.Store.Loads.ShouldBe(0);
        fixture.Protection.Reads.ShouldBe(0);
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
    }

    [Theory]
    [InlineData(null, 401)]
    [InlineData("bad", 403)]
    public async Task Availability_requires_trusted_owner_without_source_resolution(
        string? owner,
        int status
    )
    {
        var resolves = 0;
        await using var fixture = await TripsHttpFixture.Start(
            itinerary: true,
            configure: s =>
            {
                s.RemoveAll<IItineraryFactSource>();
                s.AddSingleton<IItineraryFactSource>(_ =>
                {
                    resolves++;
                    throw new InvalidOperationException();
                });
            }
        );
        fixture.Client.DefaultRequestHeaders.Remove("X-Trips-Owner");
        if (owner != null)
            fixture.Client.DefaultRequestHeaders.Add("X-Trips-Owner", owner);
        using var response = await fixture.Client.GetAsync(Availability, Ct);
        ((int)response.StatusCode).ShouldBe(status);
        resolves.ShouldBe(0);
        await Problem(response, status == 401 ? "Trips.Unauthenticated" : "Trips.Forbidden");
    }

    [Theory]
    [InlineData("?owner=PRIVATE_QUERY", null)]
    [InlineData("?cursor=x", null)]
    [InlineData("", "{}")]
    public async Task Availability_rejects_query_or_body_before_source(string query, string? body)
    {
        var source = new FictionalItinerarySource();
        await using var fixture = await TripsHttpFixture.Start(
            itinerary: true,
            configure: s => s.AddSingleton<IItineraryFactSource>(source)
        );
        using var request = new HttpRequestMessage(HttpMethod.Get, Availability + query);
        if (body != null)
            request.Content = new UnknownLengthContent(Encoding.UTF8.GetBytes(body));
        using var response = await fixture.Client.SendAsync(request, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        await Problem(response, "Trips.InvalidInput");
        source.Calls.ShouldBe(0);
    }

    [Theory]
    [InlineData("/api/trips/itinerary-availability/extra")]
    [InlineData("/api/trips/itinerary-availability//")]
    public async Task Unknown_paths_never_bypass_M1_readiness(string path)
    {
        await using var fixture = await TripsHttpFixture.Start(itinerary: true, ready: false);
        using var response = await fixture.Client.GetAsync(path, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        await Problem(response, "Trips.Unavailable");
    }

    [Theory]
    [InlineData("GET", "/api/trips/aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa/itinerary-candidate")]
    [InlineData("POST", "/api/trips/itinerary-availability")]
    [InlineData("POST", "/api/trips/bad/itinerary-candidate")]
    public async Task Wrong_method_or_invalid_guid_is_not_classified_as_M2(
        string method,
        string path
    )
    {
        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        Travel
            .Modules.Trips.Api.Middleware.TripsItineraryRoutes.Classify(context.Request)
            .ShouldBe(Travel.Modules.Trips.Api.Middleware.TripsItineraryRoute.None);
        await using var fixture = await TripsHttpFixture.Start(ready: false);
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method == "POST")
            request.Content = TripsHttpTests.Body(TripsHttpTests.CreateJson);
        using var response = await fixture.Client.SendAsync(request, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
    }

    [Theory]
    [InlineData("", 400)]
    [InlineData("[]", 400)]
    [InlineData("null", 400)]
    [InlineData("{\"x\":1}", 400)]
    [InlineData("{\"x\":1,\"x\":2}", 400)]
    [InlineData("{\"owner\":\"PRIVATE_BODY\"}", 400)]
    [InlineData("{", 400)]
    [InlineData("{\"nested\":[[[[[[[[[[[[[[[[[]]]]]]]]]]]]]]]]]}", 400)]
    public async Task Candidate_rejects_invalid_body_without_source_or_private_read(
        string body,
        int status
    )
    {
        var source = new FictionalItinerarySource();
        await using var fixture = await TripsHttpFixture.Start(
            itinerary: true,
            configure: s => s.AddSingleton<IItineraryFactSource>(source)
        );
        using var response = await Candidate(
            fixture,
            "/api/trips/aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa",
            body: body
        );
        ((int)response.StatusCode).ShouldBe(status);
        await Problem(response, "Trips.InvalidInput");
        source.Calls.ShouldBe(0);
        fixture.Store.Loads.ShouldBe(0);
        fixture.Protection.Reads.ShouldBe(0);
    }

    [Theory]
    [InlineData(null, 428, "Trips.PreconditionRequired")]
    [InlineData("1", 400, "Trips.InvalidInput")]
    [InlineData("W/\"1\"", 400, "Trips.InvalidInput")]
    [InlineData("\"01\"", 400, "Trips.InvalidInput")]
    [InlineData("\"0\"", 400, "Trips.InvalidInput")]
    [InlineData("\"9223372036854775808\"", 400, "Trips.InvalidInput")]
    [InlineData("\"2\"", 412, "Trips.RevisionMismatch")]
    public async Task Candidate_reuses_strong_revision_grammar(
        string? revision,
        int status,
        string code
    )
    {
        var source = new FictionalItinerarySource();
        await using var fixture = await TripsHttpFixture.Start(
            itinerary: true,
            configure: s => s.AddSingleton<IItineraryFactSource>(source)
        );
        var path = await Create(fixture);
        using var response = await Candidate(fixture, path, revision);
        ((int)response.StatusCode).ShouldBe(status);
        await Problem(response, code);
        source.Calls.ShouldBe(0);
    }

    [Theory]
    [InlineData("application/json; charset=utf-16")]
    [InlineData("text/plain")]
    [InlineData("application/json; charset=latin1")]
    public async Task Candidate_rejects_wrong_media_type(string contentType)
    {
        await using var fixture = await TripsHttpFixture.Start(itinerary: true);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/trips/aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa/itinerary-candidate"
        );
        request.Headers.TryAddWithoutValidation("If-Match", "\"1\"");
        request.Content = new ByteArrayContent("{}"u8.ToArray());
        request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        using var response = await fixture.Client.SendAsync(request, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        await Problem(response, "Trips.InvalidInput");
    }

    [Theory]
    [InlineData(false, 1024, 200)]
    [InlineData(false, 1025, 400)]
    [InlineData(true, 1024, 200)]
    [InlineData(true, 1025, 400)]
    public async Task Actual_stream_size_is_bounded_with_or_without_content_length(
        bool chunked,
        int size,
        int status
    )
    {
        await using var fixture = await TripsHttpFixture.Start(itinerary: true);
        var path = await Create(fixture);
        var bytes = Encoding.UTF8.GetBytes("{}" + new string(' ', size - 2));
        using var request = new HttpRequestMessage(HttpMethod.Post, path + "/itinerary-candidate");
        request.Headers.TryAddWithoutValidation("If-Match", "\"1\"");
        request.Content = chunked ? new UnknownLengthContent(bytes) : new ByteArrayContent(bytes);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        using var response = await fixture.Client.SendAsync(request, Ct);
        ((int)response.StatusCode).ShouldBe(status);
    }

    [Fact]
    public async Task Invalid_utf8_is_rejected_as_whole_body()
    {
        await using var fixture = await TripsHttpFixture.Start(itinerary: true);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/trips/aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa/itinerary-candidate"
        );
        request.Headers.TryAddWithoutValidation("If-Match", "\"1\"");
        request.Content = new UnknownLengthContent([123, 125, 0xff]);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        using var response = await fixture.Client.SendAsync(request, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Candidate_preserves_M1_readiness_while_availability_is_ready()
    {
        await using var fixture = await TripsHttpFixture.Start(itinerary: true, ready: false);
        using var available = await fixture.Client.GetAsync(Availability, Ct);
        available.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var response = await Candidate(
            fixture,
            "/api/trips/aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"
        );
        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        await Problem(response, "Trips.Unavailable");
    }

    [Fact]
    public async Task Foreign_plan_returns_not_found_before_decrypt_or_source()
    {
        var source = new FictionalItinerarySource();
        await using var fixture = await TripsHttpFixture.Start(
            itinerary: true,
            configure: s => s.AddSingleton<IItineraryFactSource>(source)
        );
        var path = await Create(fixture);
        fixture.Client.DefaultRequestHeaders.Remove("X-Trips-Owner");
        fixture.Client.DefaultRequestHeaders.Add("X-Trips-Owner", Guid.NewGuid().ToString());
        using var response = await Candidate(fixture, path);
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        source.Calls.ShouldBe(0);
        fixture.Protection.Reads.ShouldBe(0);
    }

    [Fact]
    public async Task Nonempty_plan_conflicts_and_does_not_read_source()
    {
        var source = new FictionalItinerarySource();
        await using var fixture = await TripsHttpFixture.Start(
            itinerary: true,
            configure: s => s.AddSingleton<IItineraryFactSource>(source)
        );
        var path = await Create(fixture);
        var body = JsonNode.Parse(CreateJson)!.AsObject();
        body["days"] = JsonNode.Parse(
            """[{"date":"2026-10-22","blocks":[{"blockId":"bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb","title":"PRIVATE_BLOCK","note":"PRIVATE_NOTE"}]}]"""
        );
        using var put = new HttpRequestMessage(HttpMethod.Put, path)
        {
            Content = TripsHttpTests.Body(body.ToJsonString()),
        };
        put.Headers.TryAddWithoutValidation("If-Match", "\"1\"");
        using var saved = await fixture.Client.SendAsync(put, Ct);
        saved.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var response = await Candidate(fixture, path, "\"2\"");
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        await Problem(response, "Trips.ItineraryRequiresEmptyPlan");
        source.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task Bad_optional_source_does_not_break_manual_M1_and_never_returns_stale_identity()
    {
        var source = new FictionalItinerarySource { Unavailable = true };
        await using var fixture = await TripsHttpFixture.Start(
            itinerary: true,
            configure: s => s.AddSingleton<IItineraryFactSource>(source)
        );
        var path = await Create(fixture);
        using var read = await fixture.Client.GetAsync(path, Ct);
        read.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var available = await fixture.Client.GetAsync(Availability, Ct);
        available.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        await Problem(available, "Trips.ItineraryUnavailable");
        (await available.Content.ReadAsStringAsync(Ct)).ShouldNotContain("datasetHash");
        using var generated = await Candidate(fixture, path);
        generated.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        await Problem(generated, "Trips.ItineraryUnavailable");
    }

    [Fact]
    public async Task Availability_rechecks_whole_current_package_withdrawal_and_last_clock_check()
    {
        var source = new FictionalItinerarySource();
        var clock = new ItineraryHttpClock();
        await using var fixture = await TripsHttpFixture.Start(
            itinerary: true,
            configure: s =>
            {
                s.AddSingleton<IItineraryFactSource>(source);
                s.AddSingleton<TimeProvider>(clock);
            }
        );
        var path = await Create(fixture);
        using var first = await fixture.Client.GetAsync(Availability, Ct);
        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        source.Facts = (
            FictionalItineraryFacts.Input() with
            {
                Version = "fictional-v3",
                DatasetHash = new string('c', 64),
            }
        )
            .Build()
            .Value;
        using var second = await fixture.Client.GetAsync(Availability, Ct);
        (await second.Content.ReadAsStringAsync(Ct)).ShouldContain("fictional-v3");
        source.Unavailable = true;
        using var withdrawn = await fixture.Client.GetAsync(Availability, Ct);
        withdrawn.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        source.Unavailable = false;
        var ticks = 0;
        clock.Read = () =>
            ++ticks == 1 ? FictionalItineraryFacts.Now : source.Facts.ReleaseValidUntil;
        using var expired = await fixture.Client.GetAsync(Availability, Ct);
        expired.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        clock.Read = null;
        clock.Now = source.Facts.ReleaseValidUntil;
        using var alreadyExpired = await fixture.Client.GetAsync(Availability, Ct);
        alreadyExpired.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        clock.Now = FictionalItineraryFacts.Now;
        using var unchanged = await fixture.Client.GetAsync(path, Ct);
        unchanged.Headers.ETag!.Tag.ShouldBe("\"1\"");
        source.Calls.ShouldBe(5);
    }

    [Fact]
    public async Task Busy_is_429_with_retry_after_one_from_shared_application_gate()
    {
        var encoder = new HeldEncoder();
        await using var fixture = await TripsHttpFixture.Start(
            itinerary: true,
            configure: s => s.AddSingleton<IItineraryCandidateResponseEncoder>(encoder)
        );
        var path = await Create(fixture);
        var first = Candidate(fixture, path);
        var second = Candidate(fixture, path);
        await encoder.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        using var busy = await Candidate(fixture, path);
        busy.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        await Problem(busy, "Trips.ItineraryBusy");
        busy.Headers.RetryAfter!.Delta.ShouldBe(TimeSpan.FromSeconds(1));
        encoder.Release.TrySetResult();
        using var a = await first;
        using var b = await second;
        a.StatusCode.ShouldBe(HttpStatusCode.OK);
        b.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Actual_application_deadline_maps_504_without_retry_header()
    {
        await using var fixture = await TripsHttpFixture.Start(
            itinerary: true,
            configure: s =>
                s.AddSingleton<IItineraryCandidateResponseEncoder>(new DeadlineEncoder())
        );
        var path = await Create(fixture);
        using var response = await Candidate(fixture, path);
        response.StatusCode.ShouldBe(HttpStatusCode.GatewayTimeout);
        await Problem(response, "Trips.ItineraryDeadline");
        response.Headers.RetryAfter.ShouldBeNull();
    }

    [Theory]
    [InlineData("candidate")]
    [InlineData("partial")]
    [InlineData("empty")]
    [InlineData("unsupported")]
    [InlineData("ready")]
    [InlineData("error")]
    public async Task Actual_production_http_bytes_match_fictional_wire_vectors(string vector)
    {
        var source = new FictionalItinerarySource();
        if (vector == "empty")
            source.CloseAll();
        await using var fixture = await TripsHttpFixture.Start(
            itinerary: true,
            configure: s => s.AddSingleton<IItineraryFactSource>(source)
        );
        var create =
            vector == "unsupported" ? CreateJson.Replace("moscow", "saint-petersburg")
            : vector == "partial"
                ? CreateJson.Replace("[\"architecture\"]", "[\"architecture\",\"food\"]")
            : CreateJson;
        var path = await Create(fixture, create);
        using var response =
            vector == "ready" ? await fixture.Client.GetAsync(Availability, Ct)
            : vector == "error" ? await Candidate(fixture, path, null)
            : await Candidate(fixture, path);
        ((int)response.StatusCode).ShouldBe(vector == "error" ? 428 : 200);
        var raw = await response.Content.ReadAsStringAsync(Ct);
        var actual = JsonNode.Parse(raw)!;
        var root = FindRoot();
        var file = Path.Combine(root, "tests", "fixtures", "trips-itinerary-v1", vector + ".json");
        var expected = JsonNode.Parse(File.ReadAllText(file))!;
        // Only generator UUIDs are nondeterministic. Validate them before substituting fixture UUIDs for exact whole-object comparison.
        if (actual["days"] is JsonArray days)
            for (var d = 0; d < days.Count; d++)
            for (var b = 0; b < days[d]!["blocks"]!.AsArray().Count; b++)
            {
                var currentId = actual["days"]![d]!["blocks"]![b]![
                    "proposedBlockId"
                ]!.GetValue<string>();
                Guid.Parse(currentId).ShouldNotBe(Guid.Empty);
                var expectedId = expected["days"]![d]!["blocks"]![b]![
                    "proposedBlockId"
                ]!.GetValue<string>();
                raw = raw.Replace(currentId, expectedId, StringComparison.Ordinal);
                actual["days"]![d]!["blocks"]![b]!["proposedBlockId"] = expectedId;
            }
        if (actual["traceId"] is { } trace)
        {
            var currentTrace = trace.GetValue<string>();
            System
                .Text.RegularExpressions.Regex.IsMatch(
                    currentTrace,
                    "^00-[0-9a-f]{32}-[0-9a-f]{16}-[0-9a-f]{2}$"
                )
                .ShouldBeTrue();
            raw = raw.Replace(
                currentTrace,
                expected["traceId"]!.GetValue<string>(),
                StringComparison.Ordinal
            );
            actual["traceId"] = expected["traceId"]!.DeepClone();
        }
        JsonNode.DeepEquals(actual, expected).ShouldBeTrue();
        Encoding.UTF8.GetBytes(raw).ShouldBe(File.ReadAllBytes(file));
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
        var logs = string.Join(";", fixture.Logs.Values);
        logs.ShouldNotContain("PRIVATE_TITLE");
        logs.ShouldNotContain("PRIVATE_NOTE");
        logs.ShouldNotContain("PRIVATE_QUERY");
        fixture.Logs.Overflow.ShouldBeFalse();
        if (vector is "candidate" or "partial" or "empty" or "unsupported")
        {
            using var read = await fixture.Client.GetAsync(path, Ct);
            read.Headers.ETag!.Tag.ShouldBe("\"1\"");
            var plan = JsonNode.Parse(await read.Content.ReadAsStringAsync(Ct))!;
            plan["days"]![0]!["blocks"]!.AsArray().ShouldBeEmpty();
        }
    }

    [Theory]
    [InlineData(false, "Development")]
    [InlineData(true, "Production")]
    public async Task Default_Wolverine_map_preserves_M1_with_all_M2_dependencies_absent(
        bool requested,
        string environment
    )
    {
        await using var fixture = await TripsHttpFixture.Start(
            itinerary: requested,
            environment: environment,
            defaultMap: true
        );
        fixture.App.Services.GetService<IItineraryFactSource>().ShouldBeNull();
        using var manual = await fixture.Client.GetAsync("/api/trips/destinations", Ct);
        manual.StatusCode.ShouldBe(HttpStatusCode.OK);
        fixture.Client.DefaultRequestHeaders.Remove("X-Trips-Owner");
        using var available = await fixture.Client.GetAsync(Availability, Ct);
        available.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        using var candidate = await Candidate(
            fixture,
            "/API/TRIPS/00000000-0000-0000-0000-000000000000"
        );
        candidate.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("/API/TRIPS/", "/ITINERARY-CANDIDATE/")]
    [InlineData("/api/trips/", "/itinerary-candidate")]
    public async Task Candidate_router_and_classifier_agree_for_case_and_single_trailing_slash(
        string prefix,
        string suffix
    )
    {
        await using var fixture = await TripsHttpFixture.Start(itinerary: true);
        var path = await Create(fixture);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            prefix + path.Split('/').Last() + suffix
        )
        {
            Content = TripsHttpTests.Body("{}"),
        };
        request.Headers.TryAddWithoutValidation("If-Match", "\"1\"");
        using var response = await fixture.Client.SendAsync(request, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Large_Int64_revision_is_retained_as_lossless_string()
    {
        await using var fixture = await TripsHttpFixture.Start(itinerary: true);
        var path = await Create(fixture);
        var id = Guid.Parse(path.Split('/').Last());
        var row = fixture.Store.Rows[id];
        var payload = row.ProtectedPayload.Replace(
            "\"revision\":\"1\"",
            "\"revision\":\"9007199254740993\"",
            StringComparison.Ordinal
        );
        payload.ShouldNotBe(row.ProtectedPayload);
        fixture.Store.Rows[id] = row with
        {
            Revision = 9007199254740993,
            ProtectedPayload = payload,
        };
        using var response = await Candidate(fixture, path, "\"9007199254740993\"");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var candidate = JsonNode.Parse(await response.Content.ReadAsStringAsync(Ct))!;
        candidate["baseRevision"]!.GetValue<string>().ShouldBe("9007199254740993");
    }

    [Fact]
    public async Task Candidate_rejects_query_before_any_source_or_private_read_and_logs_remain_private()
    {
        await using var fixture = await TripsHttpFixture.Start(itinerary: true);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/trips/aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa/itinerary-candidate?owner=PRIVATE_QUERY"
        )
        {
            Content = TripsHttpTests.Body("{}"),
        };
        request.Headers.TryAddWithoutValidation("If-Match", "\"1\"");
        using var response = await fixture.Client.SendAsync(request, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        fixture.Store.Loads.ShouldBe(0);
        fixture.Protection.Reads.ShouldBe(0);
        string.Join(";", fixture.Logs.Values).ShouldNotContain("PRIVATE_QUERY");
    }

    [Theory]
    [InlineData("GET", "/API/TRIPS/ITINERARY-AVAILABILITY/")]
    [InlineData("POST", "/API/TRIPS/00000000-0000-0000-0000-000000000000/ITINERARY-CANDIDATE/")]
    public async Task Disabled_M2_does_not_authenticate_or_resolve_any_dependency(
        string method,
        string path
    )
    {
        var resolves = 0;
        await using var fixture = await TripsHttpFixture.Start(configure: s =>
        {
            s.RemoveAll<ITripsReadiness>();
            s.RemoveAll<ITripPlanStore>();
            s.RemoveAll<ITripPlanCodec>();
            s.RemoveAll<ITripPlanProtector>();
            s.AddSingleton<ITripsReadiness>(_ =>
            {
                resolves++;
                throw new InvalidOperationException();
            });
            s.AddSingleton<ITripPlanStore>(_ =>
            {
                resolves++;
                throw new InvalidOperationException();
            });
            s.AddSingleton<ITripPlanCodec>(_ =>
            {
                resolves++;
                throw new InvalidOperationException();
            });
            s.AddSingleton<ITripPlanProtector>(_ =>
            {
                resolves++;
                throw new InvalidOperationException();
            });
            s.AddSingleton<IItineraryFactSource>(_ =>
            {
                resolves++;
                throw new InvalidOperationException();
            });
        });
        fixture.Client.DefaultRequestHeaders.Remove("X-Trips-Owner");
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        using var response = await fixture.Client.SendAsync(request, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        await Problem(response, "Trips.NotFound");
        resolves.ShouldBe(0);
        fixture.Authentication.Calls.ShouldBe(0);
        fixture.Store.Loads.ShouldBe(0);
        fixture.Protection.Reads.ShouldBe(0);
    }

    [Theory]
    [InlineData(null, 401)]
    [InlineData("bad", 403)]
    public async Task Candidate_authentication_precedes_body_and_all_source_work(
        string? owner,
        int status
    )
    {
        var source = new FictionalItinerarySource();
        await using var fixture = await TripsHttpFixture.Start(
            itinerary: true,
            configure: s => s.AddSingleton<IItineraryFactSource>(source)
        );
        fixture.Client.DefaultRequestHeaders.Remove("X-Trips-Owner");
        if (owner != null)
            fixture.Client.DefaultRequestHeaders.Add("X-Trips-Owner", owner);
        using var response = await Candidate(
            fixture,
            "/api/trips/aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa",
            body: "PRIVATE_BODY"
        );
        ((int)response.StatusCode).ShouldBe(status);
        source.Calls.ShouldBe(0);
        fixture.Store.Loads.ShouldBe(0);
        fixture.Protection.Reads.ShouldBe(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Complete_multibyte_response_straddles_actual_HTTP_cap_without_writes_or_retry(
        bool aboveLimit
    )
    {
        const int maximum = 128 * 1024;
        var brief = new TripBriefInput(
            "moscow",
            "saint-petersburg",
            new(2026, 10, 22),
            new(2026, 10, 24),
            2,
            "RUB",
            "15000.00",
            ["architecture"],
            "active"
        );
        var encoder = new ItineraryCandidateResponseEncoder();
        var baselineFacts = FactsWithEmojiRights(1);
        var baseline = await encoder.EncodeAsync(Computed(baselineFacts), Ct);
        baseline.IsError.ShouldBeFalse();
        using var baselineJson = JsonDocument.Parse(baseline.Value);
        var evidenceCount = baselineJson.RootElement.GetProperty("evidence").GetArrayLength();
        evidenceCount.ShouldBe(21);
        // The production serializer escapes each supplementary scalar as two six-byte UTF16 escapes.
        var escapedEmojiBytes = Encoding.UTF8.GetByteCount(JsonSerializer.Serialize("😀")) - 2;
        escapedEmojiBytes.ShouldBe(12);
        var step = evidenceCount * escapedEmojiBytes;
        var largestFittingScalars = 1 + (maximum - baseline.Value.Length) / step;
        largestFittingScalars.ShouldBe(422);
        var expectedBelowBytes = baseline.Value.Length + (largestFittingScalars - 1) * step;
        expectedBelowBytes.ShouldBe(131017);
        expectedBelowBytes.ShouldBeInRange(maximum - step + 1, maximum);
        (expectedBelowBytes + step).ShouldBe(131269);
        var scalarCount = largestFittingScalars + (aboveLimit ? 1 : 0);
        var facts = FactsWithEmojiRights(scalarCount);
        var expected = await encoder.EncodeAsync(Computed(facts), Ct);
        if (aboveLimit)
            expected.IsError.ShouldBeTrue();
        else
        {
            expected.IsError.ShouldBeFalse();
            expected.Value.Length.ShouldBe(expectedBelowBytes);
        }

        var source = new FictionalItinerarySource { Facts = facts };
        var memory = new MemoryTripStore();
        var store = new ObservedItineraryStore(memory);
        await using var fixture = await TripsHttpFixture.Start(
            itinerary: true,
            configure: services =>
            {
                services.AddSingleton<IItineraryFactSource>(source);
                services.AddSingleton<ITripPlanStore>(store);
            }
        );
        fixture
            .App.Services.GetRequiredService<IItineraryCandidateResponseEncoder>()
            .ShouldBeOfType<ItineraryCandidateResponseEncoder>();
        fixture
            .App.Services.GetRequiredService<ITripPlanCodec>()
            .ShouldBeOfType<TripPlanJsonCodec>();
        var create = CreateJson
            .Replace(
                "\"endDate\":\"2026-10-22\"",
                "\"endDate\":\"2026-10-24\"",
                StringComparison.Ordinal
            )
            .Replace("\"pace\":\"relaxed\"", "\"pace\":\"active\"", StringComparison.Ordinal);
        var path = await Create(fixture, create);
        var saved = memory.Rows.Single().Value;
        var writesBefore = store.Writes;
        var readsBefore = memory.Loads;
        var decryptionsBefore = fixture.Protection.Reads;
        var authenticationsBefore = fixture.Authentication.Calls;
        using var response = await Candidate(fixture, path);
        var raw = await response.Content.ReadAsByteArrayAsync(Ct);
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
        response.Headers.RetryAfter.ShouldBeNull();
        if (aboveLimit)
        {
            response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
            await Problem(response, "Trips.ItineraryUnavailable");
            raw.Length.ShouldBeLessThan(1024);
            using var problem = JsonDocument.Parse(raw);
            problem.RootElement.TryGetProperty("days", out _).ShouldBeFalse();
            problem.RootElement.TryGetProperty("evidence", out _).ShouldBeFalse();
            problem.RootElement.TryGetProperty("outcome", out _).ShouldBeFalse();
            problem.RootElement.TryGetProperty("candidateVersion", out _).ShouldBeFalse();
        }
        else
        {
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            response.Content.Headers.ContentType!.MediaType.ShouldBe("application/json");
            raw.Length.ShouldBe(expectedBelowBytes);
            raw.Length.ShouldBeInRange(maximum - 1024, maximum);
            response.Content.Headers.ContentLength.ShouldBe((long)raw.Length);
            using var complete = JsonDocument.Parse(raw, new JsonDocumentOptions { MaxDepth = 16 });
            var result = complete.RootElement;
            result.GetProperty("outcome").GetString().ShouldBe("candidate");
            result.GetProperty("baseRevision").GetString().ShouldBe("1");
            var days = result.GetProperty("days");
            days.GetArrayLength().ShouldBe(3);
            days.EnumerateArray()
                .Sum(day => day.GetProperty("blocks").GetArrayLength())
                .ShouldBe(12);
            days.EnumerateArray().Sum(day => day.GetProperty("edges").GetArrayLength()).ShouldBe(9);
            result.GetProperty("evidence").GetArrayLength().ShouldBe(evidenceCount);
            var rights = string.Concat(Enumerable.Repeat("😀", scalarCount));
            foreach (var evidence in result.GetProperty("evidence").EnumerateArray())
                evidence.GetProperty("rightsEvidence").GetString().ShouldBe(rights);
            // Match the entire actual response to the production projection. Only generator UUIDs vary.
            using var projected = JsonDocument.Parse(expected.Value);
            var actualJson = Encoding.UTF8.GetString(raw);
            for (var d = 0; d < 3; d++)
            for (var b = 0; b < 4; b++)
            {
                var actualId = days[d]
                    .GetProperty("blocks")[b]
                    .GetProperty("proposedBlockId")
                    .GetString()!;
                Guid.Parse(actualId).ShouldNotBe(Guid.Empty);
                var expectedId = projected
                    .RootElement.GetProperty("days")[d]
                    .GetProperty("blocks")[b]
                    .GetProperty("proposedBlockId")
                    .GetString()!;
                actualJson = actualJson.Replace(actualId, expectedId, StringComparison.Ordinal);
            }
            Encoding.UTF8.GetBytes(actualJson).ShouldBe(expected.Value.ToArray());
        }
        // Exactly one HTTP generation attempt: success has initial/final GET, overflow stops after initial GET.
        source.Calls.ShouldBe(1);
        fixture.Authentication.Calls.ShouldBe(authenticationsBefore + 1);
        memory.Loads.ShouldBe(readsBefore + (aboveLimit ? 1 : 2));
        fixture.Protection.Reads.ShouldBe(decryptionsBefore + (aboveLimit ? 1 : 2));
        store.Writes.ShouldBe(writesBefore);
        var current = memory.Rows.Single().Value;
        current.ShouldBeSameAs(saved);
        current.Revision.ShouldBe(saved.Revision);
        current.UpdatedAt.ShouldBe(saved.UpdatedAt);
        current.ProtectedPayload.ShouldBe(saved.ProtectedPayload);
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"HTTP boundary: above={aboveLimit}; emojiScalarsPerEvidence={scalarCount}; referencedEvidence={evidenceCount}; belowBytes={expectedBelowBytes}; oneStepAboveBytes={expectedBelowBytes + step}; actualStatus={(int)response.StatusCode}; actualBytes={raw.Length}; sourceCalls={source.Calls}; storeWritesBefore={writesBefore}; storeWritesAfter={store.Writes}."
        );

        ComputedCandidateResult Computed(ValidatedItineraryFacts data)
        {
            var outcome = ItineraryPolicy.Generate(
                TripBrief.Create(brief).Value,
                data.Destination,
                data,
                FictionalItineraryFacts.Now,
                Ct
            );
            outcome.IsError.ShouldBeFalse();
            outcome.Value.Days.Sum(day => day.Blocks.Count).ShouldBe(12);
            return new ComputedCandidateResult(1, outcome.Value, data);
        }
    }

    private static ValidatedItineraryFacts FactsWithEmojiRights(int scalars)
    {
        var input = FictionalItineraryFacts.Input();
        var rights = string.Concat(Enumerable.Repeat("😀", scalars));
        rights.EnumerateRunes().Count().ShouldBeInRange(1, 500);
        var evidence = input
            .Evidence.Select(e =>
                ItineraryEvidence
                    .Create(
                        e.Id,
                        e.Kind,
                        e.ScopeId,
                        e.Method,
                        e.RightsBasis,
                        rights,
                        e.SourceUrl,
                        e.RetrievedAtUtc,
                        e.ProcessedAtUtc,
                        e.ObservedAt,
                        e.ValidUntil,
                        e.PermittedFields,
                        e.ModelledWalk
                    )
                    .Value
            )
            .ToArray();
        var facts = (input with { Evidence = evidence }).Build();
        facts.IsError.ShouldBeFalse();
        return facts.Value;
    }

    private sealed class ObservedItineraryStore(MemoryTripStore inner) : ITripPlanStore
    {
        internal int Writes { get; private set; }

        public Task<ErrorOr<TripPlanRecord?>> LoadAsync(
            OwnerIdentity owner,
            Guid id,
            CancellationToken ct
        ) => inner.LoadAsync(owner, id, ct);

        public Task<ErrorOr<TripPlanRecordPage>> ListAsync(
            OwnerIdentity owner,
            TripPlanPosition? after,
            int limit,
            CancellationToken ct
        ) => inner.ListAsync(owner, after, limit, ct);

        public Task<ErrorOr<Success>> TryCreateAsync(
            TripPlanRecord row,
            int capacity,
            CancellationToken ct
        )
        {
            Writes++;
            return inner.TryCreateAsync(row, capacity, ct);
        }

        public Task<ErrorOr<Success>> TryReplaceAsync(
            long revision,
            TripPlanRecord row,
            CancellationToken ct
        )
        {
            Writes++;
            return inner.TryReplaceAsync(revision, row, ct);
        }

        public Task<ErrorOr<Success>> TryDeleteAsync(
            OwnerIdentity owner,
            Guid id,
            long revision,
            CancellationToken ct
        )
        {
            Writes++;
            return inner.TryDeleteAsync(owner, id, revision, ct);
        }
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Travel.slnx")))
            directory = directory.Parent;
        return directory!.FullName;
    }

    internal static async Task<string> Create(TripsHttpFixture fixture, string? body = null)
    {
        using var response = await fixture.Client.PostAsync(
            "/api/trips",
            TripsHttpTests.Body(body ?? CreateJson),
            Ct
        );
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return response.Headers.Location!.ToString();
    }

    internal static Task<HttpResponseMessage> Candidate(
        TripsHttpFixture fixture,
        string path,
        string? revision = "\"1\"",
        string body = "{}"
    )
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path + "/itinerary-candidate")
        {
            Content = TripsHttpTests.Body(body),
        };
        if (revision != null)
            request.Headers.TryAddWithoutValidation("If-Match", revision);
        return Send(request);
        async Task<HttpResponseMessage> Send(HttpRequestMessage message)
        {
            using (message)
                return await fixture.Client.SendAsync(message, Ct);
        }
    }

    private static async Task Problem(HttpResponseMessage response, string code)
    {
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        json.RootElement.GetProperty("code").GetString().ShouldBe(code);
        json.RootElement.GetProperty("status").GetInt32().ShouldBe((int)response.StatusCode);
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
    }

    private sealed class UnknownLengthContent(byte[] bytes) : HttpContent
    {
        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }

        protected override async Task SerializeToStreamAsync(
            Stream stream,
            TransportContext? context
        )
        {
            foreach (var b in bytes)
                await stream.WriteAsync(new[] { b });
        }
    }

    private sealed class HeldEncoder : IItineraryCandidateResponseEncoder
    {
        private int calls;
        internal TaskCompletionSource Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<ErrorOr<ReadOnlyMemory<byte>>> EncodeAsync(
            ItineraryCandidateResult result,
            CancellationToken cancellationToken = default
        )
        {
            if (Interlocked.Increment(ref calls) == 2)
                Entered.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return await new ItineraryCandidateResponseEncoder().EncodeAsync(
                result,
                cancellationToken
            );
        }
    }

    private sealed class DeadlineEncoder : IItineraryCandidateResponseEncoder
    {
        public async Task<ErrorOr<ReadOnlyMemory<byte>>> EncodeAsync(
            ItineraryCandidateResult result,
            CancellationToken cancellationToken = default
        )
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return await new ItineraryCandidateResponseEncoder().EncodeAsync(
                result,
                cancellationToken
            );
        }
    }
}
