using Microsoft.Extensions.AI;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Travel.AI.TripsBriefExtraction;
using Travel.IntegrationContracts.AI.TripsBriefExtraction;
using Xunit;

namespace Travel.AI.Tests.TripsBriefExtraction;

public sealed class TripsBriefExtractorTests
{
    internal static TripsBriefOptions Enabled() =>
        new()
        {
            Enabled = true,
            ExecutionAuthorized = true,
            PrivateTextProcessingAuthorized = true,
            ProcessorId = "anthropic",
            Model = "fictional-model",
            PricingModel = "fictional-model",
            PricingVerified = true,
            InputUsdPerMillion = 1,
            OutputUsdPerMillion = 1,
            MaxRequestCostUsd = .03m,
            MaxHourlyCostUsd = .30m,
        };

    internal static TripsBriefRequested Request(TimeProvider time, TripsBriefOptions options) =>
        new(
            Guid.NewGuid(),
            TripsBriefPolicyIdentity.Compute(
                options.ProcessorId,
                options.Model,
                TripsBriefMessageIdentity.PrivacyNoticeVersion
            ),
            "Москва",
            "ru",
            time.GetUtcNow(),
            "Europe/Moscow",
            DateOnly.FromDateTime(time.GetUtcNow().AddHours(3).DateTime),
            time.GetUtcNow().AddSeconds(5)
        );

    internal static string ValidJson =>
        "{\"fields\":{\"destinationId\":{\"state\":\"extracted\",\"value\":\"moscow\",\"evidence\":[{\"start\":0,\"end\":6}],\"alternatives\":[]},"
        + string.Join(
            ",",
            new[] { "startDate", "endDate", "durationDays", "adults", "interests", "pace" }.Select(
                n =>
                    $"\"{n}\":{{\"state\":\"missing\",\"value\":null,\"evidence\":[],\"alternatives\":[]}}"
            )
        )
        + "},\"issues\":[]}";

    [Fact]
    public async Task Ready_requires_committed_admission_and_known_usage()
    {
        var time = new FakeTimeProvider();
        var options = Enabled();
        var ledger = new FakeLedger();
        var chat = new FakeChat { Before = () => ledger.Admitted.ShouldBeTrue() };
        var result = await new TripsBriefExtractor(
            chat,
            options,
            new(options, time),
            time
        ).ExtractAsync(Request(time, options), ledger, TestContext.Current.CancellationToken);
        result.Status.ShouldBe("Ready");
        result.CostUsd.ShouldBe(.000015m);
        ledger.Completed.ShouldBe(1);
        chat.Options!.MaxOutputTokens.ShouldBe(1024);
        chat.Options.Tools.ShouldBeNull();
        ledger.Last!.Fields.ShouldBeNull("only accounting metadata reaches ledger");
    }

    [Theory]
    [InlineData("off", "Disabled")]
    [InlineData("execution", "ModelUnavailable")]
    [InlineData("privacy", "ModelUnavailable")]
    [InlineData("model", "ModelUnavailable")]
    [InlineData("budget", "ModelUnavailable")]
    [InlineData("policy", "ProcessingPolicyChanged")]
    [InlineData("input", "InputUnavailable")]
    [InlineData("anchor", "InputUnavailable")]
    [InlineData("budget-time", "DeadlineExceeded")]
    public async Task Rejected_input_never_dispatches(string kind, string expected)
    {
        var time = new FakeTimeProvider();
        var options = Enabled();
        var request = Request(time, options);
        var ledger = new FakeLedger();
        var chat = new FakeChat();
        switch (kind)
        {
            case "off":
                options.Enabled = false;
                break;
            case "execution":
                options.ExecutionAuthorized = false;
                break;
            case "privacy":
                options.PrivateTextProcessingAuthorized = false;
                break;
            case "model":
                options.PricingModel = "other";
                break;
            case "budget":
                options.MaxHourlyCostUsd = 0;
                break;
            case "policy":
                request = request with { ProcessingPolicyVersion = "stale" };
                break;
            case "input":
                request = request with { Text = new string('я', 1001) };
                break;
            case "anchor":
                request = request with { ReferenceLocalDate = new DateOnly(2001, 1, 1) };
                break;
            case "budget-time":
                request = request with { Deadline = time.GetUtcNow().AddMilliseconds(499) };
                break;
        }
        (
            await new TripsBriefExtractor(chat, options, new(options, time), time).ExtractAsync(
                request,
                ledger,
                TestContext.Current.CancellationToken
            )
        ).Status.ShouldBe(expected);
        chat.Calls.ShouldBe(0);
        ledger.Admitted.ShouldBeFalse();
    }

    [Theory]
    [InlineData("missing-model", "ModelUnavailable")]
    [InlineData("wrong-model", "ModelUnavailable")]
    [InlineData("missing-usage", "AccountingUnavailable")]
    [InlineData("throw", "InferenceFailed")]
    [InlineData("json", "InvalidOutput")]
    [InlineData("duplicate", "InvalidOutput")]
    [InlineData("tool", "InvalidOutput")]
    public async Task Unsafe_provider_outcomes_never_deliver_candidate(string kind, string expected)
    {
        var time = new FakeTimeProvider();
        var options = Enabled();
        var chat = new FakeChat();
        var ledger = new FakeLedger();
        switch (kind)
        {
            case "missing-model":
                chat.Model = null;
                break;
            case "wrong-model":
                chat.Model = "payload-secret";
                break;
            case "missing-usage":
                chat.Usage = null;
                break;
            case "throw":
                chat.Error = new InvalidOperationException("private marker Москва");
                break;
            case "json":
                chat.Json = "```json\n" + ValidJson;
                break;
            case "duplicate":
                chat.Json = ValidJson.Replace("\"issues\":[]", "\"issues\":[],\"issues\":[]");
                break;
            case "tool":
                chat.Tool = true;
                break;
        }
        var result = await new TripsBriefExtractor(
            chat,
            options,
            new(options, time),
            time
        ).ExtractAsync(Request(time, options), ledger, TestContext.Current.CancellationToken);
        result.Status.ShouldBe(expected);
        result.Fields.ShouldBeNull();
        chat.Calls.ShouldBe(1);
        if (kind is "wrong-model" or "missing-model" or "missing-usage" or "throw")
            result.CostUsd.ShouldBeNull();
        result.ToString().ShouldNotContain("private marker");
        result.ModelId.ShouldNotBe("payload-secret");
    }

