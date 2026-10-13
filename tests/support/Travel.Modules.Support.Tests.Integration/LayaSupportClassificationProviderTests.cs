using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using Shouldly;
using Travel.Modules.Support.Application;
using Travel.Modules.Support.Core;
using Travel.Modules.Support.Infrastructure.Classification;
using Xunit;

namespace Travel.Modules.Support.Tests.Integration;

// Only fake HTTP: no shared database fixture, DP keys, certificates or model calls.
[Trait("Category", "Protocol")]
public sealed class LayaSupportClassificationProviderTests
{
    private const string Source = "c73908d96ff73c0062c340a11bf7c54ec7d8bc4d";
    private const string Revision = "55cf4c4ebb4ebe31b2550e8bdf3bd21b99753851";
    private const string State =
        "{\"version\":\"support-category-v1\",\"title\":\"Demo refund\",\"messages\":[{\"role\":\"Customer\",\"text\":\"Fictional refund question\"}]}";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Passed_full_reply_without_low_confidence_flag_is_candidate_with_fixed_request()
    {
        using var handler = new ProtocolHandler();
        using var client = new HttpClient(handler);
        var result = (await Provider(client).ClassifyAsync(new(State), Ct)).Value;
        result.Category.ShouldBe(ManualCategory.Refund);
        result.Reason.ShouldBe(SupportClassificationReason.Ready);
        result.ServingProfileId.ShouldNotBeNull();
        result.ServingProfileId!.Length.ShouldBe(64);
        result.ServingProfileId!.All(Uri.IsHexDigit).ShouldBeTrue();
        result.InputTokens.ShouldBe(120);
        handler.Paths.ShouldBe(new[] { "/health", "/v1/systemone", "/health" });
        handler.Authorization.ShouldAllBe(x => x == "Bearer fictional-fixture-key");
        using var body = JsonDocument.Parse(handler.RequestBody!);
        var root = body.RootElement;
        root.EnumerateObject()
            .Select(x => x.Name)
            .Order()
            .ShouldBe(
                new[] { "head_max_len", "max_len", "min_confidence", "model", "questions", "state" }
            );
        root.GetProperty("state").GetString().ShouldBe(State);
        root.GetProperty("model").GetString().ShouldBe("multilingual");
        root.GetProperty("max_len").GetInt32().ShouldBe(512);
        root.GetProperty("head_max_len").GetInt32().ShouldBe(512);
        root.GetProperty("min_confidence").GetDouble().ShouldBe(0.75);
        var questions = root.GetProperty("questions");
        questions.EnumerateObject().Count().ShouldBe(1);
        var question = questions.GetProperty("support_category");
        question.GetProperty("type").GetString().ShouldBe("choice");
        question
            .GetProperty("instructions")
            .GetString()!
            .ShouldNotContain("Fictional refund question");
        question
            .GetProperty("criteria")
            .EnumerateObject()
            .Select(x => x.Name)
            .Order()
            .ShouldBe(
                new[]
                {
                    "Ancillaries",
                    "Cancellation",
                    "FareConditions",
                    "Other",
                    "Payment",
                    "Refund",
                    "Technical",
                    "Ticketing",
                }
            );
        foreach (
            var forbidden in new[]
            {
                "fictional-fixture-key",
                "ownerId",
                "caseId",
                "orderId",
                "hooks",
                "batch",
                "lang",
                "task",
            }
        )
            handler.RequestBody!.ShouldNotContain(forbidden);
    }

