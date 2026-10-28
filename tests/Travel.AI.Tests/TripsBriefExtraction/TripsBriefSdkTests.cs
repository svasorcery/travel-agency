using System.Net;
using Anthropic;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Shouldly;
using Travel.AI.Configuration;
using Travel.AI.TripsBriefExtraction;
using Xunit;
using static Travel.AI.Tests.TripsBriefExtraction.TripsBriefExtractorTests;

namespace Travel.AI.Tests.TripsBriefExtraction;

public sealed class TripsBriefSdkTests
{
    [Theory]
    [InlineData("fictional-model", "Ready")]
    [InlineData("wrong-model", "ModelUnavailable")]
    public async Task Sdk_wire_projection_is_observed_without_authorizing_execution(
        string returnedModel,
        string expected
    )
    {
        using var handler = new WireHandler(returnedModel);
        using var http = new HttpClient(handler);
        using var chat = TripsBriefChatClient.CreateProjectedClient("fictional-unused-key", http);
        var time = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();
        var options = Enabled();
        var result = await new TripsBriefExtractor(
            chat,
            options,
            new(options, time),
            time
        ).ExtractAsync(
            Request(time, options),
            new FakeLedger(),
            TestContext.Current.CancellationToken
        );
        handler.Failure.ShouldBeNull();
        result.Status.ShouldBe(expected);
        handler.Calls.ShouldBe(1);
        handler.LogicalSchemaMatches.ShouldBeFalse(
            "local bounds remain stronger than the provider dialect"
        );
        handler.SchemaChanges.ShouldContain("$.properties.issues.maxItems removed");
        if (expected == "Ready")
        {
            result.InputTokens.ShouldBe(10);
            result.OutputTokens.ShouldBe(5);
            result.CostUsd.ShouldBe(.000015m);
        }
    }

    private sealed class WireHandler(string returnedModel) : HttpMessageHandler
    {
        public int Calls;
        public string ReplyJson = ValidJson;
        public Exception? Failure;
        public bool LogicalSchemaMatches;
        public string[] SchemaChanges = [];
        public int WireBytes;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct
        )
        {
            try
            {
                return await SendCoreAsync(request, ct);
            }
            catch (Exception e)
            {
                Failure = e;
                throw;
            }
        }

