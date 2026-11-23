using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Travel.AI.NlSearch;
using Travel.AI.Observability;
using Travel.AI.Persistence;
using Travel.AI.Persistence.Entities;
using Travel.AI.Tests.Observability;
using Travel.IntegrationContracts.AI.NlSearch;
using Xunit;

namespace Travel.AI.Tests.NlSearch;

public sealed class FlightSearchPreviewAiHandlerTests
{
    private const string PartialQuery = "Хочу в Сочи на выходные, нас двое";
    private const string PartialReply = """
        {"origin":null,"destination":"в Сочи","departure":"на выходные","return":null,"party":"нас двое","tripType":null,"restrictions":[]}
        """;
    private const string FullQuery =
        "Из Москвы в Сочи 12 декабря 2026, обратно 15 декабря 2026, двое взрослых";
    private const string FullReply = """
        {"origin":"Из Москвы","destination":"в Сочи","departure":"12 декабря 2026","return":"обратно 15 декабря 2026","party":"двое взрослых","tripType":null,"restrictions":[]}
        """;

    [Theory]
    [InlineData(PartialQuery, PartialReply, null, "на выходные", null, "нас двое")]
    [InlineData(
        FullQuery,
        FullReply,
        "Из Москвы",
        "12 декабря 2026",
        "обратно 15 декабря 2026",
        "двое взрослых"
    )]
    public async Task Fictional_response_returns_fragments_and_accounts_for_one_preview_attempt(
        string query,
        string reply,
        string? origin,
        string departure,
        string? returnDate,
        string party
    )
    {
        using var chat = new FictionalChatClient(() => Task.FromResult(Response(reply)));
        using var fixture = new HandlerFixture(chat);
        var request = new FlightSearchPreviewRequested(
            query,
            new DateOnly(2026, 11, 23),
            Guid.NewGuid()
        );

        var parsed = await fixture.InvokeAsync(request);

        parsed.CorrelationId.ShouldBe(request.CorrelationId);
        parsed.Fragments.ShouldNotBeNull();
        parsed.Fragments.Origin.ShouldBe(origin);
        parsed.Fragments.Destination.ShouldBe("в Сочи");
        parsed.Fragments.Departure.ShouldBe(departure);
        parsed.Fragments.Return.ShouldBe(returnDate);
        parsed.Fragments.Party.ShouldBe(party);
        parsed.Fragments.TripType.ShouldBeNull();
        parsed.Fragments.Restrictions.ShouldBeEmpty();
        chat.Calls.ShouldBe(1);
        fixture.LegacyChat.Calls.ShouldBe(0);
        chat.Messages.Single(message => message.Role == ChatRole.User)
            .Text.ShouldContain("2026-11-23");
        var format = chat.Options?.ResponseFormat.ShouldBeOfType<ChatResponseFormatJson>();
        format!
            .Schema!.Value.GetProperty("required")
            .EnumerateArray()
            .Select(key => key.GetString())
            .ShouldBe(
                [
                    "origin",
                    "destination",
                    "departure",
                    "return",
                    "party",
                    "tripType",
                    "restrictions",
                ],
                ignoreOrder: true
            );
        chat.Options!.MaxOutputTokens.ShouldBe(512);
        fixture.Ledger.Entries.Select(entry => entry.Outcome).ShouldBe(["Admitted", "Ready"]);
        var row = fixture.Ledger.LastEntry.ShouldNotBeNull();
        row.MessageIdentity.ShouldBe(FlightSearchPreviewMessageIdentity.Requested);
        row.Feature.ShouldBe("flights.nl_search_preview");
        row.CorrelationId.ShouldBe(request.CorrelationId);
        row.Outcome.ShouldBe("Ready");
        row.InputTokens.ShouldBe(100);
        row.OutputTokens.ShouldBe(50);
        row.CostUsd.ShouldBe(0.00175m);
    }