    [Theory]
    [InlineData("default")]
    [InlineData("source")]
    [InlineData("image")]
    [InlineData("revision")]
    [InlineData("device")]
    [InlineData("precision")]
    [InlineData("key")]
    [InlineData("path")]
    [InlineData("http")]
    [InlineData("userinfo")]
    [InlineData("query")]
    [InlineData("fragment")]
    [InlineData("threshold")]
    public async Task Invalid_profile_is_disabled_without_http(string defect)
    {
        var options = ValidOptions();
        switch (defect)
        {
            case "default":
                options = new();
                break;
            case "source":
                options.ServerSourceSha = new('a', 40);
                break;
            case "image":
                options.ImageDigest = "latest";
                break;
            case "revision":
                options.CheckpointRevision = new('b', 40);
                break;
            case "device":
                options.ExpectedDevice = "auto";
                break;
            case "precision":
                options.ExpectedPrecision = null;
                break;
            case "key":
                options.ApiKey = "secret\r\nInjected";
                break;
            case "path":
                options.BaseUrl += "/model";
                break;
            case "http":
                options.BaseUrl = "http://laya.fixture.invalid";
                break;
            case "userinfo":
                options.BaseUrl = "https://user@laya.fixture.invalid";
                break;
            case "query":
                options.BaseUrl += "?token=secret";
                break;
            case "fragment":
                options.BaseUrl += "#frag";
                break;
            case "threshold":
                options.MinEntropyConfidence = double.NaN;
                break;
        }
        using var handler = new ProtocolHandler();
        using var client = new HttpClient(handler);
        var provider = Provider(client, options);
        provider.IsEnabled.ShouldBeFalse();
        (await provider.ClassifyAsync(new(State), Ct)).Value.Reason.ShouldBe(
            SupportClassificationReason.Disabled
        );
        handler.Paths.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("missing-loaded")]
    [InlineData("revision")]
    [InlineData("device")]
    [InlineData("missing-count")]
    [InlineData("negative-count")]
    [InlineData("fractional-count")]
    [InlineData("count-reset")]
    [InlineData("scoped-fallback")]
    [InlineData("duplicate")]
    [InlineData("oversized")]
    public async Task Identity_failures_including_restored_cuda_after_fallback_require_manual(
        string defect
    )
    {
        var health = Health();
        var after = Health();
        switch (defect)
        {
            case "missing-loaded":
                health["loaded"] = new JsonArray();
                break;
            case "revision":
                health["revisions"]!["multilingual"] = new string('a', 40);
                break;
            case "device":
                health["checkpoint_devices"]!["multilingual"] = "cpu";
                break;
            case "missing-count":
                health.Remove("cpu_fallbacks");
                break;
            case "negative-count":
                health["cpu_fallbacks"]!["multilingual"]!["count"] = -1;
                break;
            case "fractional-count":
                health["cpu_fallbacks"]!["multilingual"]!["count"] = 0.5;
                break;
            case "count-reset":
                health["cpu_fallbacks"]!["multilingual"]!["count"] = 2;
                break;
            case "scoped-fallback":
                after["cpu_fallbacks"]!["multilingual"]!["count"] = 1;
                break;
        }
        using var handler = new ProtocolHandler
        {
            BeforeHealth =
                defect == "duplicate"
                    ? health
                        .ToJsonString()
                        .Replace("\"status\":\"ok\"", "\"status\":\"ok\",\"status\":\"ok\"")
                : defect == "oversized" ? new string(' ', 65537)
                : health.ToJsonString(),
            AfterHealth = after.ToJsonString(),
        };
        using var client = new HttpClient(handler);
        var result = (await Provider(client).ClassifyAsync(new(State), Ct)).Value;
        result.Category.ShouldBeNull();
        result.Reason.ShouldBe(SupportClassificationReason.IdentityMismatch);
        handler.Paths.Count(x => x == "/v1/systemone").ShouldBeLessThanOrEqualTo(1);
    }