        private async Task<HttpResponseMessage> SendCoreAsync(
            HttpRequestMessage request,
            CancellationToken ct
        )
        {
            Calls++;
            using var doc = System.Text.Json.JsonDocument.Parse(
                await request.Content!.ReadAsStringAsync(ct)
            );
            WireBytes = (await request.Content!.ReadAsByteArrayAsync(ct)).Length;
            var root = doc.RootElement;
            root.GetProperty("model").GetString().ShouldBe("fictional-model");
            root.GetProperty("temperature").GetDouble().ShouldBe(0);
            root.GetProperty("max_tokens").GetInt32().ShouldBe(1024);
            root.TryGetProperty("thinking", out _).ShouldBeFalse();
            root.TryGetProperty("tools", out _).ShouldBeFalse();
            var schema = root.GetProperty("output_config")
                .GetProperty("format")
                .GetProperty("schema");
            System
                .Text.Json.JsonElement.DeepEquals(schema, TripsBriefWireProjection.Schema())
                .ShouldBeTrue();
            schema
                .GetProperty("$defs")
                .GetProperty("field")
                .GetProperty("properties")
                .GetProperty("evidence")
                .TryGetProperty("maxItems", out _)
                .ShouldBeFalse();
            var definition = (string)
                typeof(TripsBriefExtractor)
                    .GetField(
                        "Schema",
                        System.Reflection.BindingFlags.Static
                            | System.Reflection.BindingFlags.NonPublic
                    )!
                    .GetValue(null)!;
            using var expectedSchema = System.Text.Json.JsonDocument.Parse(definition);
            LogicalSchemaMatches = System.Text.Json.JsonElement.DeepEquals(
                schema,
                expectedSchema.RootElement
            );
            SchemaChanges = SchemaDifferences(expectedSchema.RootElement, schema, "$").ToArray();
            Convert
                .ToHexStringLower(
                    System.Security.Cryptography.SHA256.HashData(
                        System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(
                            expectedSchema.RootElement
                        )
                    )
                )
                .ShouldBe(TripsBriefExtractor.SchemaSha256());
            HasCacheControl(root).ShouldBeFalse();
            return new(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    System.Text.Json.JsonSerializer.Serialize(
                        new
                        {
                            id = "fictional-message",
                            type = "message",
                            role = "assistant",
                            model = returnedModel,
                            content = new[] { new { type = "text", text = ReplyJson } },
                            stop_reason = "end_turn",
                            usage = new { input_tokens = 10, output_tokens = 5 },
                        }
                    ),
                    System.Text.Encoding.UTF8,
                    "application/json"
                ),
            };
        }

        private static bool HasCacheControl(System.Text.Json.JsonElement e) =>
            e.ValueKind == System.Text.Json.JsonValueKind.Object
                ? e.EnumerateObject()
                    .Any(p => p.Name == "cache_control" || HasCacheControl(p.Value))
                : e.ValueKind == System.Text.Json.JsonValueKind.Array
                    && e.EnumerateArray().Any(HasCacheControl);

        private static IEnumerable<string> SchemaDifferences(
            System.Text.Json.JsonElement expected,
            System.Text.Json.JsonElement actual,
            string path
        )
        {
            if (System.Text.Json.JsonElement.DeepEquals(expected, actual))
                yield break;
            if (
                expected.ValueKind == System.Text.Json.JsonValueKind.Object
                && actual.ValueKind == expected.ValueKind
            )
            {
                foreach (var p in expected.EnumerateObject())
                    if (!actual.TryGetProperty(p.Name, out var other))
                        yield return path + "." + p.Name + " removed";
                    else
                        foreach (
                            var difference in SchemaDifferences(p.Value, other, path + "." + p.Name)
                        )
                            yield return difference;
                foreach (var p in actual.EnumerateObject())
                    if (!expected.TryGetProperty(p.Name, out _))
                        yield return path + "." + p.Name + " added";
            }
            else
                yield return path + " changed";
        }
    }

    [Theory]
    [InlineData("issues")]
    [InlineData("evidence")]
    [InlineData("alternatives")]
    public async Task Projected_wire_never_relaxes_local_array_bounds(string oversized)
    {
        var body = System.Text.Json.Nodes.JsonNode.Parse(ValidJson)!;
        if (oversized == "issues")
            body["issues"] = new System.Text.Json.Nodes.JsonArray(
                Enumerable
                    .Range(0, 17)
                    .Select(_ =>
                        System.Text.Json.Nodes.JsonNode.Parse(
                            "{\"code\":\"unsupportedOrigin\",\"slots\":[\"destinationId\"],\"evidence\":[{\"start\":0,\"end\":6}]}"
                        )
                    )
                    .ToArray()
            );
        else
            body["fields"]!["destinationId"]![oversized] = new System.Text.Json.Nodes.JsonArray(
                Enumerable
                    .Range(0, 5)
                    .Select(_ =>
                        oversized == "evidence"
                            ? System.Text.Json.Nodes.JsonNode.Parse("{\"start\":0,\"end\":6}")
                            : System.Text.Json.Nodes.JsonValue.Create("moscow")
                    )
                    .ToArray()
            );
        using var handler = new WireHandler("fictional-model") { ReplyJson = body.ToJsonString() };
        using var http = new HttpClient(handler);
        using var chat = TripsBriefChatClient.CreateProjectedClient("fictional-unused-key", http);
        var time = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();
        var options = Enabled();
        var reply = await new TripsBriefExtractor(
            chat,
            options,
            new(options, time),
            time
        ).ExtractAsync(
            Request(time, options),
            new FakeLedger(),
            TestContext.Current.CancellationToken
        );
        handler.Failure.ShouldBeNull();
        reply.Status.ShouldBe("InvalidOutput");
        reply.Fields.ShouldBeNull();
        handler.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task All48_frozen_requests_bind_the_complete_projected_wire_envelope()
    {
        var path = CorpusPath();
        foreach (
            var c in Travel
                .Tests.AiEvals.TripsBriefExtraction.TripsBriefEvaluationPacket.Load(path)
                .Cases
        )
        {
            var time = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();
            var options = Enabled();
            var request =
                Travel.Tests.AiEvals.TripsBriefExtraction.TripsBriefEvaluationPacket.Request(
                    c,
                    time.GetUtcNow()
                );
            using var handler = new WireHandler("fictional-model");
            using var http = new HttpClient(handler);
            using var chat = TripsBriefChatClient.CreateProjectedClient(
                "fictional-unused-key",
                http
            );
            await new TripsBriefExtractor(chat, options, new(options, time), time).ExtractAsync(
                request,
                new FakeLedger(),
                TestContext.Current.CancellationToken
            );
            handler.Failure.ShouldBeNull();
            handler.Calls.ShouldBe(1, c.Id);
            var estimate = TripsBriefExtractor.EstimateInput(request, options.Model);
            handler.WireBytes.ShouldBeLessThanOrEqualTo(estimate.EstimatedInputTokens, c.Id);
            estimate.EstimatedInputTokens.ShouldBeLessThanOrEqualTo(4096, c.Id);
        }
    }

    private static string CorpusPath(
        [System.Runtime.CompilerServices.CallerFilePath] string file = ""
    ) =>
        Path.GetFullPath(
            Path.Combine(
                Path.GetDirectoryName(file)!,
                "../../Travel.Tests.AiEvals/TripsBriefExtraction/qualification-cases.json"
            )
        );

    [Fact]
    public async Task Feature_sdk_has_zero_retries_using_only_a_fake_http_handler()
    {
        using var handler = new FailureHandler();
        using var http = new HttpClient(handler);
        var options = TripsBriefChatClient.CreateClientOptions("fictional-unused-key", http);
        options.MaxRetries.ShouldBe(0);
        using var chat = new AnthropicClient(options).AsIChatClient("fictional-model");
        (
            await Record.ExceptionAsync(() =>
                chat.GetResponseAsync(
                    [new(ChatRole.User, "fictional")],
                    new ChatOptions { MaxOutputTokens = 1024 },
                    TestContext.Current.CancellationToken
                )
            )
        ).ShouldNotBeNull();
        handler.Calls.ShouldBe(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Disabled_or_wrong_processor_never_resolves_provider_key(bool wrongProcessor)
    {
        var configuration = Enabled();
        configuration.Enabled = wrongProcessor;
        configuration.ProcessorId = wrongProcessor ? "local" : "anthropic";
        var secret = new ForbiddenOptions();
        using var client = new TripsBriefChatClient(secret, configuration);
        (
            await Record.ExceptionAsync(() =>
                client.GetResponseAsync(
                    [new(ChatRole.User, "fictional")],
                    new ChatOptions { ModelId = "fictional-model" },
                    TestContext.Current.CancellationToken
                )
            )
        ).ShouldBeOfType<InvalidOperationException>();
        secret.Reads.ShouldBe(0);
    }

    private sealed class ForbiddenOptions : IOptions<AnthropicOptions>
    {
        public int Reads;
        public AnthropicOptions Value
        {
            get
            {
                Reads++;
                throw new InvalidOperationException("No credentials authorized.");
            }
        }
    }

    private sealed class FailureHandler : HttpMessageHandler
    {
        public int Calls;
        public string ReplyJson = ValidJson;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Calls++;
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                {
                    Content = new StringContent(
                        "{\"type\":\"error\",\"error\":{\"type\":\"api_error\",\"message\":\"fictional failure\"}}",
                        System.Text.Encoding.UTF8,
                        "application/json"
                    ),
                }
            );
        }
    }
}