    [Theory]
    [InlineData("{\"destination\":\"в Сочи\"}")]
    [InlineData(
        "{\"origin\":null,\"destination\":null,\"departure\":null,\"return\":null,\"party\":2,\"tripType\":null,\"restrictions\":[]}"
    )]
    [InlineData(
        "{\"origin\":null,\"destination\":null,\"departure\":null,\"return\":null,\"party\":null,\"tripType\":null,\"restrictions\":[null]}"
    )]
    [InlineData(
        "{\"origin\":null,\"destination\":null,\"departure\":null,\"return\":null,\"party\":null,\"tripType\":null,\"restrictions\":[],\"extra\":true}"
    )]
    public async Task Malformed_response_returns_typed_failure_and_accounts_without_retry(
        string body
    )
    {
        using var chat = new FictionalChatClient(() => Task.FromResult(Response(body)));
        using var fixture = new HandlerFixture(chat);

        var parsed = await fixture.InvokeAsync();

        parsed.Fragments.ShouldBeNull();
        chat.Calls.ShouldBe(1);
        fixture.LegacyChat.Calls.ShouldBe(0);
        fixture.Ledger.LastEntry.ShouldNotBeNull().Outcome.ShouldBe("InvalidOutput");
    }

    [Fact]
    public async Task Oversized_response_is_rejected_before_parsing()
    {
        var body = PartialReply + new string(' ', 16 * 1024);
        using var chat = new FictionalChatClient(() => Task.FromResult(Response(body)));
        using var fixture = new HandlerFixture(chat);

        var parsed = await fixture.InvokeAsync();

        parsed.Fragments.ShouldBeNull();
        chat.Calls.ShouldBe(1);
        fixture.Ledger.LastEntry.ShouldNotBeNull().Outcome.ShouldBe("OutputBudgetExceeded");
    }

    [Fact]
    public async Task Prompt_schema_and_query_share_the_input_limit()
    {
        using var chat = new FictionalChatClient(() => Task.FromResult(Response(PartialReply)));
        var request = new FlightSearchPreviewRequested(
            new string('я', 900),
            new DateOnly(2026, 11, 23),
            Guid.NewGuid()
        );
        using var smallEnvelope = new HandlerFixture(
            chat,
            bounds: new NlSearchOptions { MaxInputTokens = 2000 }
        );

        (await smallEnvelope.InvokeAsync(request)).Fragments.ShouldBeNull();

        chat.Calls.ShouldBe(0);
        smallEnvelope.Ledger.LastEntry.ShouldBeNull();
    }

    [Theory]
    [InlineData(false, "Testing", true)]
    [InlineData(true, "Development", true)]
    [InlineData(true, "Testing", false)]
    public async Task Unavailable_preview_returns_typed_failure_without_legacy_inference(
        bool enabled,
        string environment,
        bool registerPreviewChat
    )
    {
        using var chat = new FictionalChatClient(() => Task.FromResult(Response(PartialReply)));
        using var fixture = new HandlerFixture(
            registerPreviewChat ? chat : null,
            enabled,
            environment,
            new NlSearchOptions { RequestsPerMinute = 1 }
        );

        var parsed = await fixture.InvokeAsync();

        parsed.Fragments.ShouldBeNull();
        chat.Calls.ShouldBe(0);
        fixture.LegacyChat.Calls.ShouldBe(0);
        fixture.Ledger.LastEntry.ShouldBeNull();
        using var nextRequest = fixture.Admission.TryEnter(0.01m);
        nextRequest.ShouldNotBeNull();
    }

    [Fact]
    public async Task Noncooperative_response_hits_inference_deadline_without_retry()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completion = new TaskCompletionSource<ChatResponse>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        using var chat = new FictionalChatClient(() =>
        {
            started.SetResult();
            return completion.Task;
        });
        using var fixture = new HandlerFixture(chat);
        var pending = fixture.InvokeAsync();