    [Theory]
    [InlineData("no-usage")]
    [InlineData("missing-confidence")]
    [InlineData("strict-jev")]
    [InlineData("wrong-type")]
    [InlineData("extra-answer")]
    [InlineData("unknown-category")]
    [InlineData("extra-probability")]
    [InlineData("sum")]
    [InlineData("not-argmax")]
    [InlineData("tie")]
    [InlineData("answer-disagrees")]
    [InlineData("negative")]
    [InlineData("entropy-range")]
    [InlineData("dropped")]
    [InlineData("truncated")]
    [InlineData("questions-truncated")]
    [InlineData("collapsed")]
    [InlineData("wrong-model")]
    [InlineData("wrong-repo")]
    [InlineData("unknown-field")]
    [InlineData("duplicate")]
    [InlineData("oversized")]
    [InlineData("malformed")]
    [InlineData("depth")]
    public async Task Invalid_full_protocol_cannot_produce_candidate_or_leak_diagnostics(
        string defect
    )
    {
        var reply = Reply();
        var answer = reply["answers"]!["support_category"]!;
        var probabilities = answer["probabilities"]!;
        switch (defect)
        {
            case "no-usage":
                reply.Remove("usage");
                break;
            case "missing-confidence":
                answer.AsObject().Remove("answer_confidence");
                break;
            case "strict-jev":
                reply.Remove("routing");
                break;
            case "wrong-type":
                answer["type"] = "score";
                break;
            case "extra-answer":
                reply["answers"]!["extra"] = answer.DeepClone();
                break;
            case "unknown-category":
                answer["choice"] = "Unclassified";
                break;
            case "extra-probability":
                probabilities["Unknown"] = 0;
                break;
            case "sum":
                probabilities["Refund"] = 0.7;
                break;
            case "not-argmax":
                answer["choice"] = "Payment";
                break;
            case "tie":
                probabilities["Refund"] = 0.5;
                probabilities["Payment"] = 0.5;
                foreach (
                    var label in new[]
                    {
                        "Ticketing",
                        "Cancellation",
                        "FareConditions",
                        "Ancillaries",
                        "Technical",
                        "Other",
                    }
                )
                    probabilities[label] = 0;
                answer["answer_confidence"] = 0.5;
                break;
            case "answer-disagrees":
                answer["answer_confidence"] = 0.95;
                break;
            case "negative":
                probabilities["Payment"] = -0.01;
                break;
            case "entropy-range":
                answer["confidence"] = 1.1;
                break;
            case "dropped":
                reply["usage"]!["state_tokens_dropped"] = 1;
                break;
            case "truncated":
                reply["usage"]!["truncated"] = true;
                break;
            case "questions-truncated":
                reply["usage"]!["truncated_questions"] = new JsonArray("support_category");
                break;
            case "collapsed":
                reply["usage"]!["options"] = JsonNode.Parse(
                    "{\"support_category\":{\"total\":8,\"distinct\":7,\"tokens_per_option\":1}}"
                );
                break;
            case "wrong-model":
                reply["routing"]!["model"] = "english";
                break;
            case "wrong-repo":
                reply["routing"]!["repo"] = "convaiinnovations/laya-multilingual";
                break;
            case "unknown-field":
                answer["unexpected"] = "private provider diagnostic";
                break;
        }
        var text = defect switch
        {
            "duplicate" => reply
                .ToJsonString()
                .Replace("\"choice\":\"Refund\"", "\"choice\":\"Refund\",\"choice\":\"Refund\""),
            "oversized" => new string(' ', 65537),
            "malformed" => "{\"private\":\"private provider diagnostic\",\"bad\":NaN}",
            "depth" => new string('[', 30) + "0" + new string(']', 30),
            _ => reply.ToJsonString(),
        };
        using var handler = new ProtocolHandler { Response = text };
        using var client = new HttpClient(handler);
        var result = (await Provider(client).ClassifyAsync(new(State), Ct)).Value;
        result.Category.ShouldBeNull();
        result.Reason.ShouldBeOneOf(
            SupportClassificationReason.InvalidResponse,
            SupportClassificationReason.IdentityMismatch
        );
        result.ToString().ShouldNotContain("private provider diagnostic");
    }

