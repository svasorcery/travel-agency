using System.Text;
using System.Text.Json;
using ErrorOr;
using Shouldly;
using Travel.Modules.Trips.Application;
using Travel.Modules.Trips.Application.Itineraries;
using Travel.Modules.Trips.Core;
using Travel.Modules.Trips.Core.Itineraries;
using Xunit;

namespace Travel.Modules.Trips.Tests.Unit;

// FICTIONAL structural facts only. No real source package, Q cases, database, network or model.
public sealed class ItineraryCandidateServiceTests
{
    [Fact]
    public async Task Explicit_v2_supports_saved_petersburg_and_legacy_stays_source_free()
    {
        var f = new Fixture();
        f.SetPlan("saint-petersburg");
        f.Source.Facts = ItineraryTestFacts
            .Input("saint-petersburg", "spb-outdoor-profile-v1")
            .Build()
            .Value;
        (await f.Generate()).IsError.ShouldBeFalse();
        f.Encoder.Seen.ShouldBeOfType<ScopeUnsupportedCandidateResult>();
        f.Source.Calls.ShouldBe(0);
        var result = await f.Service.GenerateAsync(
            f.Plan.Owner,
            f.Plan.TripId,
            1,
            TestContext.Current.CancellationToken,
            candidateVersion: 2
        );
        result.IsError.ShouldBeFalse();
        var computed = f.Encoder.Seen.ShouldBeOfType<ComputedCandidateResult>();
        computed.CandidateVersion.ShouldBe(2);
        computed.Facts.Destination.ShouldBe(f.Plan.Destination);
        f.Source.Calls.ShouldBe(1);
        f.AssertNoWrites();
    }

    [Fact]
    public async Task Invalid_protocol_and_v2_unsupported_city_never_consult_source()
    {
        var f = new Fixture();
        var invalid = await f.Service.GenerateAsync(
            f.Plan.Owner,
            f.Plan.TripId,
            1,
            TestContext.Current.CancellationToken,
            candidateVersion: 3
        );
        invalid.IsError.ShouldBeTrue();
        invalid.FirstError.Code.ShouldBe("Trips.InvalidInput");
        f.Store.Reads.ShouldBe(0);
        f.SetPlan("kaliningrad");
        (
            await f.Service.GenerateAsync(
                f.Plan.Owner,
                f.Plan.TripId,
                1,
                TestContext.Current.CancellationToken,
                candidateVersion: 2
            )
        ).IsError.ShouldBeFalse();
        f.Encoder.Seen.ShouldBeOfType<ScopeUnsupportedCandidateResult>()
            .CandidateVersion.ShouldBe(2);
        f.Source.Calls.ShouldBe(0);
        f.AssertNoWrites();
    }