        try
        {
            var ct = TestContext.Current.CancellationToken;
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
            fixture.Time.Advance(TimeSpan.FromMilliseconds(3500));
            (await pending.WaitAsync(TimeSpan.FromSeconds(5), ct)).Fragments.ShouldBeNull();
            chat.Calls.ShouldBe(1);
            fixture.LegacyChat.Calls.ShouldBe(0);
            fixture.Ledger.LastEntry.ShouldNotBeNull().Outcome.ShouldBe("DeadlineExceeded");
            fixture.Admission.TryEnter(0.01m).ShouldBeNull();
        }
        finally
        {
            completion.TrySetResult(Response(PartialReply));
        }
    }

    private static ChatResponse Response(string body) =>
        new(new ChatMessage(ChatRole.Assistant, body))
        {
            ModelId = NlSearchOptions.ModelId,
            Usage = new() { InputTokenCount = 100, OutputTokenCount = 50 },
        };

    private sealed class HandlerFixture : IDisposable
    {
        private readonly ServiceProvider services;

        public HandlerFixture(
            IChatClient? previewChat,
            bool enabled = true,
            string environment = "Testing",
            NlSearchOptions? bounds = null
        )
        {
            var registrations = new ServiceCollection()
                .AddSingleton<IConfiguration>(
                    new ConfigurationBuilder()
                        .AddInMemoryCollection(
                            new Dictionary<string, string?>
                            {
                                ["Flights:NlSearchPreview:Enabled"] = enabled.ToString(),
                            }
                        )
                        .Build()
                )
                .AddSingleton<IHostEnvironment>(
                    new HostingEnvironment { EnvironmentName = environment }
                )
                .AddSingleton<IChatClient>(LegacyChat)
                .AddScoped(_ => new AiDbContext(
                    new DbContextOptionsBuilder<AiDbContext>()
                        .UseNpgsql("Host=unreachable.invalid;Database=fictional")
                        .UseSnakeCaseNamingConvention()
                        .AddInterceptors(Ledger)
                        .Options
                ));
            if (previewChat is not null)
                registrations.AddKeyedSingleton(
                    FlightSearchPreviewMessageIdentity.ChatClientKey,
                    previewChat
                );
            services = registrations.BuildServiceProvider();
            Admission = new(bounds ?? new NlSearchOptions(), Time);
        }

        public FakeTimeProvider Time { get; } = new(new(2026, 11, 24, 1, 0, 0, TimeSpan.Zero));
        public NlSearchAdmission Admission { get; }
        public FictionalChatClient LegacyChat { get; } =
            new(() => Task.FromResult(Response(PartialReply)));
        public CaptureLedger Ledger { get; } = new();

        public Task<FlightSearchPreviewParsed> InvokeAsync(
            FlightSearchPreviewRequested? request = null
        ) =>
            FlightSearchPreviewAiHandler.Handle(
                request ?? new(PartialQuery, new DateOnly(2026, 11, 23), Guid.NewGuid()),
                services.GetRequiredService<IServiceScopeFactory>(),
                Time,
                new AiMetrics(new TestMeterFactory()),
                Admission,
                TestContext.Current.CancellationToken
            );

        public void Dispose()
        {
            LegacyChat.Dispose();
            services.Dispose();
        }
    }

    private sealed class CaptureLedger : SaveChangesInterceptor
    {
        public List<CostLedgerEntry> Entries { get; } = [];
        public CostLedgerEntry? LastEntry => Entries.LastOrDefault();

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        )
        {
            var entry = eventData.Context!.ChangeTracker.Entries<CostLedgerEntry>().Single();
            Entries.Add((CostLedgerEntry)entry.CurrentValues.ToObject());
            return ValueTask.FromResult(InterceptionResult<int>.SuppressWithResult(1));
        }
    }

    private sealed class FictionalChatClient(Func<Task<ChatResponse>> respond) : IChatClient
    {
        public int Calls { get; private set; }
        public ChatOptions? Options { get; private set; }
        public ChatMessage[] Messages { get; private set; } = [];

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default
        )
        {
            Calls++;
            Options = options;
            Messages = messages.ToArray();
            return respond();
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