    [Theory]
    [InlineData("entropy")]
    [InlineData("answer")]
    [InlineData("missing-gate")]
    [InlineData("abstained")]
    [InlineData("unevaluated")]
    [InlineData("threshold")]
    [InlineData("low-flag")]
    public async Task Independent_confidence_and_matching_evaluated_gate_are_required(string defect)
    {
        var reply = Reply();
        var answer = reply["answers"]!["support_category"]!;
        switch (defect)
        {
            case "entropy":
                answer["confidence"] = 0.59;
                break;
            case "answer":
                answer["probabilities"]!["Refund"] = 0.7;
                answer["probabilities"]!["Payment"] = 0.18;
                answer["answer_confidence"] = 0.7;
                break;
            case "missing-gate":
                answer.AsObject().Remove("abstention");
                break;
            case "abstained":
                answer["abstention"] = "abstained";
                break;
            case "unevaluated":
                answer["abstention"] = "unevaluated";
                break;
            case "threshold":
                answer["abstention_threshold"] = 0;
                break;
            case "low-flag":
                answer["low_confidence"] = true;
                break;
        }
        using var handler = new ProtocolHandler { Response = reply.ToJsonString() };
        using var client = new HttpClient(handler);
        var result = (await Provider(client).ClassifyAsync(new(State), Ct)).Value;
        result.Category.ShouldBeNull();
        result.Reason.ShouldBe(SupportClassificationReason.LowConfidence);
    }

    [Fact]
    public async Task False_flag_is_valid_and_profile_excludes_secret_but_binds_precision()
    {
        var reply = Reply();
        reply["answers"]!["support_category"]!["low_confidence"] = false;
        using var handler = new ProtocolHandler { Response = reply.ToJsonString() };
        using var client = new HttpClient(handler);
        var first = (await Provider(client).ClassifyAsync(new(State), Ct)).Value;
        first.Category.ShouldBe(ManualCategory.Refund);
        var rotated = ValidOptions();
        rotated.ApiKey = "other-fictional-key";
        (
            await Provider(client, rotated).ClassifyAsync(new(State), Ct)
        ).Value.ServingProfileId.ShouldBe(first.ServingProfileId);
        rotated.ExpectedPrecision = "fp16";
        (
            await Provider(client, rotated).ClassifyAsync(new(State), Ct)
        ).Value.ServingProfileId.ShouldNotBe(first.ServingProfileId);
    }