    [Fact]
    public async Task Owner_and_revision_guards_precede_private_reads_and_source_processing()
    {
        var f = new Fixture();
        (await f.Generate(owner: null)).FirstError.Code.ShouldBe("Trips.Forbidden");
        (await f.Generate(revision: 0)).FirstError.Code.ShouldBe("Trips.InvalidInput");
        f.Store.Reads.ShouldBe(0);
        f.Store.ReturnForeign = true;
        (await f.Generate()).FirstError.Code.ShouldBe("Trips.NotFound");
        f.Protector.Reads.ShouldBe(0);
        f.Source.Calls.ShouldBe(0);
        f.Encoder.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task Stale_revision_and_saved_nonempty_calendar_fail_before_source()
    {
        var f = new Fixture();
        (await f.Generate(revision: 2)).FirstError.Code.ShouldBe("Trips.RevisionMismatch");
        f.SetPlan(block: true);
        (await f.Generate()).FirstError.Code.ShouldBe("Trips.ItineraryRequiresEmptyPlan");
        f.Source.Calls.ShouldBe(0);
        f.Encoder.Calls.ShouldBe(0);
        f.AssertNoWrites();
    }

    [Theory]
    [InlineData("saint-petersburg", 1, 0, "destinationUnsupported")]
    [InlineData("moscow", 4, 0, "dateRangeUnsupported")]
    [InlineData("moscow", 14, 0, "dateRangeUnsupported")]
    [InlineData("moscow", 1, -1, "historicalDatesUnsupported")]
    [InlineData("moscow", 1, 31, "horizonUnsupported")]
    [InlineData("moscow", 2, 30, "horizonUnsupported")]
    public async Task Unsupported_scope_is_source_free_but_encoded_and_owner_rechecked(
        string destination,
        int days,
        int offset,
        string reason
    )
    {
        var f = new Fixture();
        f.SetPlan(destination, days, offset);
        var response = await f.Generate();
        response.IsError.ShouldBeFalse();
        var result = f.Encoder.Seen.ShouldBeOfType<ScopeUnsupportedCandidateResult>();
        result.Reason.ShouldBe(reason);
        result.BaseRevision.ShouldBe(1);
        result.Destination.ShouldBe(f.Plan.Destination);
        result.AsOfUtc.ShouldBe(ItineraryTestFacts.Now);
        result.PolicyVersion.ShouldBe("bounded-beam-v1");
        f.Source.Calls.ShouldBe(0);
        f.Store.Reads.ShouldBe(2);
        f.Codec.ReservedPlan.ShouldBeNull();
        f.AssertNoWrites();
    }

    [Fact]
    public async Task Current_saved_zone_date_controls_scope_instead_of_server_utc_date()
    {
        var f = new Fixture();
        f.Clock.Now = ItineraryTestFacts.Now.AddHours(22); // Moscow already next day.
        (await f.Generate()).IsError.ShouldBeFalse();
        f.Encoder.Seen.ShouldBeOfType<ScopeUnsupportedCandidateResult>()
            .Reason.ShouldBe("historicalDatesUnsupported");
        f.Source.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task Supported_candidate_is_validated_mapped_to_ordinary_intentions_and_never_written()
    {
        var f = new Fixture();
        var original = f.Plan;
        (await f.Generate()).IsError.ShouldBeFalse();
        var computed = f.Encoder.Seen.ShouldBeOfType<ComputedCandidateResult>();
        computed.BaseRevision.ShouldBe(1);
        computed.Outcome.Kind.ShouldBe(ItineraryOutcomeKind.Candidate);
        computed.Outcome.GeneratedAt.ShouldBe(ItineraryTestFacts.Now);
        computed.Facts.ShouldBeSameAs(f.Source.Facts);
        var projected = f.Codec.ReservedPlan.ShouldNotBeNull();
        projected.TripId.ShouldBe(original.TripId);
        projected.Owner.ShouldBe(original.Owner);
        projected.Revision.ShouldBe(original.Revision);
        projected.Title.ShouldBe(original.Title);
        JsonSerializer
            .Serialize(projected.Brief.ToInput())
            .ShouldBe(JsonSerializer.Serialize(original.Brief.ToInput()));
        projected.Destination.ShouldBe(original.Destination);
        projected.CreatedAt.ShouldBe(original.CreatedAt);
        projected.UpdatedAt.ShouldBe(original.UpdatedAt);
        projected.Days.Select(d => d.Date).ShouldBe(original.Days.Select(d => d.Date));
        projected.Days.SelectMany(d => d.Blocks).Count().ShouldBeGreaterThan(0);
        foreach (var block in projected.Days.SelectMany(d => d.Blocks))
        {
            block.StartTime.ShouldBeNull();
            block.EndTime.ShouldBeNull();
            block.Estimate.ShouldBeNull();
            var source = computed
                .Outcome.Days.SelectMany(d => d.Blocks)
                .Single(b => b.ProposedBlockId == block.BlockId);
            block.Note.ShouldBe(source.Note);
            block.Title.ShouldBe(source.Title);
        }
        f.Codec.ReserveBytes.ShouldBeGreaterThan(0);
        f.AssertNoWrites();
    }

    [Fact]
    public async Task Empty_remains_computed_with_basis_and_empty_ordinary_days()
    {
        var f = new Fixture();
        f.Source.Facts = EmptyFacts();
        (await f.Generate()).IsError.ShouldBeFalse();
        f.Encoder.Seen.ShouldBeOfType<ComputedCandidateResult>()
            .Outcome.Kind.ShouldBe(ItineraryOutcomeKind.Empty);
        f.Codec.ReservedPlan.ShouldNotBeNull().Days.SelectMany(d => d.Blocks).ShouldBeEmpty();
        f.AssertNoWrites();
    }

    [Theory]
    [InlineData("error")]
    [InlineData("throw")]
    [InlineData("binding")]
    [InlineData("expired")]
    [InlineData("future")]
    public async Task Unready_or_wrong_source_is_unavailable_without_encoding_or_retry(string mode)
    {
        var f = new Fixture();
        if (mode == "binding")
            f.Source.Facts = (
                ItineraryTestFacts.Input() with
                {
                    Destination = CityCatalogue.Find("saint-petersburg").Value,
                }
            )
                .Build()
                .Value;
        if (mode == "expired")
            f.Clock.Now = ItineraryTestFacts.Now.AddDays(21);
        if (mode == "future")
            f.Clock.Now = ItineraryTestFacts.Now.AddMinutes(-1);
        if (mode is "expired" or "future")
            f.SetPlan(offset: mode == "expired" ? 21 : 0);
        f.Source.Error = mode == "error" ? TripErrors.InvalidInput : null;
        f.Source.Throws = mode == "throw";
        (await f.Generate()).FirstError.Code.ShouldBe("Trips.ItineraryUnavailable");
        f.Source.Calls.ShouldBe(1);
        f.Encoder.Calls.ShouldBe(0);
        f.AssertNoWrites();
    }

    [Theory]
    [InlineData("error")]
    [InlineData("throw")]
    [InlineData("empty")]
    [InlineData("oversize")]
    [InlineData("invalidUtf8")]
    public async Task Encoder_failures_invalid_utf8_and_complete_byte_overflow_fail_without_truncation(
        string mode
    )
    {
        var f = new Fixture();
        f.Encoder.Handler = (_, _) =>
            mode switch
            {
                "error" => Task.FromResult<ErrorOr<ReadOnlyMemory<byte>>>(TripErrors.InvalidInput),
                "throw" => throw new InvalidOperationException("private error must not escape"),
                "empty" => Bytes([]),
                "oversize" => Bytes(Encoding.UTF8.GetBytes(new string('я', 65537))),
                _ => Bytes([0xff]),
            };
        (await f.Generate()).FirstError.Code.ShouldBe("Trips.ItineraryUnavailable");
        f.Encoder.Calls.ShouldBe(1);
        f.Store.Reads.ShouldBe(1);
        f.AssertNoWrites();
    }

    [Fact]
    public async Task Exact_multibyte_byte_limit_is_accepted_and_not_reserialized()
    {
        var f = new Fixture();
        var bytes = Encoding.UTF8.GetBytes("\"" + new string('я', 65535) + "\"");
        bytes.Length.ShouldBe(131072);
        f.Encoder.Handler = (_, _) => Bytes(bytes);
        (await f.Generate()).Value.Utf8Json.ToArray().ShouldBe(bytes);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(131073)]
    public async Task Complete_ordinary_M1_detail_reserve_failure_rejects_the_entire_preview(
        int size
    )
    {
        var f = new Fixture();
        f.Codec.ReservedSize = size;
        (await f.Generate()).FirstError.Code.ShouldBe("Trips.ItineraryUnavailable");
        f.Codec.ReservedPlan.ShouldNotBeNull();
        f.Store.Reads.ShouldBe(1);
        f.AssertNoWrites();
    }

    [Theory]
    [InlineData("update", "Trips.RevisionMismatch", true)]
    [InlineData("update", "Trips.RevisionMismatch", false)]
    [InlineData("delete", "Trips.NotFound", true)]
    [InlineData("delete", "Trips.NotFound", false)]
    [InlineData("foreign", "Trips.NotFound", true)]
    [InlineData("foreign", "Trips.NotFound", false)]
    public async Task Final_owner_read_rejects_races_during_encoding_and_immediately_before_final_read(
        string race,
        string error,
        bool duringEncoding
    )
    {
        var f = new Fixture();
        f.Encoder.Handler = (_, _) =>
        {
            if (duringEncoding)
                f.Race(race);
            return Bytes("{}"u8.ToArray());
        };
        if (!duringEncoding)
            f.Store.BeforeLoad = reads =>
            {
                if (reads == 2)
                    f.Race(race);
            };
        (await f.Generate()).FirstError.Code.ShouldBe(error);
        f.Store.Reads.ShouldBe(2);
        f.AssertNoWrites();
    }

    [Fact]
    public async Task Unsupported_success_also_requires_final_owner_revision_check()
    {
        var f = new Fixture();
        f.SetPlan("saint-petersburg");
        f.Store.BeforeLoad = reads =>
        {
            if (reads == 2)
                f.Race("update");
        };
        (await f.Generate()).FirstError.Code.ShouldBe("Trips.RevisionMismatch");
        f.Source.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task Producer_bytes_are_frozen_before_final_owner_read()
    {
        var f = new Fixture();
        var producer = "{\"a\":1}"u8.ToArray();
        f.Encoder.Handler = (_, _) => Bytes(producer);
        f.Store.BeforeLoad = reads =>
        {
            if (reads == 2)
                producer[5] = (byte)'9';
        };
        var result = await f.Generate();
        Encoding.UTF8.GetString(result.Value.Utf8Json.Span).ShouldBe("{\"a\":1}");
        producer[5].ShouldBe((byte)'9');
        f.AssertNoWrites();
    }

    [Fact]
    public async Task Computed_preview_expiry_is_rechecked_after_final_get()
    {
        var f = new Fixture();
        f.Store.BeforeLoad = reads =>
        {
            if (reads == 2)
                f.Clock.Now = ItineraryTestFacts.Now.AddMinutes(15);
        };
        (await f.Generate()).FirstError.Code.ShouldBe("Trips.ItineraryUnavailable");
        f.Store.Reads.ShouldBe(2);
    }

    [Fact]
    public async Task Shared_gate_admits_two_scoped_services_and_third_is_busy_then_recovers()
    {
        var gate = new ItineraryAdmissionGate();
        var f = new Fixture(gate);
        var other = new Fixture(gate);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var count = 0;
        Task<ErrorOr<ReadOnlyMemory<byte>>> Block(ItineraryCandidateResult _, CancellationToken ct)
        {
            if (Interlocked.Increment(ref count) == 2)
                entered.SetResult();
            return Finish(ct);
        }
        async Task<ErrorOr<ReadOnlyMemory<byte>>> Finish(CancellationToken ct)
        {
            await release.Task.WaitAsync(ct);
            return (ReadOnlyMemory<byte>)"{}"u8.ToArray();
        }
        f.Encoder.Handler = Block;
        other.Encoder.Handler = Block;
        var first = f.Generate();
        var second = other.Generate();
        await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        (await new Fixture(gate).Generate()).FirstError.Code.ShouldBe("Trips.ItineraryBusy");
        release.SetResult();
        (await first).IsError.ShouldBeFalse();
        (await second).IsError.ShouldBeFalse();
        (await new Fixture(gate).Generate()).IsError.ShouldBeFalse();
    }

    [Fact]
    public async Task Internal_deadline_cancels_encoder_and_releases_admission_only_after_completion()
    {
        var gate = new ItineraryAdmissionGate();
        var f = new Fixture(gate);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Encoder.Handler = async (_, ct) =>
        {
            entered.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return (ReadOnlyMemory<byte>)"{}"u8.ToArray();
        };
        var pending = f.Generate();
        await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        f.Clock.Advance(TimeSpan.FromSeconds(2));
        (await pending).FirstError.Code.ShouldBe("Trips.ItineraryDeadline");
        (await new Fixture(gate).Generate()).IsError.ShouldBeFalse();
        f.Encoder.Calls.ShouldBe(1);
        f.AssertNoWrites();
    }

    [Fact]
    public async Task Caller_cancellation_propagates_and_releases_admission_after_encoder_finishes()
    {
        var f = new Fixture();
        using var caller = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Encoder.Handler = async (_, ct) =>
        {
            entered.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return (ReadOnlyMemory<byte>)"{}"u8.ToArray();
        };
        var pending = f.Service.GenerateAsync(f.Plan.Owner, f.Plan.TripId, 1, caller.Token);
        await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        caller.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(async () => await pending);
        f.Encoder.Handler = (_, _) => Bytes("{}"u8.ToArray());
        (await f.Generate()).IsError.ShouldBeFalse();
        f.AssertNoWrites();
    }

    [Fact]
    public async Task Uncooperative_encoder_holds_lease_until_actual_finish_and_cannot_return_late_success()
    {
        var gate = new ItineraryAdmissionGate();
        var f = new Fixture(gate);
        var other = new Fixture(gate);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<ErrorOr<ReadOnlyMemory<byte>>> Ignore(
            ItineraryCandidateResult _,
            CancellationToken ct
        )
        {
            await release.Task;
            return (ReadOnlyMemory<byte>)"{}"u8.ToArray();
        }
        f.Encoder.Handler = Ignore;
        other.Encoder.Handler = Ignore;
        var first = f.Generate();
        var second = other.Generate();
        f.Clock.Advance(TimeSpan.FromSeconds(2));
        other.Clock.Advance(TimeSpan.FromSeconds(2));
        first.IsCompleted.ShouldBeFalse();
        second.IsCompleted.ShouldBeFalse();
        (await new Fixture(gate).Generate()).FirstError.Code.ShouldBe("Trips.ItineraryBusy");
        release.SetResult();
        (await first).FirstError.Code.ShouldBe("Trips.ItineraryDeadline");
        (await second).FirstError.Code.ShouldBe("Trips.ItineraryDeadline");
        (await new Fixture(gate).Generate()).IsError.ShouldBeFalse();
    }

    [Theory]
    [InlineData(true, "Trips.ItineraryDeadline")]
    [InlineData(false, "callerCancellation")]
    public async Task Cancellation_wins_over_late_non_cancellation_encoder_exception(
        bool deadline,
        string expected
    )
    {
        var f = new Fixture();
        using var caller = new CancellationTokenSource();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Encoder.Handler = async (_, _) =>
        {
            await release.Task;
            throw new InvalidOperationException("private late encoder error");
        };
        var pending = f.Service.GenerateAsync(f.Plan.Owner, f.Plan.TripId, 1, caller.Token);
        if (deadline)
            f.Clock.Advance(TimeSpan.FromSeconds(2));
        else
            caller.Cancel();
        pending.IsCompleted.ShouldBeFalse();
        release.SetResult();
        if (deadline)
            (await pending).FirstError.Code.ShouldBe(expected);
        else
            await Should.ThrowAsync<OperationCanceledException>(async () => await pending);
        f.Encoder.Handler = (_, _) => Bytes("{}"u8.ToArray());
        (await f.Generate()).IsError.ShouldBeFalse();
        f.AssertNoWrites();
    }

    [Theory]
    [InlineData("encoderError")]
    [InlineData("encoderThrow")]
    [InlineData("preflight")]
    [InlineData("expiry")]
    [InlineData("delete")]
    [InlineData("deadline")]
    [InlineData("caller")]
    public async Task Repeated_failure_and_cancellation_paths_release_exactly_one_slot(
        string failure
    )
    {
        var gate = new ItineraryAdmissionGate();
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var f = new Fixture(gate);
            using var caller = new CancellationTokenSource();
            if (failure == "preflight")
                f.Codec.ReservedSize = 131073;
            f.Encoder.Handler = (_, ct) =>
            {
                if (failure == "encoderThrow")
                    throw new InvalidOperationException();
                if (failure == "expiry")
                    f.Clock.Now = ItineraryTestFacts.Now.AddMinutes(15);
                if (failure == "delete")
                    f.Store.Row = null;
                if (failure == "deadline")
                    f.Clock.Advance(TimeSpan.FromSeconds(2));
                if (failure == "caller")
                    caller.Cancel();
                return failure == "encoderError"
                    ? Task.FromResult<ErrorOr<ReadOnlyMemory<byte>>>(TripErrors.InvalidInput)
                    : Bytes("{}"u8.ToArray());
            };
            var pending = f.Service.GenerateAsync(f.Plan.Owner, f.Plan.TripId, 1, caller.Token);
            if (failure == "caller")
                await Should.ThrowAsync<OperationCanceledException>(async () => await pending);
            else
                (await pending).FirstError.Code.ShouldBe(
                    failure switch
                    {
                        "delete" => "Trips.NotFound",
                        "deadline" => "Trips.ItineraryDeadline",
                        _ => "Trips.ItineraryUnavailable",
                    }
                );
            f.AssertNoWrites();
        }
        (await new Fixture(gate).Generate()).IsError.ShouldBeFalse();
    }

    [Fact]
    public async Task Unsupported_scope_and_source_failure_do_not_compete_for_computation_slots()
    {
        var gate = new ItineraryAdmissionGate();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = new Fixture(gate);
        var second = new Fixture(gate);
        async Task<ErrorOr<ReadOnlyMemory<byte>>> Block(
            ItineraryCandidateResult _,
            CancellationToken ct
        )
        {
            await release.Task.WaitAsync(ct);
            return (ReadOnlyMemory<byte>)"{}"u8.ToArray();
        }
        first.Encoder.Handler = Block;
        second.Encoder.Handler = Block;
        var a = first.Generate();
        var b = second.Generate();
        try
        {
            var unsupported = new Fixture(gate);
            unsupported.SetPlan("kaliningrad");
            (await unsupported.Generate()).IsError.ShouldBeFalse();
            unsupported.Source.Calls.ShouldBe(0);
            var badSource = new Fixture(gate);
            badSource.Source.Error = TripErrors.InvalidInput;
            (await badSource.Generate()).FirstError.Code.ShouldBe("Trips.ItineraryUnavailable");
            badSource.Source.Calls.ShouldBe(1);
            badSource.Encoder.Calls.ShouldBe(0);
            var third = new Fixture(gate);
            (await third.Generate()).FirstError.Code.ShouldBe("Trips.ItineraryBusy");
            third.Source.Calls.ShouldBe(1);
            third.Encoder.Calls.ShouldBe(0);
        }
        finally
        {
            release.TrySetResult();
        }
        (await a).IsError.ShouldBeFalse();
        (await b).IsError.ShouldBeFalse();
    }

    [Fact]
    public async Task Internal_policy_input_failure_becomes_unavailable_and_has_no_encoder_or_retry()
    {
        var f = new Fixture();
        // TimeProvider contract is UTC. A broken injected clock must not surface Core InvalidInput as user 400.
        f.Clock.Now = ItineraryTestFacts.Now.ToOffset(TimeSpan.FromHours(1));
        (await f.Generate()).FirstError.Code.ShouldBe("Trips.ItineraryUnavailable");
        f.Source.Calls.ShouldBe(1);
        f.Encoder.Calls.ShouldBe(0);
        f.AssertNoWrites();
    }

    private static ValidatedItineraryFacts EmptyFacts()
    {
        var input = ItineraryTestFacts.Input();
        var accessEvidence = input
            .Pois.Select(p =>
                ItineraryTestFacts
                    .EvidenceParameters("access-" + p.Id, ItineraryFactKind.Access, p.Id)
                    .Build()
                    .Value
            )
            .ToArray();
        var closed = input
            .Pois.Select(p =>
                ItineraryTestFacts.Poi(
                    p.Id,
                    access: ItineraryAccess.Create(p.Id, true, "access-" + p.Id, [], []).Value
                )
            )
            .ToArray();
        return (
            input with
            {
                Pois = closed,
                Evidence = input.Evidence.Concat(accessEvidence).ToArray(),
            }
        )
            .Build()
            .Value;
    }

    private static Task<ErrorOr<ReadOnlyMemory<byte>>> Bytes(byte[] bytes) =>
        Task.FromResult<ErrorOr<ReadOnlyMemory<byte>>>((ReadOnlyMemory<byte>)bytes);

    private sealed class Fixture
    {
        internal TripPlan Plan { get; private set; } = null!;
        internal Store Store { get; } = new();
        internal Protector Protector { get; } = new();
        internal Codec Codec { get; } = new();
        internal Source Source { get; } = new();
        internal Encoder Encoder { get; } = new();
        internal Clock Clock { get; } = new();
        internal ItineraryCandidateService Service { get; }

        internal Fixture(ItineraryAdmissionGate? gate = null)
        {
            SetPlan();
            var plans = new TripPlanService(Store, Protector, Codec, new Cursor(), Clock);
            Service = new(plans, Codec, Source, Encoder, gate ?? new(), Clock);
        }

        internal void SetPlan(
            string destination = "moscow",
            int days = 1,
            int offset = 0,
            bool block = false,
            IReadOnlyList<string>? interests = null
        )
        {
            var date = DateOnly.FromDateTime(ItineraryTestFacts.Now.DateTime).AddDays(offset);
            var brief = new TripBriefInput(
                destination,
                null,
                date,
                date.AddDays(days - 1),
                2,
                "RUB",
                null,
                interests ?? ["architecture"],
                "relaxed"
            );
            Plan = TripPlan
                .Create(
                    Guid.NewGuid(),
                    TripPlanTests.Owner,
                    "FICTIONAL private plan",
                    brief,
                    ItineraryTestFacts.Now.AddDays(-1)
                )
                .Value;
            if (block)
                Plan = TripPlan
                    .Restore(
                        Plan.TripId,
                        Plan.Owner,
                        1,
                        Plan.Title,
                        brief,
                        Plan.Destination,
                        Plan.Days.Select(d => new TripDayInput(
                                d.Date,
                                [new(Guid.NewGuid(), "Existing")]
                            ))
                            .ToArray(),
                        Plan.CreatedAt,
                        Plan.UpdatedAt
                    )
                    .Value;
            Store.Row = new(
                Plan.TripId,
                Plan.Owner,
                Plan.Revision,
                Plan.CreatedAt,
                Plan.UpdatedAt,
                "protected"
            );
            Codec.Current = Plan;
        }

        internal Task<ErrorOr<PreparedCandidateResponse>> Generate(long revision = 1) =>
            Service.GenerateAsync(
                Plan.Owner,
                Plan.TripId,
                revision,
                TestContext.Current.CancellationToken
            );

        internal Task<ErrorOr<PreparedCandidateResponse>> Generate(OwnerIdentity? owner) =>
            Service.GenerateAsync(owner, Plan.TripId, 1, TestContext.Current.CancellationToken);

        internal void Race(string mode)
        {
            if (mode == "delete")
            {
                Store.Row = null;
                return;
            }
            var row = Store.Row!;
            if (mode == "foreign")
            {
                Store.Row = row with
                {
                    Owner = OwnerIdentity.Create(row.Owner.Issuer, Guid.NewGuid()).Value,
                };
                return;
            }
            var revised = TripPlan
                .Restore(
                    Plan.TripId,
                    Plan.Owner,
                    2,
                    "Changed",
                    Plan.Brief.ToInput(),
                    Plan.Destination,
                    Plan.Days.Select(d => new TripDayInput(d.Date, [])).ToArray(),
                    Plan.CreatedAt,
                    Plan.UpdatedAt.AddSeconds(1)
                )
                .Value;
            Codec.Current = revised;
            Store.Row = row with { Revision = 2, UpdatedAt = revised.UpdatedAt };
        }

        internal void AssertNoWrites()
        {
            Store.Writes.ShouldBe(0);
            Protector.Writes.ShouldBe(0);
            Plan.Revision.ShouldBe(1);
        }
    }

    private sealed class Store : ITripPlanStore
    {
        internal TripPlanRecord? Row { get; set; }
        internal bool ReturnForeign { get; set; }
        internal int Reads { get; private set; }
        internal int Writes { get; private set; }
        internal Action<int>? BeforeLoad { get; set; }

        public Task<ErrorOr<TripPlanRecord?>> LoadAsync(
            OwnerIdentity owner,
            Guid id,
            CancellationToken ct
        )
        {
            ct.ThrowIfCancellationRequested();
            Reads++;
            BeforeLoad?.Invoke(Reads);
            var row =
                ReturnForeign && Row is { } current
                    ? current with
                    {
                        Owner = OwnerIdentity.Create(current.Owner.Issuer, Guid.NewGuid()).Value,
                    }
                    : Row;
            return Task.FromResult<ErrorOr<TripPlanRecord?>>(row);
        }

        public Task<ErrorOr<TripPlanRecordPage>> ListAsync(
            OwnerIdentity owner,
            TripPlanPosition? after,
            int limit,
            CancellationToken ct
        ) => throw new InvalidOperationException();

        public Task<ErrorOr<Success>> TryCreateAsync(
            TripPlanRecord row,
            int capacity,
            CancellationToken ct
        )
        {
            Writes++;
            throw new InvalidOperationException();
        }

        public Task<ErrorOr<Success>> TryReplaceAsync(
            long revision,
            TripPlanRecord row,
            CancellationToken ct
        )
        {
            Writes++;
            throw new InvalidOperationException();
        }

        public Task<ErrorOr<Success>> TryDeleteAsync(
            OwnerIdentity owner,
            Guid id,
            long revision,
            CancellationToken ct
        )
        {
            Writes++;
            throw new InvalidOperationException();
        }
    }

    private sealed class Protector : ITripPlanProtector
    {
        internal int Reads { get; private set; }
        internal int Writes { get; private set; }

        public Task<ErrorOr<string>> UnprotectAsync(
            string text,
            TripPlanBinding binding,
            CancellationToken ct
        )
        {
            Reads++;
            return Task.FromResult<ErrorOr<string>>("snapshot");
        }

        public Task<ErrorOr<string>> ProtectAsync(
            string text,
            TripPlanBinding binding,
            CancellationToken ct
        )
        {
            Writes++;
            throw new InvalidOperationException();
        }
    }

    private sealed class Codec : ITripPlanCodec
    {
        internal TripPlan Current { get; set; } = null!;
        internal TripPlan? ReservedPlan { get; private set; }
        internal int ReserveBytes { get; private set; }
        internal int? ReservedSize { get; set; }

        public ErrorOr<string> Serialize(TripPlan plan) => throw new InvalidOperationException();

        public ErrorOr<TripPlan> Deserialize(string text, TripPlanRecord row) => Current;

        public ErrorOr<int> MeasureDetailBytes(TripPlan plan, bool reserveMaximumServerFields)
        {
            // Same complete detail + maximum server-field reserve pattern as M1 unit codec.
            var size = Encoding.UTF8.GetByteCount(
                JsonSerializer.Serialize(
                    new
                    {
                        plan.TripId,
                        Revision = reserveMaximumServerFields ? long.MaxValue : plan.Revision,
                        plan.Title,
                        Brief = plan.Brief.ToInput(),
                        plan.Destination,
                        plan.Days,
                        CreatedAt = reserveMaximumServerFields
                            ? DateTimeOffset.MaxValue
                            : plan.CreatedAt,
                        UpdatedAt = reserveMaximumServerFields
                            ? DateTimeOffset.MaxValue
                            : plan.UpdatedAt,
                        plan.KnownUserEstimateSubtotal,
                        plan.UnknownEstimateCount,
                        plan.Warnings,
                    }
                )
            );
            if (!reserveMaximumServerFields)
                return size;
            ReservedPlan = plan;
            ReserveBytes = size;
            return ReservedSize ?? size;
        }

        public ErrorOr<int> MeasurePageBytes(TripPlanPage page) =>
            throw new InvalidOperationException();

        public ErrorOr<string> SerializePage(TripPlanPage page) =>
            throw new InvalidOperationException();
    }

    private sealed class Cursor : ITripPlanCursor
    {
        public ErrorOr<string> Encode(OwnerIdentity owner, TripPlanPosition position) =>
            throw new InvalidOperationException();

        public ErrorOr<TripPlanPosition> Decode(OwnerIdentity owner, string cursor) =>
            throw new InvalidOperationException();
    }

    private sealed class Source : IItineraryFactSource
    {
        internal ValidatedItineraryFacts Facts { get; set; } = ItineraryTestFacts.Create();
        internal Error? Error { get; set; }
        internal bool Throws { get; set; }
        internal int Calls { get; private set; }

        public Task<ErrorOr<ValidatedItineraryFacts>> ReadAsync(
            DestinationSelection destination,
            CancellationToken ct = default
        )
        {
            Calls++;
            if (Throws)
                throw new InvalidOperationException("private source exception");
            return Task.FromResult<ErrorOr<ValidatedItineraryFacts>>(
                Error is { } error ? error : Facts
            );
        }
    }

    private sealed class Encoder : IItineraryCandidateResponseEncoder
    {
        internal int Calls { get; private set; }
        internal ItineraryCandidateResult? Seen { get; private set; }
        internal Func<
            ItineraryCandidateResult,
            CancellationToken,
            Task<ErrorOr<ReadOnlyMemory<byte>>>
        > Handler { get; set; } = (_, _) => Bytes("{}"u8.ToArray());

        public Task<ErrorOr<ReadOnlyMemory<byte>>> EncodeAsync(
            ItineraryCandidateResult result,
            CancellationToken ct = default
        )
        {
            Calls++;
            Seen = result;
            return Handler(result, ct);
        }
    }

    private sealed class Clock : TimeProvider
    {
        private readonly List<ManualTimer> timers = [];
        internal DateTimeOffset Now { get; set; } = ItineraryTestFacts.Now;

        public override DateTimeOffset GetUtcNow() => Now;

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period
        )
        {
            var timer = new ManualTimer(this, callback, state, dueTime);
            timers.Add(timer);
            return timer;
        }

        internal void Advance(TimeSpan by)
        {
            Now += by;
            foreach (var timer in timers.ToArray())
                timer.Fire();
        }

        private sealed class ManualTimer(
            Clock clock,
            TimerCallback callback,
            object? state,
            TimeSpan dueTime
        ) : ITimer
        {
            private DateTimeOffset due = clock.Now + dueTime;
            private bool disposed;

            internal void Fire()
            {
                if (!disposed && clock.Now >= due)
                {
                    disposed = true;
                    callback(state);
                }
            }

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                due = clock.Now + dueTime;
                return !disposed;
            }

            public void Dispose() => disposed = true;

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }
}
