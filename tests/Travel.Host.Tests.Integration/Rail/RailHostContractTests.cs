using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shouldly;
using Travel.Host.Tests.Integration.Hotels;
using Travel.Modules.Rail.Infrastructure;
using VerifyTests;
using Xunit;

namespace Travel.Host.Tests.Integration.Rail;

[Trait("Category", "Integration")]
[Collection(HostIntegrationCollection.Name)]
public sealed class RailHostContractTests
{
    private const string KeyCanary = "rail-host-fictional-key-canary";
    private const string QueryCanary = "RAIL-HOST-QUERY-CANARY";

    [Fact]
    public async Task Isolated_actual_Host_starts_Rail_real_disabled_and_dispatches_nothing()
    {
        var wire = new DenyRealTransport();
        await using var fixture = Fixture(wire, false);
        await fixture.StartAsync(TestContext.Current.CancellationToken);
        fixture.IsolatedConfiguration.ShouldBeTrue();
        var options = fixture.Services.GetRequiredService<IOptions<RailOptions>>().Value;
        options.Enabled.ShouldBeFalse();
        options.Fictional.ShouldBeFalse();
        var profile = fixture.Services.GetRequiredService<RailRuntime>().Current;
        profile.Name.ShouldBe("real");
        profile.Enabled.ShouldBeFalse();
        wire.Calls.ShouldBe(0, "Startup must not call the real transport.");

        using var stations = await fixture.Client.GetAsync(
            "/api/rail/stations?q=" + QueryCanary,
            TestContext.Current.CancellationToken
        );
        var stationBody = await Body(stations, HttpStatusCode.OK);
        stationBody.GetProperty("kind").GetString().ShouldBe("skipped");
        stationBody.GetProperty("catalogueGeneration").ValueKind.ShouldBe(JsonValueKind.Null);
        stationBody.GetProperty("attribution").ValueKind.ShouldBe(JsonValueKind.Null);
        using var request = SearchRequest("11111111111111111111111111111111");
        using var search = await fixture.Client.SendAsync(
            request,
            TestContext.Current.CancellationToken
        );
        var searchBody = await Body(search, HttpStatusCode.OK);
        searchBody.GetProperty("kind").GetString().ShouldBe("skipped");
        searchBody.GetProperty("items").GetArrayLength().ShouldBe(0);
        searchBody.GetProperty("modes").GetArrayLength().ShouldBe(2);
        wire.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task Actual_anonymous_Host_executes_catalogue_and_exact_normalized_search_without_real_wire_or_query_logs()
    {
        var wire = new DenyRealTransport();
        var capture = new Capture();
        await using var fixture = Fixture(wire, true, capture);
        await fixture.StartAsync(TestContext.Current.CancellationToken);
        fixture.IsolatedConfiguration.ShouldBeTrue();
        wire.Calls.ShouldBe(0);
        var profile = fixture.Services.GetRequiredService<RailRuntime>().Current;
        profile.Name.ShouldBe("fictional");
        profile.Enabled.ShouldBeTrue();
        using (
            var canary = await fixture.Client.GetAsync(
                "/api/rail/stations?q=" + QueryCanary,
                TestContext.Current.CancellationToken
            )
        )
            (await Body(canary, HttpStatusCode.OK))
                .GetProperty("kind")
                .GetString()
                .ShouldBe("complete");
        var stationExample = Catalog().Single(x => x.GetProperty("id").GetString() == "stations");
        using var stations = await fixture.Client.GetAsync(
            stationExample.GetProperty("path").GetString(),
            TestContext.Current.CancellationToken
        );
        var stationBody = await Body(stations, HttpStatusCode.OK);
        stationBody.GetProperty("suggestions").GetArrayLength().ShouldBe(3);
        var generation = stationBody.GetProperty("catalogueGeneration").GetString()!;
        stationBody
            .GetProperty("attribution")
            .GetProperty("frameUrl")
            .GetString()
            .ShouldBe("/rail-fictional-banner.html");
        using var request = SearchRequest(generation);
        using var response = await fixture.Client.SendAsync(
            request,
            TestContext.Current.CancellationToken
        );
        var result = await Body(response, HttpStatusCode.OK);
        result.GetProperty("kind").GetString().ShouldBe("complete");
        result.GetProperty("source").GetString().ShouldBe("yandex-rasp");
        result.GetProperty("profile").GetString().ShouldBe("fictional");
        result.GetProperty("profileEpoch").GetString().ShouldBe(profile.Epoch);
        result
            .GetProperty("request")
            .GetProperty("catalogueGeneration")
            .GetString()
            .ShouldBe(generation);
        result.GetProperty("normalizedCount").GetInt32().ShouldBe(2);
        foreach (var journey in result.GetProperty("items").EnumerateArray())
        {
            journey.GetProperty("departure").GetString().ShouldEndWith("+03:00");
            journey
                .GetProperty("tickets")
                .GetProperty("electronicTicket")
                .GetString()
                .ShouldBe("false");
            var price = journey.GetProperty("tickets").GetProperty("fares")[0].GetProperty("price");
            price.GetProperty("amount").GetString().ShouldBe("1234.05");
            price.GetProperty("currency").GetString().ShouldBe("RUB");
        }
        using var cachedRequest = SearchRequest(generation);
        using var cached = await fixture.Client.SendAsync(
            cachedRequest,
            TestContext.Current.CancellationToken
        );
        (await Body(cached, HttpStatusCode.OK))
            .GetProperty("receivedAt")
            .GetString()
            .ShouldBe(result.GetProperty("receivedAt").GetString());

        using var staleRequest = SearchRequest("11111111111111111111111111111111");
        using var stale = await fixture.Client.SendAsync(
            staleRequest,
            TestContext.Current.CancellationToken
        );
        (await Body(stale, HttpStatusCode.BadRequest))
            .GetProperty("code")
            .GetString()
            .ShouldBe("Rail.StationSelection.Expired");
        using var invalid = await fixture.Client.PostAsync(
            "/api/rail/search",
            new StringContent(
                "{\"unknown\":\"RAIL-BODY-CANARY\"}",
                Encoding.UTF8,
                "application/json"
            ),
            TestContext.Current.CancellationToken
        );
        (await Body(invalid, HttpStatusCode.BadRequest))
            .GetProperty("code")
            .GetString()
            .ShouldBe("Rail.Request.Invalid");
        using var duplicateTemplate = SearchRequest(generation);
        var duplicateJson = (
            await duplicateTemplate.Content!.ReadAsStringAsync(
                TestContext.Current.CancellationToken
            )
        ).TrimEnd();
        using var duplicate = await fixture.Client.PostAsync(
            "/api/rail/search",
            new StringContent(
                duplicateJson[..^1] + ",\"transport\":\"train\"}",
                Encoding.UTF8,
                "application/json"
            ),
            TestContext.Current.CancellationToken
        );
        (await Body(duplicate, HttpStatusCode.BadRequest))
            .GetProperty("code")
            .GetString()
            .ShouldBe("Rail.Request.Invalid");
        using var oversized = await fixture.Client.PostAsync(
            "/api/rail/search",
            new StringContent(new string('x', 8193), Encoding.UTF8, "application/json"),
            TestContext.Current.CancellationToken
        );
        await Body(oversized, HttpStatusCode.RequestEntityTooLarge);
        using var control = await fixture.Client.GetAsync(
            "/api/status?ordinary=RAIL-HOST-CONTROL-CANARY",
            TestContext.Current.CancellationToken
        );
        var logs = string.Join('\n', capture.Events);
        logs.ShouldContain("/api/rail/stations");
        logs.ShouldContain("RAIL-HOST-CONTROL-CANARY");
        logs.ShouldNotContain(QueryCanary);
        logs.ShouldNotContain(KeyCanary);
        logs.ShouldNotContain("RAIL-BODY-CANARY");
        wire.Calls.ShouldBe(0, "Fictional source must never select the real named transport.");
    }

    [Fact]
    public async Task Actual_Host_OpenAPI_declares_Rail_request_query_outcomes_and_preserves_other_module_snapshot()
    {
        var wire = new DenyRealTransport();
        await using var fixture = Fixture(wire, false);
        await fixture.StartAsync(TestContext.Current.CancellationToken);
        var routes = fixture
            .Services.GetRequiredService<EndpointDataSource>()
            .Endpoints.OfType<RouteEndpoint>()
            .Where(x =>
                x.RoutePattern.RawText?.StartsWith("/api/rail/", StringComparison.Ordinal) == true
            )
            .ToArray();
        routes.Length.ShouldBe(2);
        foreach (var route in routes)
            route.Metadata.GetMetadata<IAllowAnonymous>().ShouldNotBeNull();
        using var response = await fixture.Client.GetAsync(
            "/openapi/v1.json",
            TestContext.Current.CancellationToken
        );
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = JsonNode.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)
        )!;
        var paths = json["paths"]!;
        var stations = paths["/api/rail/stations"]!["get"]!;
        var search = paths["/api/rail/search"]!["post"]!;
        var q = stations["parameters"]
            ?.AsArray()
            .SingleOrDefault(x => x?["name"]?.GetValue<string>() == "q");
        q.ShouldNotBeNull("The actual query contract must appear in Host OpenAPI.");
        q!["in"]!.GetValue<string>().ShouldBe("query");
        q["required"]!.GetValue<bool>().ShouldBeTrue();
        q["schema"]!["minLength"]!.GetValue<int>().ShouldBe(3);
        q["schema"]!["maxLength"]!.GetValue<int>().ShouldBe(80);
        search["requestBody"]!["required"]!.GetValue<bool>().ShouldBeTrue();
        var input = Schema(
            json,
            search["requestBody"]!["content"]!["application/json"]!["schema"]!
        );
        TestContext.Current.TestOutputHelper?.WriteLine(
            "RAIL_PUBLIC_INPUT_SCHEMA " + input.ToJsonString()
        );
        (input["additionalProperties"]?.GetValue<bool>()).ShouldBe(false);
        input["required"]!
            .AsArray()
            .Select(x => x!.GetValue<string>())
            .ShouldBe(
                new[] { "from", "to", "catalogueGeneration", "departureDate", "transport" },
                ignoreOrder: true
            );
        var properties = input["properties"]!.AsObject();
        foreach (var name in new[] { "from", "to" })
        {
            var reference = Schema(json, properties[name]!);
            (reference["additionalProperties"]?.GetValue<bool>()).ShouldBe(false);
            reference["required"].ShouldNotBeNull();
            reference["required"]!
                .AsArray()
                .Select(x => x!.GetValue<string>())
                .ShouldBe(new[] { "source", "code" }, ignoreOrder: true);
            foreach (var field in new[] { "source", "code" })
            {
                (reference["properties"]![field]!["readOnly"]?.GetValue<bool>()).ShouldNotBe(true);
                reference["properties"]![field]!["type"]!.GetValue<string>().ShouldBe("string");
            }
        }
        foreach (
            var property in Catalog()
                .Single(x => x.GetProperty("id").GetString() == "search")
                .GetProperty("body")
                .EnumerateObject()
        )
            properties
                .ContainsKey(property.Name)
                .ShouldBeTrue("Missing request field: " + property.Name);
        foreach (var operation in new[] { stations, search })
        {
            operation["responses"]!["200"]!["content"]!["application/json"]!["schema"]!
                ["$ref"]
                .ShouldNotBeNull();
            operation["responses"]!["400"].ShouldNotBeNull();
            operation["responses"]!["429"].ShouldNotBeNull();
            foreach (var status in operation["responses"]!.AsObject())
                status.Value!["headers"]!["Cache-Control"]!["schema"]!["const"]!
                    .GetValue<string>()
                    .ShouldBe("no-store");
        }
        search["responses"]!["413"].ShouldNotBeNull();
        var contract = json.ToJsonString();
        foreach (
            var forbidden in new[]
            {
                KeyCanary,
                "api.rasp.yandex-net.ru",
                "logo_vm",
                "tickets_info",
                "apikey",
            }
        )
            contract.ShouldNotContain(forbidden);
        if (json["servers"] is JsonArray servers)
            foreach (var server in servers.OfType<JsonObject>())
                server["url"] = "<server>";
        var settings = new VerifySettings();
        settings.DisableDiff();
        settings.UseDirectory("../Web/Verified");
        settings.UseFileName("HostWebContractTests.OpenApi");
        await Verifier.Verify(
            json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
            settings
        );
        wire.Calls.ShouldBe(0);
    }

    private static JsonNode Schema(JsonNode document, JsonNode schema) =>
        schema["$ref"] is JsonValue reference
            ? document["components"]!["schemas"]![reference.GetValue<string>().Split('/').Last()]!
            : schema;

    private static HotelsHostFixture Fixture(
        DenyRealTransport wire,
        bool fictional,
        Capture? capture = null
    ) =>
        new()
        {
            AssetsReady = assets =>
                TestContext.Current.TestOutputHelper?.WriteLine(
                    "RAIL_HOST_OWNED_RESOURCES " + JsonSerializer.Serialize(assets)
                ),
            ConfigureTestServices = services =>
            {
                services
                    .AddHttpClient("rail-yandex")
                    .ConfigurePrimaryHttpMessageHandler(() => wire);
                if (fictional)
                    services.PostConfigure<RailOptions>(o =>
                    {
                        o.Enabled = true;
                        o.Fictional = true;
                        o.ApiKey = KeyCanary;
                    });
                if (capture is not null)
                    services.AddLogging(o =>
                    {
                        o.SetMinimumLevel(LogLevel.Debug);
                        o.AddProvider(capture);
                    });
            },
        };

    private static async Task<JsonElement> Body(HttpResponseMessage response, HttpStatusCode status)
    {
        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        foreach (
            var forbidden in new[]
            {
                KeyCanary,
                "api.rasp.yandex-net.ru",
                "tickets_info",
                "logo_vm",
                "RAIL-BODY-CANARY",
            }
        )
            text.ShouldNotContain(forbidden);
        response.StatusCode.ShouldBe(status, text);
        response.Headers.CacheControl?.NoStore.ShouldBe(true);
        using var json = JsonDocument.Parse(text);
        return json.RootElement.Clone();
    }

    private static HttpRequestMessage SearchRequest(string generation)
    {
        var example = Catalog().Single(x => x.GetProperty("id").GetString() == "search");
        var body = example
            .GetProperty("body")
            .GetRawText()
            .Replace("{{catalogueGeneration}}", generation, StringComparison.Ordinal)
            .Replace(
                "{{departureDate}}",
                DateTimeOffset.UtcNow.ToString("yyyy-MM-dd"),
                StringComparison.Ordinal
            );
        return new(HttpMethod.Post, example.GetProperty("path").GetString())
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
    }

    private static JsonElement[] Catalog()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Travel.slnx")))
            root = root.Parent;
        if (root is null)
            throw new DirectoryNotFoundException("Travel.slnx not found.");
        using var document = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(root.FullName, "docs/examples/rail-requests.json"))
        );
        return document.RootElement.EnumerateArray().Select(x => x.Clone()).ToArray();
    }

    private sealed class DenyRealTransport : HttpMessageHandler
    {
        public int Calls;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Interlocked.Increment(ref Calls);
            throw new InvalidOperationException(
                "Real Rail transport forbidden in actual Host fixture."
            );
        }
    }

    private sealed class Capture : ILoggerProvider
    {
        public ConcurrentQueue<string> Events { get; } = new();

        public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

        public void Dispose() { }

        private sealed class Logger(Capture owner, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter
            ) =>
                owner.Events.Enqueue(
                    category + " " + formatter(state, exception) + " " + state + " " + exception
                );
        }
    }
}