    [Fact]
    public async Task Oversized_or_invalid_unicode_input_never_starts_http()
    {
        using var handler = new ProtocolHandler();
        using var client = new HttpClient(handler);
        foreach (
            var input in new[]
            {
                new string('a', 8001),
                new string('\uD800', 1),
                new string('界', 8001),
            }
        )
            (await Provider(client).ClassifyAsync(new(input), Ct)).Value.Reason.ShouldBe(
                SupportClassificationReason.InputTooLarge
            );
        handler.Paths.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(401)]
    [InlineData(422)]
    [InlineData(503)]
    [InlineData(302)]
    public async Task Http_failure_is_fixed_manual_with_one_post(int status)
    {
        using var handler = new ProtocolHandler { PostStatus = (HttpStatusCode)status };
        using var client = new HttpClient(handler);
        var result = (await Provider(client).ClassifyAsync(new(State), Ct)).Value;
        result.Category.ShouldBeNull();
        result.Reason.ShouldBe(SupportClassificationReason.Unavailable);
        handler.Paths.Count(x => x == "/v1/systemone").ShouldBe(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Timeout_or_lost_reply_is_uncertain_with_zero_replay(bool timeout)
    {
        using var handler = new ProtocolHandler
        {
            PostException = timeout
                ? new TaskCanceledException("private timeout")
                : new HttpRequestException("private lost reply"),
        };
        using var client = new HttpClient(handler);
        var result = (await Provider(client).ClassifyAsync(new(State), Ct)).Value;
        result.Category.ShouldBeNull();
        result.Reason.ShouldBe(SupportClassificationReason.Uncertain);
        handler.Paths.Count(x => x == "/v1/systemone").ShouldBe(1);
        result.ToString().ShouldNotContain("private");
    }

    [Fact]
    public async Task Caller_cancellation_propagates_without_replay()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        using var handler = new ProtocolHandler { BeforePost = () => cts.Cancel() };
        using var client = new HttpClient(handler);
        await Should.ThrowAsync<OperationCanceledException>(async () =>
            await Provider(client).ClassifyAsync(new(State), cts.Token)
        );
        handler.Paths.Count(x => x == "/v1/systemone").ShouldBe(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Concurrent_identity_drift_stops_pending_input_and_inflight_candidate_disclosure(
        bool inferenceAlreadyStarted
    )
    {
        using var handler = new InterleavedHealthHandler(inferenceAlreadyStarted);
        using var client = new HttpClient(handler);
        var provider = Provider(client);
        var pending = provider.ClassifyAsync(new(State), Ct);
        await handler.Paused.WaitAsync(TimeSpan.FromSeconds(5), Ct);

        var invalidator = (await provider.ClassifyAsync(new(State), Ct)).Value;
        invalidator.Reason.ShouldBe(SupportClassificationReason.IdentityMismatch);
        provider.IsEnabled.ShouldBeFalse();
        handler.Release();

        var result = (await pending).Value;
        result.Category.ShouldBeNull();
        result.Reason.ShouldBe(
            inferenceAlreadyStarted
                ? SupportClassificationReason.Uncertain
                : SupportClassificationReason.IdentityMismatch
        );
        handler.PostCount.ShouldBe(inferenceAlreadyStarted ? 1 : 0);
        (await provider.ClassifyAsync(new(State), Ct)).Value.Reason.ShouldBe(
            SupportClassificationReason.Disabled
        );
        handler.PostCount.ShouldBe(inferenceAlreadyStarted ? 1 : 0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Unbounded_length_stream_still_enforces_utf8_and_response_byte_limit(
        bool invalidUtf8
    )
    {
        var valid = Encoding.UTF8.GetBytes(Reply().ToJsonString());
        byte[] bytes;
        if (invalidUtf8)
        {
            bytes = valid;
            var offset = Encoding
                .UTF8.GetString(bytes)
                .IndexOf("Explicit model", StringComparison.Ordinal);
            offset.ShouldBeGreaterThan(0);
            bytes[offset] = 0xff; // Inside an otherwise valid JSON string; replacement decoding would accept it.
        }
        else
        {
            bytes = Enumerable.Repeat((byte)' ', 65537).ToArray();
            valid.CopyTo(bytes, 0); // A valid reply plus whitespace; an unbounded decoder would accept it.
        }
        using var handler = new ProtocolHandler { StreamedResponse = bytes };
        using var client = new HttpClient(handler);
        var result = (await Provider(client).ClassifyAsync(new(State), Ct)).Value;
        result.Category.ShouldBeNull();
        result.Reason.ShouldBe(SupportClassificationReason.InvalidResponse);
        handler.Paths.Count(x => x == "/v1/systemone").ShouldBe(1);
    }

    private sealed class NonSeekingMemoryStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
    }

    private sealed class InterleavedHealthHandler(bool inferenceAlreadyStarted) : HttpMessageHandler
    {
        private readonly TaskCompletionSource paused = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private readonly TaskCompletionSource release = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private int healthCalls;
        private int postCount;
        public Task Paused => paused.Task;
        public int PostCount => Volatile.Read(ref postCount);

        public void Release() => release.TrySetResult();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            if (request.Method == HttpMethod.Post)
            {
                Interlocked.Increment(ref postCount);
                return Json(Reply());
            }
            var index = Interlocked.Increment(ref healthCalls);
            var pauseAt = inferenceAlreadyStarted ? 2 : 1;
            if (index == pauseAt)
            {
                paused.TrySetResult();
                await release.Task.WaitAsync(cancellationToken);
            }
            var health = Health();
            if (index == pauseAt + 1)
                health["revisions"]!["multilingual"] = new string('a', 40);
            return Json(health);
        }

        private static HttpResponseMessage Json(JsonObject value) =>
            new(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    value.ToJsonString(),
                    Encoding.UTF8,
                    "application/json"
                ),
            };
    }

    private static LayaSupportClassificationProvider Provider(
        HttpClient client,
        LayaClassificationOptions? options = null
    ) => new(client, Options.Create(options ?? ValidOptions()));

    private static LayaClassificationOptions ValidOptions() =>
        new()
        {
            Enabled = true,
            BaseUrl = "https://laya.fixture.invalid",
            ApiKey = "fictional-fixture-key",
            ServerSourceSha = Source,
            ImageDigest = "sha256:" + new string('a', 64),
            CheckpointRevision = Revision,
            ExpectedDevice = "cuda:0",
            ExpectedPrecision = "fp32",
        };

    private static JsonObject Health() =>
        JsonNode
            .Parse(
                """
                {"status":"ok","loaded":["multilingual"],"revisions":{"multilingual":"55cf4c4ebb4ebe31b2550e8bdf3bd21b99753851"},"device":"cuda:0","device_is_preference":false,"checkpoint_devices":{"multilingual":"cuda:0"},"cpu_fallbacks":{"multilingual":{"count":0,"last_reason":null}}}
                """
            )!
            .AsObject();

    private static JsonObject Reply() =>
        JsonNode
            .Parse(
                """
                {"model":"laya-rl-agent","answers":{"support_category":{"type":"choice","choice":"Refund","probabilities":{"Payment":0.03,"Ticketing":0.02,"Cancellation":0.01,"Refund":0.85,"FareConditions":0.02,"Ancillaries":0.02,"Technical":0.03,"Other":0.02},"confidence":0.7,"answer_confidence":0.85,"action":{"act_probability":1.0},"x_jev_confidence":0.8286,"abstention":"passed","abstention_threshold":0.75}},"usage":{"input_tokens":120,"output_tokens":0,"state_tokens":30,"state_tokens_dropped":0,"truncated":false,"truncated_questions":[]},"routing":{"model":"multilingual","repo":"convaiinnovations/laya/multilingual","reason":"Explicit model","detection":null,"workflow":null}}
                """
            )!
            .AsObject();

    private sealed class ProtocolHandler : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];
        public List<string> Authorization { get; } = [];
        public string? RequestBody { get; private set; }
        public string BeforeHealth { get; init; } = Health().ToJsonString();
        public string AfterHealth { get; init; } = Health().ToJsonString();
        public string Response { get; init; } = Reply().ToJsonString();
        public byte[]? StreamedResponse { get; init; }
        public HttpStatusCode PostStatus { get; init; } = HttpStatusCode.OK;
        public Exception? PostException { get; init; }
        public Action? BeforePost { get; init; }
        private int healthCalls;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Paths.Add(request.RequestUri!.AbsolutePath);
            Authorization.Add(request.Headers.Authorization!.ToString());
            if (request.Method == HttpMethod.Get)
                return new(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        healthCalls++ % 2 == 0 ? BeforeHealth : AfterHealth,
                        Encoding.UTF8,
                        "application/json"
                    ),
                };
            request.Method.ShouldBe(HttpMethod.Post);
            RequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            BeforePost?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
            if (PostException is not null)
                throw PostException;
            HttpContent content;
            if (StreamedResponse is { } bytes)
            {
                content = new StreamContent(new NonSeekingMemoryStream(bytes));
                content.Headers.ContentType = new("application/json") { CharSet = "utf-8" };
                content.Headers.ContentLength.ShouldBeNull();
            }
            else
                content = new StringContent(Response, Encoding.UTF8, "application/json");
            return new(PostStatus) { Content = content };
        }
    }
}