    [Fact]
    public async Task Accounting_failure_before_dispatch_makes_zero_calls()
    {
        var time = new FakeTimeProvider();
        var options = Enabled();
        var chat = new FakeChat();
        var ledger = new FakeLedger { FailAdmission = true };
        (
            await new TripsBriefExtractor(chat, options, new(options, time), time).ExtractAsync(
                Request(time, options),
                ledger,
                TestContext.Current.CancellationToken
            )
        ).Status.ShouldBe("AccountingUnavailable");
        chat.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task Inference_ignoring_cancellation_keeps_active_lease_and_discards_late_output()
    {
        var time = new FakeTimeProvider();
        var options = Enabled();
        var admission = new TripsBriefAdmission(options, time);
        var pending = new TaskCompletionSource<ChatResponse>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var chat = new FakeChat { Pending = pending };
        var ledger = new FakeLedger();
        var extractor = new TripsBriefExtractor(chat, options, admission, time);
        var handling = extractor.ExtractAsync(
            Request(time, options),
            ledger,
            TestContext.Current.CancellationToken
        );
        chat.Calls.ShouldBe(1);
        time.Advance(TimeSpan.FromMilliseconds(3500));
        var result = await handling.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken
        );
        result.Status.ShouldBe("DeadlineExceeded");
        result.Fields.ShouldBeNull();
        result.CostUsd.ShouldBeNull();
        admission.TryEnter(.001m).ShouldBeNull();
        pending.SetResult(
            new ChatResponse(new ChatMessage(ChatRole.Assistant, ValidJson))
            {
                ModelId = "fictional-model",
                Usage = new() { InputTokenCount = 10, OutputTokenCount = 5 },
            }
        );
        TripsBriefAdmission.Lease? next = null;
        SpinWait
            .SpinUntil(
                () => (next = admission.TryEnter(.001m)) is not null,
                TimeSpan.FromSeconds(2)
            )
            .ShouldBeTrue();
        next!.Dispose();
        ledger.Completed.ShouldBe(1);
        chat.Calls.ShouldBe(1);
    }

    [Fact]
    public void Unknown_cost_retains_hourly_reservation_and_starts_are_limited()
    {
        var time = new FakeTimeProvider();
        var options = Enabled();
        options.MaxHourlyCostUsd = .06m;
        var admission = new TripsBriefAdmission(options, time);
        admission.TryEnter(.001m)!.Dispose();
        admission.TryEnter(.001m)!.Dispose();
        admission.TryEnter(.001m).ShouldBeNull();
        time.Advance(TimeSpan.FromMinutes(59));
        admission.TryEnter(.001m).ShouldBeNull();
        time.Advance(TimeSpan.FromMinutes(1));
        using var afterHour = admission.TryEnter(.001m);
        afterHour.ShouldNotBeNull();
    }

    [Fact]
    public void Known_cost_frees_unused_reservation_but_never_bypasses_six_starts_per_minute()
    {
        var time = new FakeTimeProvider();
        var options = Enabled();
        var admission = new TripsBriefAdmission(options, time);
        for (var n = 0; n < 6; n++)
        {
            using var lease = admission.TryEnter(.001m);
            lease.ShouldNotBeNull();
            lease.Settle(.001m);
        }
        admission.TryEnter(.001m).ShouldBeNull();
        time.Advance(TimeSpan.FromMinutes(1));
        using var next = admission.TryEnter(.001m);
        next.ShouldNotBeNull();
    }

    internal sealed class FakeLedger : ITripsBriefLedger
    {
        public bool Admitted;
        public int Completed;
        public bool FailAdmission;
        public TripsBriefParsed? Last;

        public Task AdmitAsync(Guid correlationId, string model, CancellationToken ct)
        {
            if (FailAdmission)
                throw new InvalidOperationException("private marker");
            Admitted = true;
            return Task.CompletedTask;
        }

        public Task CompleteAsync(TripsBriefParsed result, CancellationToken ct)
        {
            Completed++;
            Last = result;
            return Task.CompletedTask;
        }
    }

    internal sealed class FakeChat : IChatClient
    {
        public string Json = ValidJson;
        public string? Model = "fictional-model";
        public UsageDetails? Usage = new() { InputTokenCount = 10, OutputTokenCount = 5 };
        public Exception? Error;
        public bool Tool;
        public int Calls;
        public ChatOptions? Options;
        public Action? Before;
        public TaskCompletionSource<ChatResponse>? Pending;

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default
        )
        {
            Calls++;
            Options = options;
            Before?.Invoke();
            if (Error is not null)
                throw Error;
            if (Pending is not null)
                return Pending.Task;
            var message = new ChatMessage(ChatRole.Assistant, Json);
            if (Tool)
                message.Contents.Add(
                    new FunctionCallContent("c", "bad", new Dictionary<string, object?>())
                );
            return Task.FromResult(new ChatResponse(message) { ModelId = Model, Usage = Usage });
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }
}
