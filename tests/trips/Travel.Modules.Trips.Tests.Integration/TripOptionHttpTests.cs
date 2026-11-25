using System.Net;
using System.Text;
using System.Text.Json;
using ErrorOr;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Travel.Modules.Trips.Application.Itineraries;
using Travel.Modules.Trips.Application.Options;
using Travel.Modules.Trips.Core;
using Travel.Modules.Trips.Core.Itineraries;
using Travel.Modules.Trips.Infrastructure.Itineraries;
using Xunit;

namespace Travel.Modules.Trips.Tests.Integration;

public sealed class TripOptionHttpTests
{
    private const string Path = "/api/trips/option-preview";
    private const string Brief =
        """{"brief":{"destinationId":"saint-petersburg","originId":"moscow","startDate":"2026-10-28","endDate":"2026-10-30","adults":2,"currency":"RUB","totalBudget":null,"interests":["architecture"],"pace":"relaxed"}}""";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(false, true, true, "Testing", "spb-option")]
    [InlineData(true, false, true, "Testing", "spb-option")]
    [InlineData(true, true, false, "Testing", "spb-option")]
    [InlineData(true, true, true, "Testing", "other")]
    [InlineData(true, true, true, "Development", "spb-option")]
    [InlineData(true, true, true, "Production", "spb-option")]
    public async Task Disabled_routes_are_concealed_before_authentication(
        bool trips,
        bool walking,
        bool preview,
        string environment,
        string profile
    )
    {
        await using var fixture = await TripsHttpFixture.Start(
            enabled: trips,
            itinerary: walking,
            environment: environment,
            optionPreview: preview,
            optionProfile: profile,
            optionCatalogue: "A"
        );
        fixture.Client.DefaultRequestHeaders.Remove("X-Trips-Owner");
        using var discovery = await fixture.Client.GetAsync(Path + "/availability", Ct);
        using var post = await fixture.Client.PostAsync(Path, Body("malformed"), Ct);
        discovery.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        post.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        fixture.Authentication.Calls.ShouldBe(0);
        fixture.App.Services.GetService<TripOptionService>().ShouldBeNull();
    }

    [Theory]
    [InlineData("A", "09:00")]
    [InlineData("B", "20:00")]
    public async Task Current_unsaved_brief_works_without_title_or_workspace_readiness(
        string version,
        string arrival
    )
    {
        await using var fixture = await Start(version);
        using var discovery = await fixture.Client.GetAsync(Path + "/availability", Ct);
        discovery.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var metadata = JsonDocument.Parse(await discovery.Content.ReadAsByteArrayAsync(Ct));
        metadata.RootElement.GetProperty("profileId").GetString().ShouldBe("spb-option");
        metadata
            .RootElement.GetProperty("asOfUtc")
            .GetDateTimeOffset()
            .ShouldBe(TripOptionScenario.HistoricalInstant);
        using var response = await fixture.Client.PostAsync(Path, Body(Brief), Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
        using var json = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(Ct));
        json.RootElement.GetProperty("outcome").GetString().ShouldBe("option");
        var offer = json.RootElement.GetProperty("option");
        offer
            .GetProperty("flight")
            .GetProperty("outbound")
            .GetProperty("arrivesAt")
            .GetDateTimeOffset()
            .ToString("HH:mm")
            .ShouldBe(arrival);
        offer.GetProperty("price").GetProperty("knownSubtotal").GetString().ShouldBe("30000.00");
        fixture.Store.Rows.ShouldBeEmpty();

        using var unsupported = await fixture.Client.PostAsync(
            Path,
            Body(Brief.Replace("\"adults\":2", "\"adults\":3")),
            Ct
        );
        using var noMatch = await fixture.Client.PostAsync(
            Path,
            Body(Brief.Replace("10-28", "10-29")),
            Ct
        );
        foreach (
            var (result, outcome) in new[] { (unsupported, "unsupported"), (noMatch, "noMatch") }
        )
        {
            result.StatusCode.ShouldBe(HttpStatusCode.OK);
            using var payload = JsonDocument.Parse(await result.Content.ReadAsByteArrayAsync(Ct));
            payload.RootElement.GetProperty("outcome").GetString().ShouldBe(outcome);
        }
        fixture.Client.DefaultRequestHeaders.Remove("X-Trips-Owner");
        using var unauth = await fixture.Client.PostAsync(Path, Body(Brief), Ct);
        unauth.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        fixture.Store.Rows.ShouldBeEmpty();
    }

    [Fact]
    public async Task Strict_input_unknown_length_and_private_diagnostics()
    {
        await using var fixture = await Start("A");
        foreach (
            var invalid in new[]
            {
                Brief.Replace("\"brief\":", "\"privateBodyMarker\":1,\"brief\":"),
                Brief.Replace("\"pace\":", "\"pace\":\"active\",\"pace\":"),
                Brief.Replace("\"relaxed\"", "123"),
                Brief.Replace("\"originId\":\"moscow\"", "\"originId\":null"),
                "{",
                "{}",
            }
        )
        {
            using var response = await fixture.Client.PostAsync(Path, Body(invalid), Ct);
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }
        using var forbiddenQuery = await fixture.Client.PostAsync(
            Path + "?clock=privateQueryMarker",
            Body(Brief),
            Ct
        );
        forbiddenQuery.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var malformedGet = await fixture.Client.GetAsync(
            Path + "/availability?variant=B",
            Ct
        );
        malformedGet.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var boundary = Brief.PadRight(4096);
        using var exact = await fixture.Client.PostAsync(Path, Body(boundary), Ct);
        exact.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var tooLarge = await fixture.Client.PostAsync(Path, Body(boundary + " "), Ct);
        tooLarge.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
        using var stream = UnknownLengthBody.Json(boundary + " ");
        using var chunked = await fixture.Client.PostAsync(Path, stream, Ct);
        chunked.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
        var logs = string.Join(" ", fixture.Logs.Values);
        logs.ShouldNotContain("privateBodyMarker");
        logs.ShouldNotContain("privateQueryMarker");
        fixture.Store.Rows.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("group", "groupUnsupported")]
    [InlineData("direction", "directionUnsupported")]
    [InlineData("length", "dateRangeUnsupported")]
    [InlineData("horizon", "horizonUnsupported")]
    public async Task Unsupported_brief_returns_reasons_without_opening_withdrawn_package(
        string field,
        string reason
    )
    {
        var input = field switch
        {
            "group" => Brief.Replace("\"adults\":2", "\"adults\":3"),
            "direction" => Brief.Replace("\"originId\":\"moscow\"", "\"originId\":\"kaliningrad\""),
            "length" => Brief.Replace("10-30", "10-28"),
            "horizon" => Brief.Replace("10-28", "11-28").Replace("10-30", "11-30"),
            _ => throw new ArgumentOutOfRangeException(nameof(field)),
        };
        var source = new BlockingEvidence { FailImmediately = true };
        await using var fixture = await Start(
            "A",
            withdrawn: true,
            configure: services =>
                services.AddSingleton(provider => new TripOptionService(
                    provider.GetRequiredService<TripOptionScenario>(),
                    source,
                    provider.GetRequiredService<ItineraryAdmissionGate>(),
                    () => true
                ))
        );
        using var post = await fixture.Client.PostAsync(Path, Body(input), Ct);
        post.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await post.Content.ReadAsByteArrayAsync(Ct));
        var root = json.RootElement;
        root.GetProperty("outcome").GetString().ShouldBe("unsupported");
        root.GetProperty("reasons").EnumerateArray().Single().GetString().ShouldBe(reason);
        root.GetProperty("option").ValueKind.ShouldBe(JsonValueKind.Null);
        root.GetProperty("datasetHash")
            .GetString()
            .ShouldBe(
                Travel.Modules.Trips.Api.Contracts.TripsItineraryProtocol.SaintPetersburgHash
            );
        source.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task Discovery_independent_of_withdrawn_source_and_preview_fails_closed()
    {
        await using var fixture = await Start("A", withdrawn: true);
        using var get = await fixture.Client.GetAsync(Path + "/availability", Ct);
        get.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var post = await fixture.Client.PostAsync(Path, Body(Brief), Ct);
        post.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        fixture.Store.Rows.ShouldBeEmpty();
    }

    [Fact]
    public async Task Concurrent_requests_share_two_slots_cancellation_and_real_deadline_release_them()
    {
        var slow = new BlockingEvidence();
        await using var fixture = await Start(
            "A",
            configure: s =>
                s.AddSingleton(p => new TripOptionService(
                    p.GetRequiredService<TripOptionScenario>(),
                    slow,
                    p.GetRequiredService<ItineraryAdmissionGate>(),
                    () => false
                ))
        );
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        using var content1 = Body(Brief);
        using var content2 = Body(Brief);
        var first = fixture.Client.PostAsync(Path, content1, cancel.Token);
        var second = fixture.Client.PostAsync(Path, content2, Ct);
        await slow.BothEntered.Task.WaitAsync(TimeSpan.FromSeconds(5), Ct);
        using var outside = await fixture.Client.PostAsync(
            Path,
            Body(Brief.Replace("\"adults\":2", "\"adults\":3")),
            Ct
        );
        outside.StatusCode.ShouldBe(HttpStatusCode.OK);
        slow.Calls.ShouldBe(2);
        using var busy = await fixture.Client.PostAsync(Path, Body(Brief), Ct);
        busy.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        await cancel.CancelAsync();
        await Should.ThrowAsync<OperationCanceledException>(async () => await first);
        using var timedOut = await second;
        timedOut.StatusCode.ShouldBe(HttpStatusCode.GatewayTimeout);
        slow.FailImmediately = true;
        using var after = await fixture.Client.PostAsync(Path, Body(Brief), Ct);
        after.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        slow.Calls.ShouldBe(3);
        fixture.Store.Rows.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("2026-11-24T04:08:52Z", false)]
    [InlineData("2026-10-27T12:00:00Z", true)]
    public async Task Expired_or_altered_package_returns_unavailable_for_an_in_scope_brief(
        string instant,
        bool alterPackage
    )
    {
        using var package = new CuratedSaintPetersburgFactsTests.Package();
        if (alterPackage)
            package.Mutate("saint-petersburg-v1.json", payload => payload["datasetId"] = "changed");
        var scenario = new TripOptionScenario("A", DateTimeOffset.Parse(instant));
        await using var fixture = await Start(
            "A",
            configure: services =>
            {
                var source = new CuratedItineraryFacts(
                    package.Directory,
                    scenario,
                    expectedRelease: CuratedItineraryRelease.ReviewedSaintPetersburgV1
                );
                services.AddSingleton(scenario);
                services.AddSingleton(provider => new TripOptionService(
                    scenario,
                    source,
                    provider.GetRequiredService<ItineraryAdmissionGate>(),
                    () => false
                ));
            }
        );
        var inScope = alterPackage
            ? Brief
            : Brief.Replace("10-28", "11-25").Replace("10-30", "11-27");
        using var response = await fixture.Client.PostAsync(Path, Body(inScope), Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        fixture.Store.Rows.ShouldBeEmpty();
    }

    private sealed class BlockingEvidence : IItineraryFactSource
    {
        private int calls;
        public int Calls => calls;
        public TaskCompletionSource BothEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool FailImmediately { get; set; }

        public async Task<ErrorOr<ValidatedItineraryFacts>> ReadAsync(
            DestinationSelection destination,
            CancellationToken cancellationToken = default
        )
        {
            if (Interlocked.Increment(ref calls) == 2)
                BothEntered.TrySetResult();
            if (!FailImmediately)
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return Error.Failure("Trips.ItineraryUnavailable", "No test data.");
        }
    }

    private sealed class UnknownLengthBody(string text) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            stream.WriteAsync(Encoding.UTF8.GetBytes(text)).AsTask();

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }

        public static UnknownLengthBody Json(string text)
        {
            var content = new UnknownLengthBody(text);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(
                "application/json"
            );
            return content;
        }
    }

    private static Task<TripsHttpFixture> Start(
        string version,
        bool withdrawn = false,
        Action<IServiceCollection>? configure = null
    ) =>
        TripsHttpFixture.Start(
            ready: false,
            itinerary: true,
            environment: "Testing",
            optionPreview: true,
            optionProfile: "spb-option",
            optionCatalogue: version,
            withdrawn: withdrawn,
            configure: configure
        );

    private static StringContent Body(string text) => new(text, Encoding.UTF8, "application/json");
}
