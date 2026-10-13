using ErrorOr;
using Shouldly;
using Travel.IntegrationContracts.Flights;
using Travel.Modules.Support.Application;
using Travel.Modules.Support.Core;

namespace Travel.Modules.Support.Tests.Unit;

public sealed class SupportClassificationServiceTests
{
    [Fact]
    public async Task Disabled_does_not_decrypt_infer_read_order_or_change_case()
    {
        var f = await Fixture.Create(false);
        var before = f.Context.Store.Cases[f.Context.CaseId];
        var writes = f.Context.Store.WriteAttempts;
        var result = await f.Suggest();
        result.IsError.ShouldBeFalse();
        result.Value.Status.ShouldBe(SupportClassificationStatus.Disabled);
        f.Context.Protector.UnprotectCalls.ShouldBe(0);
        f.Provider.Calls.ShouldBe(0);
        f.Orders.Calls.ShouldBe(0);
        f.Context.Store.WriteAttempts.ShouldBe(writes);
        f.Context.Store.Cases[f.Context.CaseId].ShouldBe(before);
    }

    [Theory]
    [InlineData("anonymous")]
    [InlineData("customer")]
    [InlineData("wrong-client")]
    [InlineData("no-permission")]
    public async Task Access_is_checked_before_private_processing(string kind)
    {
        var f = await Fixture.Create();
        var caller = kind switch
        {
            "anonymous" => f.Context.Operator with { IsAuthenticated = false },
            "customer" => f.Context.Customer,
            "wrong-client" => f.Context.Operator with { AuthorizedParty = "travel-web" },
            _ => f.Context.Operator with { HasOperatorPermission = false },
        };
        f.Context.Store.FailReads = true;
        var result = await f.Service.SuggestAsync(
            caller,
            f.Request,
            TestContext.Current.CancellationToken
        );
        result.IsError.ShouldBeTrue();
        result.FirstError.Code.ShouldBe(
            kind == "anonymous" ? "Support.Unauthenticated" : "Support.Forbidden"
        );
        f.Context.Protector.UnprotectCalls.ShouldBe(0);
        f.Provider.Calls.ShouldBe(0);
        f.Orders.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task Candidate_contains_only_complete_bound_text_and_never_saves_triage()
    {
        var f = await Fixture.Create();
        var before = f.Context.Store.Cases[f.Context.CaseId];
        var writes = f.Context.Store.WriteAttempts;
        var result = await f.Suggest();
        result.IsError.ShouldBeFalse();
        result.Value.Status.ShouldBe(SupportClassificationStatus.Candidate);
        result.Value.Category.ShouldBe(ManualCategory.Payment);
        result.Value.CapturedRevision.ShouldBe(1);
        result.Value.RequestId.ShouldBe(f.Request.RequestId);
        (result.Value.ExpiresAt - result.Value.GeneratedAt).ShouldBe(TimeSpan.FromMinutes(2));
        f.Provider.Input!.SerializedState.ShouldContain("A literal first message");
        f.Provider.Input.SerializedState.ShouldNotContain(f.Context.Customer.Subject.ToString());
        f.Provider.Input.SerializedState.ShouldNotContain(f.Context.CaseId.ToString());
        f.Provider.Input.ToString().ShouldNotContain("first message");
        f.Provider.Calls.ShouldBe(1);
        f.Context.Store.Cases[f.Context.CaseId].ShouldBe(before);
        f.Context.Store.WriteAttempts.ShouldBe(writes);
    }

    [Fact]
    public async Task Fifth_message_prevents_any_decryption_or_partial_inference()
    {
        var f = await Fixture.Create();
        for (var i = 0; i < 4; i++)
            (
                await f.Context.ReplyAsync(
                    f.Context.Customer,
                    i + 1,
                    SupportActionKind.CustomerReply
                )
            ).IsError.ShouldBeFalse();
        var result = await f.Service.SuggestAsync(
            f.Context.Operator,
            f.Request with
            {
                ExpectedRevision = 5,
            },
            TestContext.Current.CancellationToken
        );
        result.IsError.ShouldBeFalse();
        result.Value.Status.ShouldBe(SupportClassificationStatus.ManualRequired);
        result.Value.Reason.ShouldBe(SupportClassificationReason.InputTooLarge);
        f.Context.Protector.UnprotectCalls.ShouldBe(0);
        f.Provider.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task Revision_conflict_and_unreadable_text_never_reach_provider()
    {
        var f = await Fixture.Create();
        (
            await f.Service.SuggestAsync(
                f.Context.Operator,
                f.Request with
                {
                    ExpectedRevision = 2,
                },
                TestContext.Current.CancellationToken
            )
        ).FirstError.Code.ShouldBe("Support.Conflict");
        f.Context.Protector.UnprotectCalls.ShouldBe(0);
        f.Context.Protector.Unavailable = true;
        (await f.Suggest()).FirstError.Code.ShouldBe("Support.Unavailable");
        f.Provider.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task Concurrent_manual_resolution_discards_model_candidate()
    {
        var f = await Fixture.Create();
        f.Provider.Before = async () =>
        {
            (
                await f.Context.Service.ActAsync(
                    f.Context.Operator,
                    new(f.Context.CaseId, Guid.NewGuid(), 1, SupportActionKind.Resolve),
                    TestContext.Current.CancellationToken
                )
            ).IsError.ShouldBeFalse();
        };
        (await f.Suggest()).FirstError.Code.ShouldBe("Support.Conflict");
        f.Context.Store.Cases[f.Context.CaseId].State.ShouldBe(CaseState.Resolved);
        f.Context.Store.Cases[f.Context.CaseId].Category.ShouldBe(ManualCategory.Unclassified);
    }

    [Fact]
    public async Task Expired_case_is_concealed_before_decryption_or_inference()
    {
        var f = await Fixture.Create();
        var current = f.Context.Store.Cases[f.Context.CaseId];
        f.Context.Store.Cases[f.Context.CaseId] = current with
        {
            State = CaseState.Resolved,
            ResolvedAt = f.Context.Clock.GetUtcNow().AddDays(-90),
            PurgeAt = f.Context.Clock.GetUtcNow(),
        };
        (await f.Suggest()).FirstError.Code.ShouldBe("Support.NotFound");
        f.Context.Protector.UnprotectCalls.ShouldBe(0);
        f.Provider.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task Revision_changed_during_decryption_is_rejected_before_inference()
    {
        var f = await Fixture.Create();
        f.Context.Protector.BeforeUnprotect = () =>
        {
            var row = f.Context.Store.Cases[f.Context.CaseId];
            f.Context.Store.Cases[f.Context.CaseId] = row with { Revision = 2 };
        };
        (await f.Suggest()).FirstError.Code.ShouldBe("Support.Conflict");
        f.Provider.Calls.ShouldBe(0);
        f.Orders.Calls.ShouldBe(0);
        f.Context.Store.Cases[f.Context.CaseId].Category.ShouldBe(ManualCategory.Unclassified);
    }

    [Fact]
    public async Task Provider_timeout_is_uncertain_and_never_replayed_or_saved()
    {
        var f = await Fixture.Create();
        f.Provider.Before = () =>
        {
            f.Context.Clock.Advance(TimeSpan.FromSeconds(11));
            return Task.CompletedTask;
        };
        var result = await f.Suggest();
        result.IsError.ShouldBeFalse();
        result.Value.Status.ShouldBe(SupportClassificationStatus.ManualRequired);
        result.Value.Reason.ShouldBe(SupportClassificationReason.Uncertain);
        f.Provider.Calls.ShouldBe(1);
        f.Orders.Calls.ShouldBe(0);
        f.Context.Store.Cases[f.Context.CaseId].Category.ShouldBe(ManualCategory.Unclassified);
    }

    [Theory]
    [InlineData("foreign")]
    [InlineData("stale")]
    [InlineData("missing")]
    public async Task Unusable_order_context_never_becomes_normal_or_high(string failure)
    {
        var f = await Fixture.Create();
        var row = f.Context.Store.Cases[f.Context.CaseId];
        f.Context.Store.Cases[f.Context.CaseId] = row with
        {
            OrderLink = new(f.Context.OrderId, 7, f.Context.Clock.GetUtcNow()),
        };
        f.Orders.Failure = failure;
        var result = await f.Suggest();
        result.IsError.ShouldBeFalse();
        result.Value.Urgency.Kind.ShouldBe(SupportUrgencyKind.Unknown);
        f.Context.Store.Cases[f.Context.CaseId].Priority.ShouldBe(SupportPriority.Normal);
    }

    [Fact]
    public async Task Fresh_order_urgency_is_independent_of_model_abstention_and_never_saved()
    {
        var f = await Fixture.Create();
        var row = f.Context.Store.Cases[f.Context.CaseId];
        f.Context.Store.Cases[f.Context.CaseId] = row with
        {
            OrderLink = new(f.Context.OrderId, 7, f.Context.Clock.GetUtcNow()),
        };
        f.Provider.Result = new(null, SupportClassificationReason.LowConfidence);
        var result = await f.Suggest();
        result.IsError.ShouldBeFalse();
        result.Value.Status.ShouldBe(SupportClassificationStatus.ManualRequired);
        result.Value.Category.ShouldBeNull();
        result.Value.Urgency.Kind.ShouldBe(SupportUrgencyKind.High);
        f.Context.Store.Cases[f.Context.CaseId].Priority.ShouldBe(SupportPriority.Normal);
    }

    [Fact]
    public void Admission_is_nonqueued_bounded_and_consumes_attempts_after_release()
    {
        var clock = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(
            new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero)
        );
        var admission = new SupportClassificationAdmission(clock);
        var actor = Guid.NewGuid();
        var id = Guid.NewGuid();
        using var first = admission.TryAcquire(actor, id, 1);
        first.ShouldNotBeNull();
        admission.TryAcquire(actor, id, 1).ShouldBeNull();
        using var second = admission.TryAcquire(actor, Guid.NewGuid(), 1);
        second.ShouldNotBeNull();
        admission.TryAcquire(Guid.NewGuid(), Guid.NewGuid(), 1).ShouldBeNull();
        first!.Dispose();
        using var third = admission.TryAcquire(actor, Guid.NewGuid(), 1);
        third.ShouldNotBeNull();
        third!.Dispose();
        admission.TryAcquire(actor, Guid.NewGuid(), 1).ShouldBeNull();
        second!.Dispose();
        clock.Advance(TimeSpan.FromMinutes(1));
        admission.TryAcquire(actor, Guid.NewGuid(), 1).ShouldNotBeNull().Dispose();
    }

    [Fact]
    public void Process_budget_cannot_be_bypassed_by_rotating_operator_identity()
    {
        var clock = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(
            new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero)
        );
        var admission = new SupportClassificationAdmission(clock);
        for (var i = 0; i < 12; i++)
            admission.TryAcquire(Guid.NewGuid(), Guid.NewGuid(), 1).ShouldNotBeNull().Dispose();
        admission.TryAcquire(Guid.NewGuid(), Guid.NewGuid(), 1).ShouldBeNull();
        clock.Advance(TimeSpan.FromSeconds(61));
        admission.TryAcquire(Guid.NewGuid(), Guid.NewGuid(), 1).ShouldNotBeNull().Dispose();
    }

    [Fact]
    public async Task Profile_disabled_during_later_order_read_cannot_disclose_ready_category()
    {
        var f = await Fixture.Create();
        var row = f.Context.Store.Cases[f.Context.CaseId];
        f.Context.Store.Cases[f.Context.CaseId] = row with
        {
            OrderLink = new(f.Context.OrderId, 7, f.Context.Clock.GetUtcNow()),
        };
        f.Orders.Before = () => f.Provider.IsEnabled = false;
        var result = await f.Suggest();
        result.IsError.ShouldBeFalse();
        result.Value.Status.ShouldBe(SupportClassificationStatus.ManualRequired);
        result.Value.Reason.ShouldBe(SupportClassificationReason.IdentityMismatch);
        result.Value.Category.ShouldBeNull();
        f.Context.Store.Cases[f.Context.CaseId].Category.ShouldBe(ManualCategory.Unclassified);
    }

    [Fact]
    public async Task Complete_conversation_over_scalar_budget_is_not_silently_shortened()
    {
        var f = await Fixture.Create();
        for (var revision = 1; revision <= 2; revision++)
            (
                await f.Context.Service.ReplyAsync(
                    f.Context.Customer,
                    new(
                        f.Context.CaseId,
                        Guid.NewGuid(),
                        new string('a', 4000),
                        revision,
                        SupportActionKind.CustomerReply
                    ),
                    TestContext.Current.CancellationToken
                )
            ).IsError.ShouldBeFalse();
        var result = await f.Service.SuggestAsync(
            f.Context.Operator,
            f.Request with
            {
                ExpectedRevision = 3,
            },
            TestContext.Current.CancellationToken
        );
        result.IsError.ShouldBeFalse();
        result.Value.Reason.ShouldBe(SupportClassificationReason.InputTooLarge);
        f.Provider.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task Invalid_stored_actor_or_future_written_revision_fails_closed_before_decryption()
    {
        var f = await Fixture.Create();
        var key = (f.Context.CaseId, f.Context.FirstMessageId);
        var message = f.Context.Store.Messages[key];
        f.Context.Store.Messages[key] = message with { ActorKind = (SupportActorKind)99 };
        (await f.Suggest()).FirstError.Code.ShouldBe("Support.Unavailable");
        f.Context.Protector.UnprotectCalls.ShouldBe(0);
        f.Provider.Calls.ShouldBe(0);
        f.Context.Store.Messages[key] = message with { WrittenAtRevision = 2 };
        (await f.Suggest()).FirstError.Code.ShouldBe("Support.Unavailable");
        f.Context.Protector.UnprotectCalls.ShouldBe(0);
    }

    [Fact]
    public async Task Classification_metrics_observe_outcomes_without_private_or_identifying_labels()
    {
        var observed = new System.Collections.Concurrent.ConcurrentBag<KeyValuePair<
            string,
            object?
        >[]>();
        using var listener = new System.Diagnostics.Metrics.MeterListener();
        listener.InstrumentPublished = (instrument, owner) =>
        {
            if (
                instrument.Meter.Name == "Travel.Support.Classification"
                && instrument.Name == "support.classification.outcomes"
            )
                owner.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) => observed.Add(tags.ToArray()));
        listener.Start();
        var f = await Fixture.Create();
        (await f.Suggest()).IsError.ShouldBeFalse();
        observed.ShouldNotBeEmpty();
        observed.ShouldAllBe(tags =>
            tags.All(tag => new[] { "status", "reason", "category", "profile" }.Contains(tag.Key))
        );
        observed
            .SelectMany(tags => tags)
            .Any(tag =>
                tag.Value?.ToString() == f.Context.CaseId.ToString()
                || tag.Value?.ToString() == f.Context.Customer.Subject.ToString()
                || tag.Value?.ToString()
                    ?.Contains("literal first message", StringComparison.Ordinal) == true
            )
            .ShouldBeFalse();
    }

    [Fact]
    public async Task Profile_retired_before_provider_entry_is_manual_not_disabled_with_active_urgency()
    {
        var f = await Fixture.Create();
        var row = f.Context.Store.Cases[f.Context.CaseId];
        f.Context.Store.Cases[f.Context.CaseId] = row with
        {
            OrderLink = new(f.Context.OrderId, 7, f.Context.Clock.GetUtcNow()),
        };
        f.Provider.Before = () =>
        {
            f.Provider.IsEnabled = false;
            f.Provider.Result = new(null, SupportClassificationReason.Disabled);
            return Task.CompletedTask;
        };
        var result = await f.Suggest();
        result.IsError.ShouldBeFalse();
        result.Value.Status.ShouldBe(SupportClassificationStatus.ManualRequired);
        result.Value.Reason.ShouldBe(SupportClassificationReason.IdentityMismatch);
        result.Value.Category.ShouldBeNull();
    }

    [Fact]
    public async Task Departure_crossed_during_final_case_read_does_not_leave_a_stale_high_recommendation()
    {
        var f = await Fixture.Create();
        var row = f.Context.Store.Cases[f.Context.CaseId];
        f.Context.Store.Cases[f.Context.CaseId] = row with
        {
            OrderLink = new(f.Context.OrderId, 7, f.Context.Clock.GetUtcNow()),
        };
        f.Orders.UntilDeparture = TimeSpan.FromSeconds(2);
        var reads = 0;
        f.Context.Store.AfterCaseRead = () =>
        {
            if (++reads == 3)
                f.Context.Clock.Advance(TimeSpan.FromSeconds(3));
        };
        var result = await f.Suggest();
        result.IsError.ShouldBeFalse();
        result.Value.Status.ShouldBe(SupportClassificationStatus.Candidate);
        result.Value.Urgency.Kind.ShouldBe(SupportUrgencyKind.Unknown);
        result.Value.Urgency.NextScheduledDepartureAt.ShouldBeNull();
        f.Context.Store.Cases[f.Context.CaseId].Priority.ShouldBe(SupportPriority.Normal);
    }

    private sealed class Fixture
    {
        public SupportTestContext Context { get; } = new();
        public Provider Provider { get; } = new();
        public Orders Orders { get; }
        public SupportClassificationService Service { get; }
        public RequestSupportClassification Request { get; }

        private Fixture(bool enabled)
        {
            Provider.IsEnabled = enabled;
            Orders = new(Context.Clock);
            Service = new(
                Context.Store,
                Context.Protector,
                Provider,
                Orders,
                new(Context.Clock),
                Context.Clock
            );
            Request = new(Context.CaseId, Guid.NewGuid(), 1);
        }

        public static async Task<Fixture> Create(bool enabled = true)
        {
            var f = new Fixture(enabled);
            (await f.Context.CreateAsync()).IsError.ShouldBeFalse();
            return f;
        }

        public Task<ErrorOr<SupportClassificationResult>> Suggest() =>
            Service.SuggestAsync(Context.Operator, Request, TestContext.Current.CancellationToken);
    }

    private sealed class Provider : ISupportClassificationProvider
    {
        public bool IsEnabled { get; set; } = true;
        public int Calls { get; private set; }
        public SupportClassificationInput? Input { get; private set; }
        public Func<Task>? Before { get; set; }
        public SupportProviderClassification Result { get; set; } =
            new(ManualCategory.Payment, SupportClassificationReason.Ready, new string('a', 64), 71);

        public async Task<ErrorOr<SupportProviderClassification>> ClassifyAsync(
            SupportClassificationInput input,
            CancellationToken ct
        )
        {
            Calls++;
            Input = input;
            if (Before is not null)
                await Before();
            return Result;
        }
    }

    private sealed class Orders(TimeProvider clock) : IFlightsSupportTriageContext
    {
        public int Calls { get; private set; }
        public string? Failure { get; set; }
        public Action? Before { get; set; }
        public TimeSpan? UntilDeparture { get; set; }

        public Task<ErrorOr<FlightSupportTriageFacts>> ReadAsync(
            Guid ownerId,
            Guid orderId,
            CancellationToken cancellationToken = default
        )
        {
            Calls++;
            Before?.Invoke();
            return Task.FromResult<ErrorOr<FlightSupportTriageFacts>>(
                Failure == "missing"
                    ? Error.Failure("Flights.OrderAccessUnavailable", "Unavailable")
                    : new FlightSupportTriageFacts(
                        orderId,
                        Failure == "foreign" ? Guid.NewGuid() : ownerId,
                        8,
                        Failure == "stale" ? clock.GetUtcNow().AddSeconds(-31) : clock.GetUtcNow(),
                        FlightSupportJourneyState.Active,
                        clock.GetUtcNow().Add(UntilDeparture ?? TimeSpan.FromHours(2))
                    )
            );
        }
    }
}
