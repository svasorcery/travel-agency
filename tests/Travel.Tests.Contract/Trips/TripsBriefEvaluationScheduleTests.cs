using Shouldly;
using Travel.Tests.AiEvals.TripsBriefExtraction;
using Xunit;
using static Travel.Tests.Contract.Trips.TripsBriefEvaluationTestFixture;

namespace Travel.Tests.Contract.Trips;

public sealed class TripsBriefEvaluationScheduleTests
{
    [Fact]
    public async Task Actual_client_boundary_remains_paced_after_uneven_ledger_admission()
    {
        var clock = new Clock();
        var schedule = new TripsBriefEvaluationSchedule(clock);
        var binding = Binding() with { MaxCalls = 2 };
        var options = Manifest().Options.ToRuntime();
        using var inner = new ScriptedChat(clock);
        using var observed = new TripsBriefEvaluationObservedChatClient(inner, schedule);
        var ledger = new DelayedLedger(clock);
        var pipeline = new ProductionTripsBriefEvaluationPipeline(
            new(observed, options, new(options, clock), clock),
            ledger,
            options
        );
        var result = await TripsBriefEvaluationRunner.RunAuthorizedAsync(
            Packet,
            binding,
            pipeline,
            clock,
            new MechanicalJournal(),
            schedule,
            (delay, ct) =>
            {
                clock.Advance(delay);
                return Task.CompletedTask;
            },
            cancellationToken: TestContext.Current.CancellationToken
        );
        result.ActualProviderStarts.ShouldBe(2);
        result.Dispatched.ShouldBe(2);
        (inner.Starts[1] - inner.Starts[0]).ShouldBeGreaterThanOrEqualTo(TimeSpan.FromSeconds(11));
        result.Cases.Take(2).All(x => x.Status == "Ready").ShouldBeTrue();
    }

    private sealed class DelayedLedger(Clock clock)
        : Travel.AI.TripsBriefExtraction.ITripsBriefLedger
    {
        private int calls;

        public Task AdmitAsync(Guid id, string model, CancellationToken ct)
        {
            if (calls++ == 0)
                clock.Advance(TimeSpan.FromSeconds(1));
            return Task.CompletedTask;
        }

        public Task CompleteAsync(
            Travel.IntegrationContracts.AI.TripsBriefExtraction.TripsBriefParsed metadata,
            CancellationToken ct
        ) => Task.CompletedTask;
    }

    private sealed class ScriptedChat(Clock clock) : Microsoft.Extensions.AI.IChatClient
    {
        public List<TimeSpan> Starts { get; } = [];
        private readonly TripsBriefEvalCase[] cases = TripsBriefEvaluationPacket.Load(Packet).Cases;

        public Task<Microsoft.Extensions.AI.ChatResponse> GetResponseAsync(
            IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages,
            Microsoft.Extensions.AI.ChatOptions? options = null,
            CancellationToken cancellationToken = default
        )
        {
            var c = cases[Starts.Count];
            Starts.Add(TimeSpan.FromTicks(clock.GetTimestamp()));
            var candidate =
                Travel.Modules.Trips.Application.BriefExtraction.TripBriefCandidateValidator.Analyze(
                    TripsBriefEvaluationPacket.Input(c, clock.GetUtcNow())
                );
            var json = System.Text.Json.JsonSerializer.Serialize(
                new { fields = candidate.Fields, issues = candidate.Issues },
                new System.Text.Json.JsonSerializerOptions(
                    System.Text.Json.JsonSerializerDefaults.Web
                )
            );
            return Task.FromResult(
                new Microsoft.Extensions.AI.ChatResponse(
                    new Microsoft.Extensions.AI.ChatMessage(
                        Microsoft.Extensions.AI.ChatRole.Assistant,
                        json
                    )
                )
                {
                    ModelId = "fictional-model",
                    Usage = new() { InputTokenCount = 1, OutputTokenCount = 1 },
                }
            );
        }

        public IAsyncEnumerable<Microsoft.Extensions.AI.ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages,
            Microsoft.Extensions.AI.ChatOptions? options = null,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }

    [Fact]
    public void Late_wait_cannot_catch_up()
    {
        var clock = new Clock();
        var s = new TripsBriefEvaluationSchedule(clock);
        s.Begin(TimeSpan.FromHours(6));
        clock.Advance(TimeSpan.FromSeconds(90));
        s.RecordProviderStart();
        s.DelayBeforeCase(1).ShouldBe(TimeSpan.FromSeconds(11));
        Should.Throw<InvalidOperationException>(() => s.RecordProviderStart());
        Should.Throw<InvalidOperationException>(() => s.Begin(TimeSpan.FromHours(6)));
    }

    [Fact]
    public void Ledger_delay_cannot_shorten_provider_spacing()
    {
        var clock = new Clock();
        var s = new TripsBriefEvaluationSchedule(clock);
        s.Begin(TimeSpan.FromHours(6));
        clock.Advance(TimeSpan.FromSeconds(1)); // First admission is slower.
        s.RecordProviderStart();
        clock.Advance(TimeSpan.FromSeconds(10));
        s.DelayBeforeCase(1).ShouldBe(TimeSpan.FromSeconds(1));
        clock.Advance(TimeSpan.FromSeconds(1));
        s.RecordProviderStart(); // Second admission has no delay.
        s.ProviderStarts.ShouldBe(2);
    }

    [Fact]
    public void Utc_rollback_cannot_extend_run()
    {
        var clock = new Clock();
        var s = new TripsBriefEvaluationSchedule(clock);
        s.Begin(TimeSpan.FromHours(6));
        clock.Advance(TimeSpan.FromHours(6));
        clock.SetUtc(Now.AddYears(-1));
        s.HasBudget(TimeSpan.Zero).ShouldBeFalse();
        Should.Throw<InvalidOperationException>(() => s.RecordProviderStart());
    }
}
